using CardBenefits.Application.Tests.Support;
using static CardBenefits.Application.Tests.Support.FakeFeed;

namespace CardBenefits.Application.Tests;

public class ReminderTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task A_period_with_value_left_gets_one_nudge_at_seven_days_and_one_at_two()
    {
        // Wednesday the 18th: November's monthly credits end on the 30th, twelve days out.
        await using var tracker = await Tracker.StartAsync("2026-11-18 10:00");
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-09", 10, "AMEX DINING CREDIT"));

        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-23 10:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-28 10:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        Assert.Equal(2, tracker.Notifier.Sent.Count);
        var first = tracker.Notifier.Sent[0];
        Assert.Equal("Expiring soon: Uber Cash (Amex Gold)", first.Subject);
        Assert.Contains("Uber Cash, Amex Gold (Alex): $10.00 left, ends Nov 30 (7 days) [mark it used yourself]", first.Body);
        Assert.DoesNotContain("Dining", first.Body);
        Assert.Contains("(2 days)", tracker.Notifier.Sent[1].Body);
    }

    [Fact]
    public async Task A_period_that_gets_used_is_not_nudged_again()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-23 10:00");
        await tracker.CardAsync();
        await tracker.Reminders.RunAsync(Ct);
        Assert.Equal("2 card benefits expire soon", tracker.Notifier.Sent.Single().Subject);

        foreach (var benefit in new[] { TestCatalog.Uber, TestCatalog.Dining })
        {
            await tracker.Usage.MarkUsedAsync((await tracker.PeriodAsync(benefit, "2026-11-23")).Id, null, null, Ct);
        }

        tracker.Clock.Set("2026-11-28 10:00");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
    }

    [Fact]
    public async Task The_weekly_digest_goes_out_once_on_sunday_morning()
    {
        // Saturday evening.
        await using var tracker = await Tracker.StartAsync("2026-11-07 20:00");
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-05", 31.07m, "AMAZON REFUND"));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-08 08:59");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
        tracker.Clock.Set("2026-11-08 09:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        tracker.Clock.Set("2026-11-08 15:00");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        var digest = tracker.Notifier.Sent.Single();
        Assert.Equal("Card benefits this week: 2 to use", digest.Subject);
        Assert.Contains("Dining credit, Amex Gold (Alex): $10.00 left, ends Nov 30 (22 days)", digest.Body);
        Assert.Contains("1 credit(s) are waiting in the review queue.", digest.Body);
    }

    [Fact]
    public async Task A_reminder_that_could_not_be_sent_is_tried_again()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-23 10:00");
        await tracker.CardAsync();

        tracker.Notifier.Broken = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.Reminders.RunAsync(Ct));

        tracker.Notifier.Broken = false;
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Single(tracker.Notifier.Sent);
    }
}
