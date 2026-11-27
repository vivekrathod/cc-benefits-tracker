using System.Globalization;
using System.Text.Json;
using CardBenefits.Application;
using CardBenefits.Application.Balances;
using CardBenefits.Application.Ports;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Balances;

/// <summary>
/// Reads the JSON the udaan command prints for "udaan check": one result, or several under
/// "results". Everything the tracker knows about udaan is in this file: its program keys,
/// which number in a result is the balance, and how it words a failure.
/// </summary>
internal sealed class UdaanBalanceParser : IBalanceReportParser
{
    private const int LongestProblem = 300;

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

        return new BalanceReport(account, owner, program.Name, status, problem, checkedAt, reading);
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
