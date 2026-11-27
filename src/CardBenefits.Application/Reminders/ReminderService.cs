using System.Globalization;
using System.Text;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Views;
using CardBenefits.Contracts;

namespace CardBenefits.Application.Reminders;

/// <summary>
/// Sends the nudges before a period with value left runs out or miles lapse, and the weekly digest.
/// It is safe to call as often as the host likes: each reminder is recorded when it
/// goes out and is never sent twice.
/// </summary>
public sealed class ReminderService(
    ITrackerStore store, TrackerQueries queries, INotifier notifier, IClock clock, TrackerSettings settings)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var sent = 0;
        if (await SendNudgesAsync(cancellationToken))
        {
            sent++;
        }

        if (await SendBalanceNudgesAsync(cancellationToken))
        {
            sent++;
        }

        if (await SendDigestAsync(cancellationToken))
        {
            sent++;
        }

        return sent;
    }

    private async Task<bool> SendBalanceNudgesAsync(CancellationToken cancellationToken)
    {
        var thresholds = settings.BalanceNudgeDaysBefore.OrderDescending().ToArray();
        if (thresholds.Length == 0)
        {
            return false;
        }

        var due = new List<(BalanceDto Balance, string Key)>();
        foreach (var balance in await GetLapsingBalancesAsync(thresholds[0], cancellationToken))
        {
            // The day is part of the key: activity in the program moves it out, and the new day earns its own reminders.
            var threshold = thresholds.Last(days => balance.DaysUntilExpiry <= days);
            var key = $"miles:{balance.PersonId}:{balance.Program.ToUpperInvariant()}:{balance.ExpiresOn:yyyy-MM-dd}:{threshold}";
            if (await store.TryRecordReminderAsync(key, clock.Now, cancellationToken))
            {
                due.Add((balance, key));
            }
        }

        if (due.Count == 0)
        {
            return false;
        }

        var body = new StringBuilder("These miles and points are due to lapse:\n\n");
        AppendBalances(body, due.Select(item => item.Balance));
        var subject = due.Count == 1
            ? $"Miles expire soon: {due[0].Balance.Program} ({due[0].Balance.PersonName})"
            : $"{due.Count} miles and points balances expire soon";

        return await SendAsync(new Notification(subject, body.ToString()), due.Select(item => item.Key), cancellationToken);
    }

    /// <summary>Balances with something to lose that lapse within the given days, soonest first.</summary>
    private async Task<IReadOnlyList<BalanceDto>> GetLapsingBalancesAsync(int days, CancellationToken cancellationToken) =>
        (await queries.GetBalancesAsync(cancellationToken)).Balances
            .Where(balance => balance.DaysUntilExpiry is { } left && left >= 0 && left <= days)
            .OrderBy(balance => balance.DaysUntilExpiry)
            .ToArray();

    private async Task<bool> SendNudgesAsync(CancellationToken cancellationToken)
    {
        var thresholds = settings.NudgeDaysBefore.OrderDescending().ToArray();
        if (thresholds.Length == 0)
        {
            return false;
        }

        var due = new List<(PeriodDto Period, string Key)>();
        foreach (var period in await queries.GetExpiringSoonAsync(thresholds[0], cancellationToken))
        {
            // The tightest threshold reached decides which nudge this is, so a period first
            // noticed two days out gets one nudge, not two.
            var threshold = thresholds.Last(days => period.DaysLeft <= days);
            var key = $"nudge:{period.Id}:{threshold}";
            if (await store.TryRecordReminderAsync(key, clock.Now, cancellationToken))
            {
                due.Add((period, key));
            }
        }

        if (due.Count == 0)
        {
            return false;
        }

        var body = new StringBuilder("These benefits still have value left and end soon:\n\n");
        AppendPeriods(body, due.Select(item => item.Period));
        var subject = due.Count == 1
            ? $"Expiring soon: {due[0].Period.BenefitName} ({due[0].Period.CardName})"
            : $"{due.Count} card benefits expire soon";

        return await SendAsync(new Notification(subject, body.ToString()), due.Select(item => item.Key), cancellationToken);
    }

    private async Task<bool> SendDigestAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        if (now.DayOfWeek != settings.DigestDay || TimeOnly.FromDateTime(now.DateTime) < settings.DigestAt)
        {
            return false;
        }

        var key = $"digest:{clock.Today:yyyy-MM-dd}";
        if (!await store.TryRecordReminderAsync(key, now, cancellationToken))
        {
            return false;
        }

        var expiring = await queries.GetExpiringSoonAsync(30, cancellationToken);
        var review = await store.GetReviewItemsAsync(cancellationToken);
        var body = new StringBuilder();

        if (expiring.Count == 0)
        {
            body.Append("Nothing with value left ends in the next 30 days.\n");
        }
        else
        {
            body.Append("Ending in the next 30 days with value left:\n\n");
            AppendPeriods(body, expiring);
        }

        var horizon = settings.BalanceNudgeDaysBefore.DefaultIfEmpty(0).Max();
        var lapsing = await GetLapsingBalancesAsync(horizon, cancellationToken);
        if (lapsing.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"\nMiles and points that lapse in the next {horizon} days:\n\n");
            AppendBalances(body, lapsing);
        }

        if (review.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"\n{review.Count} credit(s) are waiting in the review queue.\n");
        }

        var subject = $"Card benefits this week: {expiring.Count} to use";
        return await SendAsync(new Notification(subject, body.ToString()), [key], cancellationToken);
    }

    private async Task<bool> SendAsync(Notification notification, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.SendAsync(notification, cancellationToken);
            return true;
        }
        catch
        {
            // Not delivered, so not sent: forget the record and the next run tries again.
            foreach (var key in keys)
            {
                await store.ForgetReminderAsync(key, CancellationToken.None);
            }

            throw;
        }
    }

    private static void AppendBalances(StringBuilder body, IEnumerable<BalanceDto> balances)
    {
        foreach (var balance in balances)
        {
            var when = balance.DaysUntilExpiry switch
            {
                0 => "expire today",
                1 => "expire tomorrow",
                var days => string.Create(CultureInfo.InvariantCulture, $"expire {balance.ExpiresOn:MMM d, yyyy} ({days} days)"),
            };
            body.Append(CultureInfo.InvariantCulture, $"- {balance.Program} ({balance.PersonName}): {balance.Amount:#,0} {when}\n");
        }
    }

    private static void AppendPeriods(StringBuilder body, IEnumerable<PeriodDto> periods)
    {
        foreach (var period in periods)
        {
            var left = period.HasDollarValue
                ? string.Create(CultureInfo.InvariantCulture, $"${period.Remaining:0.00} left")
                : "not used yet";
            var when = period.DaysLeft switch
            {
                0 => "ends today",
                1 => "ends tomorrow",
                _ => string.Create(CultureInfo.InvariantCulture, $"ends {period.End:MMM d} ({period.DaysLeft} days)"),
            };
            var manual = period.Manual ? " [mark it used yourself]" : "";
            body.Append(CultureInfo.InvariantCulture, $"- {period.BenefitName}, {period.CardName} ({period.PersonName}): {left}, {when}{manual}\n");
        }
    }
}
