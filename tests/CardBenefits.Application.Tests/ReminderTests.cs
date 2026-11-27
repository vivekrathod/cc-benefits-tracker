using CardBenefits.Application.Tests.Support;
using CardBenefits.Contracts;
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

    [Fact]
    public async Task Miles_get_one_reminder_ninety_days_before_they_lapse_and_one_at_thirty()
    {
        // 103 days before the miles lapse on March 1.
        await using var tracker = await Tracker.StartAsync("2026-11-18 10:00");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(
            Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-18T12:00:00+00:00", expires: "2027-03-01"), Ct);

        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-12-01 10:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2027-01-30 10:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        Assert.Equal(2, tracker.Notifier.Sent.Count);
        var first = tracker.Notifier.Sent[0];
        Assert.Equal("Miles expire soon: American AAdvantage (Alex)", first.Subject);
        Assert.Contains("- American AAdvantage (Alex): 45,210 expire Mar 1, 2027 (90 days)", first.Body);
        Assert.Contains("(30 days)", tracker.Notifier.Sent[1].Body);
    }

    [Fact]
    public async Task Miles_whose_last_day_moved_out_are_left_alone_until_the_new_day_comes_near()
    {
        await using var tracker = await Tracker.StartAsync("2026-12-01 10:00");
        var alex = await tracker.PersonAsync();
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 9_000, DateOnly.Parse("2027-03-01")), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Flying Blue", 400, DateOnly.Parse("2027-02-10")), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "No date", 400, null), Ct);
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal("2 miles and points balances expire soon", tracker.Notifier.Sent.Single().Subject);

        // A flight credited the next day, and the program pushed the day out by two years.
        tracker.Clock.Set("2026-12-02 10:00");
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 9_500, DateOnly.Parse("2028-12-02")), Ct);

        tracker.Clock.Set("2027-01-30 10:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        var second = tracker.Notifier.Sent[1];
        Assert.Equal("Miles expire soon: Flying Blue (Alex)", second.Subject);
        Assert.DoesNotContain("Avios", second.Body);

        // A Sunday, before the digest goes out.
        tracker.Clock.Set("2028-09-03 08:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Contains("- Avios (Alex): 9,500 expire Dec 2, 2028 (90 days)", tracker.Notifier.Sent[2].Body);
    }

    [Fact]
    public async Task The_weekly_digest_lists_miles_that_lapse_in_the_next_ninety_days()
    {
        // Sunday morning, after the first reminder for these miles already went out.
        await using var tracker = await Tracker.StartAsync("2026-11-07 20:00");
        var alex = await tracker.PersonAsync();
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 9_000, DateOnly.Parse("2027-01-15")), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Flying Blue", 400, DateOnly.Parse("2027-06-30")), Ct);
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-08 09:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));

        var digest = tracker.Notifier.Sent[1];
        Assert.Contains("Miles and points that lapse in the next 90 days:", digest.Body);
        Assert.Contains("- Avios (Alex): 9,000 expire Jan 15, 2027 (68 days)", digest.Body);
        Assert.DoesNotContain("Flying Blue", digest.Body);
    }
}
