using System.Globalization;
using System.Text;
using Chitragupta.Application.Balances;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Views;
using Chitragupta.Contracts;

namespace Chitragupta.Application.Reminders;

/// <summary>
/// Sends the nudges before a period with value left runs out or miles lapse, the alerts when an
/// automatic balance check needs somebody, and the weekly digest.
/// It is safe to call as often as the host likes: each reminder is recorded when it
/// goes out and is never sent twice.
/// </summary>
public sealed class ReminderService(
    ITrackerStore store,
    TrackerQueries queries,
    INotifier notifier,
    IClock clock,
    TrackerSettings settings,
    BalanceCheckerState checkerState)
{
    /// <summary>After the automation comes back, the accounts take a while to be checked one by one.</summary>
    private static readonly TimeSpan CatchUp = TimeSpan.FromHours(3);

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

        if (await SendCheckAlertsAsync(cancellationToken))
        {
            sent++;
        }

        if (await SendDigestAsync(cancellationToken))
        {
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// One mail for whatever is new about the automatic checks: a failure only a person can clear,
    /// a failure that has lasted, a check that works again after an alert, an account nothing
    /// was heard about, and an automation that stopped answering. Each is said once.
    /// </summary>
    private async Task<bool> SendCheckAlertsAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var asked = checkerState.Read();
        var histories = CheckHistory.ByAccount(
            await store.GetBalanceAttemptsAsync(now.AddDays(-settings.BalanceHistoryDays), cancellationToken));

        var needsYou = new List<string>();
        var stillFailing = new List<string>();
        var recovered = new List<string>();
        var quiet = new List<string>();
        var keys = new List<string>();
        string? first = null;

        async Task<bool> NewAsync(string key)
        {
            if (!await store.TryRecordReminderAsync(key, now, cancellationToken))
            {
                return false;
            }

            keys.Add(key);
            return true;
        }

        foreach (var history in histories)
        {
            var latest = history.Latest;
            var name = $"{latest.Account} ({latest.Program})";
            if (history.FailingSince is { } since)
            {
                var told = latest.NeedsAPerson && await NewAsync(AlertKey("needs-you", latest.Account, since));
                var lasting = now - since >= TimeSpan.FromDays(settings.BalanceStillFailingAfterDays)
                    && await NewAsync(AlertKey("still-failing", latest.Account, since));
                if (told || lasting)
                {
                    var line = new StringBuilder();
                    line.Append(CultureInfo.InvariantCulture, $"- {name}: {CheckHistory.Words(latest.Status)} since {Moment(since)}.");
                    line.Append(latest.Problem is null ? "" : $" {latest.Problem}");
                    line.Append(latest.Advice is null ? "\n" : $"\n  What to do: {latest.Advice}\n");
                    (told ? needsYou : stillFailing).Add(line.ToString());
                    first ??= told ? $"Balance check needs you: {name}" : $"Balance check still failing: {name}";
                }
            }
            else if (history.RecoveredFrom is { } from
                && (await store.HasReminderAsync(AlertKey("needs-you", latest.Account, from), cancellationToken)
                    || await store.HasReminderAsync(AlertKey("still-failing", latest.Account, from), cancellationToken))
                && await NewAsync(AlertKey("recovered", latest.Account, from)))
            {
                recovered.Add(string.Create(CultureInfo.InvariantCulture, $"- {name}: read again {Moment(latest.CheckedAt)}.\n"));
                first ??= $"Balance check works again: {name}";
            }

            // While the automation is away, or has only just come back, a quiet account is its doing and is said once, below.
            var catchingUp = asked.UnreachableSince is not null || now - asked.ReachableSince < CatchUp;
            if (!catchingUp
                && now - latest.CheckedAt >= TimeSpan.FromHours(settings.BalanceOverdueAfterHours)
                && await NewAsync(AlertKey("overdue", latest.Account, latest.CheckedAt)))
            {
                quiet.Add(string.Create(CultureInfo.InvariantCulture, $"- {name}: nothing heard since {Moment(latest.CheckedAt)}.\n"));
                first ??= $"Balance check has gone quiet: {name}";
            }
        }

        string? down = null;
        if (asked.UnreachableSince is { } away
            && now - away >= TimeSpan.FromMinutes(settings.BalanceCheckerDownAfterMinutes)
            && await NewAsync($"balance-check:down:{away.ToUnixTimeMilliseconds()}"))
        {
            down = string.Create(
                CultureInfo.InvariantCulture,
                $"The balance automation has not answered since {Moment(away)}, so no balance can be checked. Where udaan runs, see that udaan's service is running.\n");
            first ??= "The balance automation is not answering";
        }

        var count = needsYou.Count + stillFailing.Count + recovered.Count + quiet.Count + (down is null ? 0 : 1);
        if (count == 0)
        {
            return false;
        }

        var body = new StringBuilder();
        Section(body, "Needs you:", needsYou);
        Section(body, "Still failing:", stillFailing);
        Section(body, "Working again:", recovered);
        Section(body, "Not checked:", quiet);
        body.Append(down is null ? "" : down + "\n");
        body.Append("Every balance stays as it was last read. The Points screen has the details.\n");

        var subject = count == 1 ? first! : $"Balance checks: {count} things to know";
        return await SendAsync(new Notification(subject, body.ToString()), keys, cancellationToken);
    }

    private static string AlertKey(string kind, string account, DateTimeOffset since) =>
        $"balance-check:{kind}:{account}:{since.ToUnixTimeMilliseconds()}";

    private string Moment(DateTimeOffset instant) =>
        clock.ToLocal(instant).ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);

    private static void Section(StringBuilder body, string title, List<string> lines)
    {
        if (lines.Count > 0)
        {
            body.Append(title).Append('\n').AppendJoin("", lines).Append('\n');
        }
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

        var points = await queries.GetBalancesAsync(cancellationToken);
        if (points.Checks.Count > 0)
        {
            var wrong = points.Checks.Where(check => check.Status != "Ok" || check.Overdue).ToArray();
            body.Append(
                CultureInfo.InvariantCulture,
                $"\nAutomatic balance checks: {(wrong.Length == 0 ? $"all {points.Checks.Count} ok" : $"{wrong.Length} of {points.Checks.Count} need a look")}.\n");
            foreach (var check in wrong)
            {
                var what = check.Status == "Ok"
                    ? $"nothing heard since {Moment(check.CheckedAt)}"
                    : $"{CheckHistory.Words(Enum.Parse<Domain.BalanceCheckStatus>(check.Status))} since {Moment(check.FailingSince ?? check.CheckedAt)}";
                body.Append(CultureInfo.InvariantCulture, $"- {check.Account} ({check.Program}): {what}\n");
            }
        }

        if (points.Checker?.UnreachableSince is { } away)
        {
            body.Append(CultureInfo.InvariantCulture, $"\nThe balance automation has not answered since {Moment(away)}.\n");
        }

        var subject = $"Chitragupta this week: {expiring.Count} to use";
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
