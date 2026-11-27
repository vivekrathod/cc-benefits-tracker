using Chitragupta.Application;
using Chitragupta.Application.Balances;
using Chitragupta.Domain;
using Chitragupta.Infrastructure.Balances;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>The results are in the shape udaan prints them. Every account and number is made up.</summary>
public class UdaanBalanceParserTests
{
    private readonly UdaanBalanceParser _parser = new();

    [Fact]
    public void Reads_an_american_result_with_its_expiry_day()
    {
        var report = Assert.Single(_parser.Parse("""
            {
             "account": "alex-aa", "program": "aa", "owner": "alex", "otp": "gmail", "proxy": false,
             "program_name": "American Airlines AAdvantage", "ok": true, "status": "ok", "source": "live",
             "checked_at": "2026-11-27T16:52:38+00:00", "session_reused": false,
             "balance": {
              "award_miles": 45210, "loyalty_points": 1200, "loyalty_points_to_next_tier": 38800,
              "next_tier": "AAdvantage Gold", "miles_expire": "2028-03-14"
             },
             "driver": "patchright+chrome+playjev", "jev_used": true,
             "decisions": [ { "step": "page state", "method": "jev", "choice": "signed_in", "confidence": 1 } ],
             "duration_s": 31.7
            }
            """));

        var at = DateTimeOffset.Parse("2026-11-27T16:52:38Z");
        Assert.Equal(
            new BalanceReport(
                "alex-aa", "alex", "American AAdvantage", BalanceCheckStatus.Ok, null, at,
                new ReportedBalance(45_210, DateOnly.Parse("2028-03-14"), at))
            {
                DurationSeconds = 31.7,
                Trail = """[ { "step": "page state", "method": "jev", "choice": "signed_in", "confidence": 1 } ]""",
            },
            report);
    }

    [Theory]
    [InlineData("alaska", "available_points", "Alaska Atmos Rewards")]
    [InlineData("united", "miles", "United MileagePlus")]
    public void Knows_which_number_is_the_balance_for_each_program(string key, string field, string name)
    {
        var report = Assert.Single(_parser.Parse($$"""
            { "account": "alex-{{key}}", "program": "{{key}}", "owner": "alex", "ok": true, "status": "ok", "source": "cache", "age_s": 120,
              "checked_at": "2026-11-27T16:52:38+00:00", "balance": { "{{field}}": 1900, "status": "Member", "pqp": 7 } }
            """));

        Assert.Equal((name, BalanceCheckStatus.Ok), (report.Program, report.Status));
        Assert.Equal(new ReportedBalance(1_900, null, DateTimeOffset.Parse("2026-11-27T16:52:38Z")), report.Balance);
    }

    [Fact]
    public void Reads_every_result_of_a_check_of_all_accounts()
    {
        var reports = _parser.Parse("""
            { "results": [
              { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": true, "status": "ok",
                "checked_at": "2026-11-27T16:52:38+00:00", "balance": { "award_miles": 10, "miles_expire": null } },
              { "account": "sam-aa", "program": "aa", "owner": "sam", "ok": true, "status": "ok",
                "checked_at": "2026-11-27T17:10:00+00:00", "balance": { "award_miles": 20, "miles_expire": null } } ] }
            """);

        Assert.Equal([("alex-aa", (long?)10), ("sam-aa", 20)], reports.Select(report => (report.Account, report.Balance?.Amount)));
        Assert.All(reports, report => Assert.Null(report.Balance!.ExpiresOn));
    }

    [Theory]
    [InlineData("needs_user", BalanceCheckStatus.NeedsUser)]
    [InlineData("blocked", BalanceCheckStatus.Blocked)]
    [InlineData("failed", BalanceCheckStatus.Failed)]
    [InlineData("something new", BalanceCheckStatus.Failed)]
    public void A_failed_check_keeps_its_reason_and_brings_the_last_reading_that_worked(string status, BalanceCheckStatus expected)
    {
        var report = Assert.Single(_parser.Parse($$"""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": false, "status": "{{status}}",
              "error": "the sign-in form came back; not retrying", "checked_at": "2026-11-27T16:52:38+00:00",
              "last_good": { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": true, "status": "ok",
                "checked_at": "2026-11-20T12:00:00+00:00", "balance": { "award_miles": 45210, "miles_expire": "2028-03-14" } } }
            """));

        Assert.Equal(
            (expected, "the sign-in form came back; not retrying", (DateTimeOffset?)DateTimeOffset.Parse("2026-11-27T16:52:38Z")),
            (report.Status, report.Problem, report.CheckedAt));
        Assert.Equal(
            new ReportedBalance(45_210, DateOnly.Parse("2028-03-14"), DateTimeOffset.Parse("2026-11-20T12:00:00Z")), report.Balance);
    }

