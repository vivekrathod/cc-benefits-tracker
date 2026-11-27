using System.Globalization;

namespace CardBenefits.Application.Tests.Support;

/// <summary>Results in the shape the udaan command prints them. Every number here is made up.</summary>
internal static class Udaan
{
    public static string Ok(
        string account, string program, string owner, long balance, string checkedAt, string? expires = null)
    {
        var (field, extra) = program switch
        {
            "aa" => ("award_miles", $", \"loyalty_points\": 0, \"miles_expire\": {(expires is null ? "null" : $"\"{expires}\"")}"),
            "alaska" => ("available_points", ", \"status\": \"Atmos Member\""),
            "united" => ("miles", ", \"pqp\": 0"),
            _ => ("points", ""),
        };

        return string.Create(CultureInfo.InvariantCulture, $$"""
            {
             "account": "{{account}}", "program": "{{program}}", "owner": "{{owner}}", "otp": "gmail", "proxy": false,
             "program_name": "Some Program", "ok": true, "status": "ok", "source": "live",
             "checked_at": "{{checkedAt}}", "session_reused": true,
             "balance": { "{{field}}": {{balance}}{{extra}} },
             "driver": "patchright+chrome+playjev", "jev_used": false, "decisions": [], "duration_s": 12.3
            }
            """);
    }

    public static string Failed(
        string account, string program, string owner, string status, string error, string checkedAt, string? lastGood = null) =>
        $$"""
        {
         "account": "{{account}}", "program": "{{program}}", "owner": "{{owner}}", "otp": "gmail", "proxy": false,
         "program_name": "Some Program", "ok": false, "status": "{{status}}", "error": "{{error}}",
         "checked_at": "{{checkedAt}}", "decisions": [], "duration_s": 40.1,
         "last_good": {{lastGood ?? "null"}}
        }
        """;

    public static string All(params string[] results) => $$"""{ "results": [ {{string.Join(",", results)}} ] }""";
}
