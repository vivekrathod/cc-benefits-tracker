using System.Globalization;
using System.Text;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Views;
using CardBenefits.Contracts;

namespace CardBenefits.Application.Reminders;

/// <summary>
/// Sends the nudges before a period with value left runs out, and the weekly digest.
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

        if (await SendDigestAsync(cancellationToken))
        {
            sent++;
        }

        return sent;
    }

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
