using CardBenefits.Application.Ports;
using CardBenefits.Contracts;
using CardBenefits.Domain;

namespace CardBenefits.Application.Balances;

/// <summary>
/// Keeps miles and points balances. Readings arrive from outside, pushed by an automation
/// or typed in: the tracker never signs in to a loyalty program itself. Every reading is
/// kept, and the newest one is the balance.
/// </summary>
public sealed class BalanceService(ITrackerStore store, IBalanceReportParser parser, IClock clock)
{
    /// <summary>Takes an automation's output as it was printed. Sending the same output again changes nothing.</summary>
    public async Task<IReadOnlyList<BalanceImportLineDto>> ImportAsync(string report, CancellationToken cancellationToken)
    {
        var reports = parser.Parse(report);
        var people = await store.GetPeopleAsync(cancellationToken);

        var lines = new List<BalanceImportLineDto>();
        foreach (var item in reports)
        {
            lines.Add(await ImportAsync(item, people, cancellationToken));
        }

        return lines;
    }

    public async Task RecordAsync(RecordBalanceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Program))
        {
            throw new RequestRejectedException("A balance needs the name of its program.");
        }

        if (request.Amount < 0)
        {
            throw new RequestRejectedException("A balance cannot be negative.");
        }

        var people = await store.GetPeopleAsync(cancellationToken);
        if (people.All(person => person.Id != request.PersonId))
        {
            throw new NotFoundException("The person");
        }

        var known = await FindAsync(request.PersonId, request.Program, cancellationToken);
        await store.AddBalanceAsync(
            new ProgramBalance(
                request.PersonId, known?.Program ?? request.Program, request.Amount, clock.Now, BalanceSource.Manual, request.ExpiresOn),
            cancellationToken);
    }

    public async Task RemoveAsync(Guid personId, string program, CancellationToken cancellationToken)
    {
        var known = await FindAsync(personId, program, cancellationToken) ?? throw new NotFoundException("The balance");
        await store.DeleteBalancesAsync(known.PersonId, known.Program, cancellationToken);
    }

    private async Task<BalanceImportLineDto> ImportAsync(
        BalanceReport report, IReadOnlyList<Person> people, CancellationToken cancellationToken)
    {
        var owner = FindOwner(people, report.Owner);
        var recorded = false;

        if (owner is not null && report.Balance is { } reading)
        {
            // The same person and program keep one spelling, the first one seen.
            var known = await FindAsync(owner.Id, report.Program, cancellationToken);
            if (known is null || reading.AsOf > known.AsOf)
            {
                await store.AddBalanceAsync(
                    new ProgramBalance(
                        owner.Id, known?.Program ?? report.Program, reading.Amount, reading.AsOf, BalanceSource.Automation, reading.ExpiresOn),
                    cancellationToken);
                recorded = true;
            }
        }

        // A check that worked for an owner nobody goes by is still a problem, just the tracker's own.
        var unplaced = owner is null && report.Status == BalanceCheckStatus.Ok;
        var check = new BalanceCheck(
            report.Account,
            report.Owner,
            report.Program,
            unplaced ? BalanceCheckStatus.UnknownOwner : report.Status,
            unplaced
                ? $"Nobody in the tracker goes by \"{report.Owner}\". Rename a person on the Manage screen, or change the owner in the automation."
                : report.Problem,
            report.CheckedAt ?? clock.Now);

        var checks = await store.GetBalanceChecksAsync(cancellationToken);
        var last = checks.FirstOrDefault(candidate => candidate.Account == check.Account);
        if (last is null || check.CheckedAt >= last.CheckedAt)
        {
            await store.SaveBalanceCheckAsync(check, cancellationToken);
        }

        return new BalanceImportLineDto(check.Account, check.Status.ToString(), recorded, check.Problem);
    }

    private async Task<ProgramBalance?> FindAsync(Guid personId, string program, CancellationToken cancellationToken) =>
        (await store.GetLatestBalancesAsync(cancellationToken)).FirstOrDefault(balance => balance.IsFor(personId, program));

    /// <summary>
    /// An automation names an owner in its own words. That is a person's whole name or,
    /// while only one person has it, their first name. Anything else waits for a person
    /// to sort out: a result never creates somebody.
    /// </summary>
    private static Person? FindOwner(IReadOnlyList<Person> people, string owner)
    {
        var name = owner.Trim();
        var whole = people.Where(person => person.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (whole.Length == 1)
        {
            return whole[0];
        }

        var first = people
            .Where(person => person.Name.Split(' ', 2)[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return first.Length == 1 ? first[0] : null;
    }
}
