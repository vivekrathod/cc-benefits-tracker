using Chitragupta.Application.Tests.Support;
using Chitragupta.Contracts;
using static Chitragupta.Application.Tests.Support.FakeFeed;

namespace Chitragupta.Application.Tests;

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
        Assert.Equal("Chitragupta this week: 2 to use", digest.Subject);
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
        var alex = await tracker.PersonAsync();
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "American AAdvantage", 45_210, DateOnly.Parse("2027-03-01")), Ct);

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

    [Fact]
    public async Task A_check_only_a_person_can_fix_is_said_once_again_after_two_days_and_once_more_when_it_works()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-24 13:00");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-24T17:30:00+00:00"), Ct);
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-25 13:00");
        await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", "needs_user", "the sign-in form came back", "2026-11-25T17:30:00+00:00", code: "login_rejected"), Ct);
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        var alert = tracker.Notifier.Sent.Single();
        Assert.Equal("Balance check needs you: alex-aa (American AAdvantage)", alert.Subject);
        Assert.Contains("Needs you:\n- alex-aa (American AAdvantage): needs you since Nov 25, 12:30 PM. the sign-in form came back", alert.Body);
        Assert.Contains("What to do: The site refused the stored login", alert.Body);
        Assert.Contains("Every balance stays as it was last read.", alert.Body);

        // The next day the automation holds the account and says so again. That is the same failure, not news.
        tracker.Clock.Set("2026-11-26 13:00");
        await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", "needs_user", "live checks held", null, code: "login_rejected", attempted: false), Ct);
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-27 13:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal("Balance check still failing: alex-aa (American AAdvantage)", tracker.Notifier.Sent[1].Subject);

        tracker.Clock.Set("2026-11-28 13:00");
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_900, "2026-11-28T17:30:00+00:00"), Ct);
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal("Balance check works again: alex-aa (American AAdvantage)", tracker.Notifier.Sent[2].Subject);
        Assert.Contains("read again Nov 28, 12:30 PM", tracker.Notifier.Sent[2].Body);
    }

    [Theory]
    [InlineData("failed", "otp_timeout", true)]
    [InlineData("blocked", "bot_wall", false)]
    public async Task A_failure_that_clears_by_itself_is_only_said_once_it_has_lasted_two_days(string status, string code, bool retryable)
    {
        await using var tracker = await Tracker.StartAsync("2026-11-24 13:00");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", status, "not this time", "2026-11-24T17:30:00+00:00", code: code, retryable: retryable), Ct);
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        // It works the next day. Nobody was told it failed, so nobody is told it works.
        tracker.Clock.Set("2026-11-25 13:00");
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T17:30:00+00:00"), Ct);
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        foreach (var day in new[] { "26", "27", "28" })
        {
            tracker.Clock.Set($"2026-11-{day} 13:00");
            await tracker.Balances.ImportAsync(
                Udaan.Failed("alex-aa", "aa", "alex", status, "not this time", $"2026-11-{day}T17:30:00+00:00", code: code, retryable: retryable), Ct);
        }

        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
        Assert.Contains("Still failing:\n- alex-aa (American AAdvantage)", tracker.Notifier.Sent.Single().Body);
    }

    [Fact]
    public async Task An_account_nothing_was_heard_about_for_over_a_day_is_said_once()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-25 13:00");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(
            Udaan.All(
                Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T17:30:00+00:00"),
                Udaan.Ok("alex-united", "united", "alex", 7_000, "2026-11-25T17:45:00+00:00")),
            Ct);

        tracker.Clock.Set("2026-11-26 14:00");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-26 15:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        tracker.Clock.Set("2026-11-27 15:00");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        var alert = tracker.Notifier.Sent.Single();
        Assert.Equal("Balance checks: 2 things to know", alert.Subject);
        Assert.Contains("Not checked:\n- alex-aa (American AAdvantage): nothing heard since Nov 25, 12:30 PM.", alert.Body);
    }

    [Fact]
    public async Task An_automation_that_stops_answering_is_said_once_after_half_an_hour_and_its_quiet_accounts_are_not_listed_too()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-25 13:00");
        await tracker.PersonAsync();
        tracker.Checker.IsConfigured = true;
        tracker.Checker.Accounts.Add("alex-aa");
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T17:30:00+00:00"), Ct);
        tracker.Checker.Outage = "udaan's service did not answer.";

        tracker.Clock.Set("2026-11-26 12:30");
        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-26 12:50");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        tracker.Clock.Set("2026-11-26 13:00");
        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
        // By the evening the account is overdue as well, for the reason already given.
        tracker.Clock.Set("2026-11-26 20:00");
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));

        var alert = tracker.Notifier.Sent.Single();
        Assert.Equal("The balance automation is not answering", alert.Subject);
        Assert.Contains("has not answered since Nov 26, 12:30 PM", alert.Body);

        // It comes back and catches up, and the account is never reported as quiet.
        tracker.Checker.Outage = null;
        tracker.Checker.Answer = account => Application.Ports.BalanceCheckAnswer.ComeBackAt(tracker.Clock.Now.AddMinutes(15));
        await tracker.Runner.RunDueAsync(Ct);
        Assert.Equal(0, await tracker.Reminders.RunAsync(Ct));
    }

    [Fact]
    public async Task An_alert_that_could_not_be_sent_goes_out_on_the_next_run()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-25 13:00");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", "needs_user", "the sign-in form came back", "2026-11-25T17:30:00+00:00", code: "login_rejected"), Ct);

        tracker.Notifier.Broken = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.Reminders.RunAsync(Ct));
        tracker.Notifier.Broken = false;

        Assert.Equal(1, await tracker.Reminders.RunAsync(Ct));
    }

    [Fact]
    public async Task The_digest_says_how_the_automatic_checks_are_doing()
    {
        // Sunday morning.
        await using var tracker = await Tracker.StartAsync("2026-11-29 09:30");
        await tracker.PersonAsync();
        await tracker.Balances.ImportAsync(
            Udaan.All(
                Udaan.Ok("alex-united", "united", "alex", 7_000, "2026-11-29T13:00:00+00:00"),
                Udaan.Failed("alex-aa", "aa", "alex", "blocked", "site challenge", "2026-11-29T13:10:00+00:00", code: "bot_wall")),
            Ct);

        await tracker.Reminders.RunAsync(Ct);

        var digest = tracker.Notifier.Sent.Single(sent => sent.Subject.StartsWith("Chitragupta this week", StringComparison.Ordinal));
        Assert.Contains("Automatic balance checks: 1 of 2 need a look.\n- alex-aa (American AAdvantage): blocked by the site since Nov 29, 8:10 AM", digest.Body);
    }
}
