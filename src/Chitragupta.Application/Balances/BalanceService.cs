using Chitragupta.Application.Ports;
using Chitragupta.Contracts;
using Chitragupta.Domain;

namespace Chitragupta.Application.Balances;

/// <summary>
/// Keeps miles and points balances. Readings arrive from an automation's results or are typed
/// in: the tracker never signs in to a loyalty program itself. Every reading is kept, and the
/// newest one is the balance. Every attempt is kept too, so a failure has a history.
/// </summary>
public sealed class BalanceService(ITrackerStore store, IBalanceReportParser parser, IClock clock, TrackerSettings settings)
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
        var owner = Owners.Find(people, report.Owner);
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

        if (report.Deferred)
        {
            // "Not now" is an answer about timing, not about the account. There is nothing to keep.
            return new BalanceImportLineDto(report.Account, "Wait", recorded, report.Problem);
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
            report.CheckedAt ?? clock.Now)
        {
            ErrorCode = report.ErrorCode,
            Retryable = report.Retryable,
            SignedIn = report.SignedIn,
            Advice = report.Advice,
            DurationSeconds = report.DurationSeconds,
            ToolVersion = report.ToolVersion,
            Trail = report.Trail,
        };
        await store.SaveBalanceCheckAsync(check, cancellationToken);

        return new BalanceImportLineDto(check.Account, check.Status.ToString(), recorded, check.Problem);
    }

    /// <summary>Removes attempts older than the history that is kept. The newest attempt of every account stays.</summary>
    public Task<int> TrimHistoryAsync(CancellationToken cancellationToken) =>
        store.DeleteBalanceAttemptsBeforeAsync(clock.Now.AddDays(-settings.BalanceHistoryDays), cancellationToken);

    /// <summary>Forgets an account's checks, for one the automation no longer has. Its balances stay.</summary>
    public async Task ForgetChecksAsync(string account, CancellationToken cancellationToken)
    {
        if (await store.DeleteBalanceChecksAsync(account, cancellationToken) == 0)
        {
            throw new NotFoundException("The account");
        }
    }

    private async Task<ProgramBalance?> FindAsync(Guid personId, string program, CancellationToken cancellationToken) =>
        (await store.GetLatestBalancesAsync(cancellationToken)).FirstOrDefault(balance => balance.IsFor(personId, program));
}
