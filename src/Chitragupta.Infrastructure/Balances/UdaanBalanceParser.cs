using System.Globalization;
using System.Text.Json;
using Chitragupta.Application;
using Chitragupta.Application.Balances;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Infrastructure.Balances;

/// <summary>
/// Reads the JSON the udaan command prints for "udaan check": one result, or several under
/// "results". Everything the tracker knows about udaan's results is in this file: its program
/// keys, which number in a result is the balance, how it words a failure, and what a person
/// can do about each kind.
/// </summary>
internal sealed class UdaanBalanceParser : IBalanceReportParser
{
    private const int LongestProblem = 300;
    private const int LongestTrail = 16_000;

    // The names are the catalog's, so a balance reads like the program on the card it belongs with.
    private static readonly Dictionary<string, ProgramShape> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aa"] = new("American AAdvantage", "award_miles", "miles_expire"),
        ["alaska"] = new("Alaska Atmos Rewards", "available_points", null),
        ["united"] = new("United MileagePlus", "miles", null),
    };

    public IReadOnlyList<BalanceReport> Parse(string report)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(report);
        }
        catch (JsonException)
        {
            throw NotAResult("it is not JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw NotAResult("it is not a JSON object");
            }

            var results = root.TryGetProperty("results", out var several) && several.ValueKind == JsonValueKind.Array
                ? several.EnumerateArray().ToArray()
                : [root];
            return results.Select(Read).ToArray();
        }
    }

    private static BalanceReport Read(JsonElement result)
    {
        var account = Text(result, "account");
        if (result.ValueKind != JsonValueKind.Object || account is null)
        {
            // udaan answers a mistyped command with a result that names no account.
            var said = result.ValueKind == JsonValueKind.Object ? Text(result, "error") : null;
            throw NotAResult(said is null ? "it names no account" : $"udaan said: {Shorten(said)}");
        }

        var owner = Text(result, "owner") ?? "";
        var key = Text(result, "program") ?? "";
        var checkedAt = Instant(result, "checked_at");
        var status = Text(result, "status") switch
        {
            "ok" => BalanceCheckStatus.Ok,
            "blocked" => BalanceCheckStatus.Blocked,
            "needs_user" => BalanceCheckStatus.NeedsUser,
            _ => BalanceCheckStatus.Failed,
        };

        if (!Programs.TryGetValue(key, out var program))
        {
            return new BalanceReport(
                account, owner, key, BalanceCheckStatus.UnknownProgram,
                $"The tracker does not know which number is the balance for udaan's program \"{key}\".", checkedAt, null);
        }

        var word = Text(result, "status");
        var code = status == BalanceCheckStatus.Ok ? null : Text(result, "error_code");

        // A failed check carries the last result that worked, with its own time.
        var good = status == BalanceCheckStatus.Ok
            ? result
            : result.TryGetProperty("last_good", out var last) && last.ValueKind == JsonValueKind.Object ? last : (JsonElement?)null;
        var reading = good is null ? null : Reading(good.Value, program);

        var problem = status == BalanceCheckStatus.Ok ? null : Shorten(Text(result, "error") ?? "The check failed without saying why.");
        if (status == BalanceCheckStatus.Ok && reading is null)
        {
            (status, problem) = (BalanceCheckStatus.Failed, $"udaan reported success without a \"{program.Balance}\" number.");
        }

        return new BalanceReport(account, owner, program.Name, status, problem, checkedAt, reading)
        {
            ErrorCode = code,
            Retryable = status != BalanceCheckStatus.Ok && Flag(result, "retryable") == true,
            // Results from before udaan said so were all real sign-ins.
            SignedIn = Flag(result, "attempted") != false,
            Deferred = word == "wait",
            Advice = Advice(code, account),
            DurationSeconds = result.TryGetProperty("duration_s", out var took) && took.ValueKind == JsonValueKind.Number
                ? took.GetDouble()
                : null,
            ToolVersion = Text(result, "udaan_version"),
            Trail = Trail(result),
        };
    }

    /// <summary>What a person can do, in udaan's own commands. A kind that clears by itself gets a line that says so.</summary>
    private static string? Advice(string? code, string account) => code switch
    {
        "login_rejected" =>
            $"The site refused the stored login, and udaan will not send it again. Where udaan runs, run \"udaan set-login {account}\", then \"udaan check {account} --fresh\".",
        "login_missing" => $"The stored login is incomplete. Where udaan runs, run \"udaan set-login {account}\".",
        "unknown_page" or "not_signed_in" =>
            $"The site showed something udaan does not know. Where udaan runs, run \"udaan check {account} --fresh --show\" to watch where it stops.",
        "parse_failed" => "It signed in, but the balance was not where it used to be. The site changed, so udaan needs a fix.",
        "otp_rejected" => $"The site would not take the sign-in code. Where udaan runs, run \"udaan check {account} --fresh --show\" to watch it.",
        "otp_source" => "udaan could not read the mailbox the sign-in code arrives in. Where udaan runs, run \"udaan gmail-login\" again.",
        "bot_wall" => "The site turned the automation away. udaan has paused this account and tries again by itself.",
        "otp_timeout" => "The sign-in code did not arrive in time. It is tried again later.",
        "secrets_unavailable" => "udaan could not reach Kaal, where the logins are kept. It is tried again later; check that Kaal is running if it lasts.",
        "jev_unavailable" => "udaan could not reach Jev, which reads the pages. It is tried again later.",
        "page_not_loaded" or "browser_failed" => "The browser or the page did not come up. It is tried again later.",
        _ => null,
    };

    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>udaan's "decisions": each step it took and why. Steps and choices only; it never holds a login or a code.</summary>
    private static string? Trail(JsonElement result)
    {
        if (!result.TryGetProperty("decisions", out var decisions)
            || decisions.ValueKind != JsonValueKind.Array
            || decisions.GetArrayLength() == 0)
        {
            return null;
        }

        var text = decisions.GetRawText();
        return text.Length <= LongestTrail ? text : null;
    }

    private static ReportedBalance? Reading(JsonElement result, ProgramShape program)
    {
        if (Instant(result, "checked_at") is not { } asOf
            || !result.TryGetProperty("balance", out var balance)
            || balance.ValueKind != JsonValueKind.Object
            || !balance.TryGetProperty(program.Balance, out var number)
            || number.ValueKind != JsonValueKind.Number
            || !number.TryGetInt64(out var amount)
            || amount < 0)
        {
            return null;
        }

        var expires = program.Expiry is not null
            && DateOnly.TryParseExact(Text(balance, program.Expiry), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : (DateOnly?)null;
        return new ReportedBalance(amount, expires, asOf);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static DateTimeOffset? Instant(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant
            : null;

    private static string Shorten(string text) => text.Length <= LongestProblem ? text : text[..LongestProblem] + "…";

    private static RequestRejectedException NotAResult(string why) =>
        new($"This does not look like the output of \"udaan check\": {why}.");

    /// <summary>A program as the tracker names it, and where udaan puts its balance and expiry day.</summary>
    private sealed record ProgramShape(string Name, string Balance, string? Expiry);
}