    [Fact]
    public void A_check_paused_after_a_block_has_no_time_of_its_own_and_may_have_no_reading()
    {
        var report = Assert.Single(_parser.Parse("""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": false, "status": "blocked",
              "error": "live checks paused after a site block: site challenge", "retry_after": "2026-11-27T22:00:00+00:00", "last_good": null }
            """));

        Assert.Equal((BalanceCheckStatus.Blocked, null, null), (report.Status, report.CheckedAt, report.Balance));
    }

    [Fact]
    public void A_long_reason_is_cut_short()
    {
        var report = Assert.Single(_parser.Parse($$"""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": false, "status": "failed", "error": "{{new string('x', 400)}}" }
            """));

        Assert.Equal(301, report.Problem!.Length);
    }

    [Fact]
    public void A_program_it_does_not_know_is_said_so_instead_of_guessed_at()
    {
        var report = Assert.Single(_parser.Parse("""
            { "account": "alex-delta", "program": "delta", "owner": "alex", "ok": true, "status": "ok",
              "checked_at": "2026-11-27T16:52:38+00:00", "balance": { "miles": 5 } }
            """));

        Assert.Equal((BalanceCheckStatus.UnknownProgram, "delta", null), (report.Status, report.Program, report.Balance));
        Assert.Contains("\"delta\"", report.Problem);
    }

    [Theory]
    [InlineData("""{ "award_miles": null }""")]
    [InlineData("""{ "award_miles": -4 }""")]
    [InlineData("""{ "award_miles": "many" }""")]
    [InlineData("""{ }""")]
    public void Success_without_a_usable_number_is_a_failure(string balance)
    {
        var report = Assert.Single(_parser.Parse($$"""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": true, "status": "ok",
              "checked_at": "2026-11-27T16:52:38+00:00", "balance": {{balance}} }
            """));

        Assert.Equal((BalanceCheckStatus.Failed, null), (report.Status, report.Balance));
        Assert.Contains("award_miles", report.Problem);
    }

    [Theory]
    [InlineData("udaan: command not found", "not JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("""{ "ok": false, "status": "usage", "error": "no account matches \"aa\"" }""", "udaan said: no account matches")]
    [InlineData("""{ "results": [ 5 ] }""", "names no account")]
    public void Anything_else_is_refused_with_the_reason(string text, string reason)
    {
        var refused = Assert.Throws<RequestRejectedException>(() => _parser.Parse(text));

        Assert.Contains(reason, refused.Message);
    }

    [Fact]
    public void A_failure_keeps_its_code_whether_trying_again_can_help_and_what_to_do()
    {
        var report = Assert.Single(_parser.Parse("""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "udaan_version": "0.3.0", "ok": false,
              "status": "needs_user", "error": "the sign-in form came back; not retrying",
              "error_code": "login_rejected", "retryable": false, "source": "live",
              "checked_at": "2026-11-27T16:52:38+00:00", "decisions": [], "duration_s": 48.2, "last_good": null }
            """));

        Assert.Equal(
            (BalanceCheckStatus.NeedsUser, "login_rejected", false, true, false, 48.2, "0.3.0", null),
            (report.Status, report.ErrorCode, report.Retryable, report.SignedIn, report.Deferred, report.DurationSeconds, report.ToolVersion, report.Trail));
        Assert.Contains("udaan set-login alex-aa", report.Advice);
    }

    [Fact]
    public void A_failure_that_clears_by_itself_says_so()
    {
        var report = Assert.Single(_parser.Parse("""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": false, "status": "failed",
              "error": "no code arrived", "error_code": "otp_timeout", "retryable": true,
              "checked_at": "2026-11-27T16:52:38+00:00" }
            """));

        Assert.Equal((BalanceCheckStatus.Failed, true), (report.Status, report.Retryable));
        Assert.Contains("tried again later", report.Advice);
    }

    [Fact]
    public void A_refusal_is_known_as_one_because_no_sign_in_happened()
    {
        var reports = _parser.Parse("""
            { "results": [
              { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": false, "status": "needs_user", "attempted": false,
                "error_code": "login_rejected", "retryable": false, "held": true, "error": "live checks held since a rejected login", "last_good": null },
              { "account": "alex-united", "program": "united", "owner": "alex", "ok": false, "status": "wait", "attempted": false,
                "error": "another live sign-in ended too recently", "retry_after": "2026-11-27T17:10:00+00:00", "last_good": null }
            ] }
            """);

        Assert.Equal([(false, false), (false, true)], reports.Select(report => (report.SignedIn, report.Deferred)));
        Assert.Null(reports[0].CheckedAt);
    }
}
