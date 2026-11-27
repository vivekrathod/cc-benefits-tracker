using Chitragupta.Application.Balances;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Tests.Support;

namespace Chitragupta.Application.Tests;

/// <summary>When the tracker asks the automation for a check, and when it holds back.</summary>
public class BalanceCheckRunnerTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task Nothing_is_asked_before_the_daily_time_and_then_each_account_once_one_at_a_time()
    {
        await using var tracker = await StartAsync("2026-11-27 12:29", "alex-aa", "alex-united");

        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        Assert.Empty(tracker.Checker.Asked);

        tracker.Clock.Set("2026-11-27 12:30");
        var first = await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 12:31");
        var second = await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 12:32");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));

        Assert.Equal(new BalanceCheckRun(BalanceCheckKind.Scheduled, "American AAdvantage", "Ok", null, true), first);
        Assert.Equal(BalanceCheckKind.Scheduled, second!.Kind);
        // Anything read since midnight is good enough for the day's check.
        Assert.Equal(
            [("alex-aa", TimeSpan.Parse("12:30")), ("alex-united", TimeSpan.Parse("12:31"))],
            tracker.Checker.Asked);
        Assert.Equal(2, (await tracker.Queries.GetBalancesAsync(Ct)).Balances.Count);

        // The next day starts over.
        tracker.Clock.Set("2026-11-28 12:30");
        Assert.NotNull(await tracker.Runner.RunDueAsync(Ct));
    }

    [Fact]
    public async Task Two_accounts_of_one_program_are_dealt_as_far_apart_as_the_list_allows()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-alaska", "alex-aa", "sam-aa");
        await tracker.PersonAsync("Sam");
        tracker.Checker.Answer = account => BalanceCheckAnswer.Result(
            Udaan.Ok(account, account.Split('-')[1], account.Split('-')[0], 1_000, tracker.Clock.Now.ToString("o")));

        foreach (var minute in new[] { "30", "45", "59" })
        {
            tracker.Clock.Set($"2026-11-27 12:{minute}");
            await tracker.Runner.RunDueAsync(Ct);
        }

        Assert.Equal(["alex-aa", "alex-alaska", "sam-aa"], tracker.Checker.Asked.Select(asked => asked.Account));
    }

    [Fact]
    public async Task An_account_already_heard_about_today_is_not_asked_again()
    {
        await using var tracker = await StartAsync("2026-11-27 09:00", "alex-aa");
        // Somebody ran udaan by hand in the morning and the result was pushed in.
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-27T14:00:00+00:00"), Ct);

        tracker.Clock.Set("2026-11-27 12:30");

        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        Assert.Empty(tracker.Checker.Asked);
    }

    [Fact]
    public async Task A_failure_that_may_clear_by_itself_gets_one_retry_four_hours_later_and_never_a_third_sign_in()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        tracker.Checker.Answer = account => Failed(tracker, account, "failed", "otp_timeout", retryable: true);

        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 16:29");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));

        tracker.Clock.Set("2026-11-27 16:30");
        var retry = await tracker.Runner.RunDueAsync(Ct);

        Assert.Equal((BalanceCheckKind.Retry, "Failed", "otp_timeout"), (retry!.Kind, retry.Status, retry.ErrorCode));
        // Only a reading taken since the failure will do.
        Assert.Equal(TimeSpan.Parse("03:59:59"), tracker.Checker.Asked[^1].MaxAge);

        tracker.Clock.Set("2026-11-27 21:00");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        Assert.Equal(2, tracker.Checker.Asked.Count);
    }

    [Theory]
    [InlineData("needs_user", "login_rejected")]
    [InlineData("blocked", "bot_wall")]
    [InlineData("failed", "parse_failed")]
    public async Task A_failure_that_trying_again_cannot_help_is_never_retried(string status, string code)
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        tracker.Checker.Answer = account => Failed(tracker, account, status, code, retryable: false);

        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 20:00");

        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        Assert.Single(tracker.Checker.Asked);
    }

    [Fact]
    public async Task An_account_the_automation_holds_is_asked_once_a_day_and_the_refusal_is_kept()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        tracker.Checker.Answer = account => BalanceCheckAnswer.Result(
            Udaan.Failed(account, "aa", "alex", "needs_user", "live checks held since a rejected login", null, code: "login_rejected", attempted: false));

        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 12:31");
        await tracker.Runner.RunDueAsync(Ct);

        Assert.Single(tracker.Checker.Asked);
        var check = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Checks);
        Assert.Equal(("NeedsUser", 0), (check.Status, check.SignInsToday));
    }

    [Fact]
    public async Task When_the_automation_says_not_now_nothing_is_asked_until_the_time_it_gave()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa", "alex-united");
        var ok = tracker.Checker.Answer;
        tracker.Checker.Answer = account =>
            account == "alex-united" ? BalanceCheckAnswer.ComeBackAt(tracker.Clock.Now.AddMinutes(15)) : ok(account);

        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 12:31");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        tracker.Clock.Set("2026-11-27 12:40");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        Assert.Equal(2, tracker.Checker.Asked.Count);

        tracker.Checker.Answer = ok;
        tracker.Clock.Set("2026-11-27 12:46");
        Assert.Equal("United MileagePlus", (await tracker.Runner.RunDueAsync(Ct))!.Program);
    }

    [Fact]
    public async Task A_check_somebody_asks_for_goes_first_and_wants_a_reading_from_the_last_hour()
    {
        await using var tracker = await StartAsync("2026-11-27 09:00", "alex-aa", "alex-united");
        await tracker.Runner.RunDueAsync(Ct);

        await tracker.Runner.RequestAsync("alex-united", Ct);
        var waiting = (await tracker.Queries.GetBalancesAsync(Ct)).Checker;
        var run = await tracker.Runner.RunDueAsync(Ct);

        Assert.NotNull(waiting);
        Assert.Equal((BalanceCheckKind.Requested, "United MileagePlus"), (run!.Kind, run.Program));
        Assert.Equal([("alex-united", TimeSpan.FromHours(1))], tracker.Checker.Asked);
        var check = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Checks);
        Assert.Equal((1, false, true), (check.SignInsToday, check.Queued, check.CanCheckNow));
    }

    [Fact]
    public async Task Asking_is_refused_once_the_day_has_had_its_two_sign_ins()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 15:00");
        await tracker.Runner.RequestAsync("alex-aa", Ct);
        await tracker.Runner.RunDueAsync(Ct);

        tracker.Clock.Set("2026-11-27 18:00");
        var refused = await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Runner.RequestAsync("alex-aa", Ct));

        Assert.Contains("daily limit", refused.Message);
        Assert.False((await tracker.Queries.GetBalancesAsync(Ct)).Checks.Single().CanCheckNow);
        await Assert.ThrowsAsync<NotFoundException>(() => tracker.Runner.RequestAsync("nobody-aa", Ct));
    }

    [Fact]
    public async Task While_the_automation_does_not_answer_the_tracker_says_since_when_and_asks_for_nothing()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        tracker.Checker.Outage = "udaan's service did not answer.";

        await tracker.Runner.RunDueAsync(Ct);
        tracker.Clock.Set("2026-11-27 12:45");
        await tracker.Runner.RunDueAsync(Ct);

        var checker = (await tracker.Queries.GetBalancesAsync(Ct)).Checker!;
        Assert.Equal((DateTimeOffset.Parse("2026-11-27T12:30:00-05:00"), "udaan's service did not answer."), (checker.UnreachableSince, checker.Problem));
        Assert.Equal(1, (await tracker.Queries.GetSummaryAsync(tracker.Feed.Name, Ct)).BalanceProblems);
        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Runner.RequestAsync("alex-aa", Ct));

        // It comes back, and the day's check runs as if nothing had happened.
        tracker.Checker.Outage = null;
        Assert.NotNull(await tracker.Runner.RunDueAsync(Ct));
        Assert.Null((await tracker.Queries.GetBalancesAsync(Ct)).Checker!.UnreachableSince);
    }

    [Fact]
    public async Task An_answer_that_is_not_a_result_is_shown_and_nothing_is_asked_for_a_quarter_of_an_hour()
    {
        await using var tracker = await StartAsync("2026-11-27 12:30", "alex-aa");
        var ok = tracker.Checker.Answer;
        tracker.Checker.Answer = _ => throw new BalanceCheckerUnavailableException("udaan's service took too long to answer.", reached: true);

        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        tracker.Checker.Answer = ok;
        tracker.Clock.Set("2026-11-27 12:44");
        Assert.Null(await tracker.Runner.RunDueAsync(Ct));

        var checker = (await tracker.Queries.GetBalancesAsync(Ct)).Checker!;
        Assert.Equal((null, "udaan's service took too long to answer."), (checker.UnreachableSince, checker.Problem));

        tracker.Clock.Set("2026-11-27 12:45");
        Assert.NotNull(await tracker.Runner.RunDueAsync(Ct));
        Assert.Null((await tracker.Queries.GetBalancesAsync(Ct)).Checker!.Problem);
    }

    [Fact]
    public async Task With_no_automation_connected_nothing_is_asked_and_asking_is_refused()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 12:30");
        tracker.Checker.Accounts.Add("alex-aa");

        Assert.Null(await tracker.Runner.RunDueAsync(Ct));
        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Runner.RequestAsync("alex-aa", Ct));
        Assert.Null((await tracker.Queries.GetBalancesAsync(Ct)).Checker);
    }

    /// <summary>A tracker with the automation connected, Alex as the owner, and every check working.</summary>
    private static async Task<Tracker> StartAsync(string now, params string[] accounts)
    {
        var tracker = await Tracker.StartAsync(now);
        await tracker.PersonAsync("Alex");
        tracker.Checker.IsConfigured = true;
        tracker.Checker.Accounts.AddRange(accounts);
        tracker.Checker.Answer = account => BalanceCheckAnswer.Result(
            Udaan.Ok(account, account.Split('-')[1], "alex", 1_000, tracker.Clock.Now.ToString("o")));
        return tracker;
    }

    private static BalanceCheckAnswer Failed(Tracker tracker, string account, string status, string code, bool retryable) =>
        BalanceCheckAnswer.Result(
            Udaan.Failed(account, account.Split('-')[1], "alex", status, "it did not work", tracker.Clock.Now.ToString("o"), code: code, retryable: retryable));
}
