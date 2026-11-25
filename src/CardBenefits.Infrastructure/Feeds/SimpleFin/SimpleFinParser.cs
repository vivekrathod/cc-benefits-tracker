using System.Globalization;
using System.Text.Json;
using CardBenefits.Application.Feeds;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Feeds.SimpleFin;

/// <summary>
/// Reads a SimpleFIN /accounts response (protocol v2). A positive amount is money
/// into the account, which is already the convention the tracker uses.
/// </summary>
internal static class SimpleFinParser
{
    public static FeedSnapshot Parse(Stream json, Func<DateTimeOffset, DateOnly> toLocalDate)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var connections = new Dictionary<string, string>();
        foreach (var connection in Array(root, "connections"))
        {
            if (Text(connection, "conn_id") is { } id)
            {
                connections[id] = Text(connection, "name") ?? Text(connection, "org_name") ?? id;
            }
        }

        var accounts = Array(root, "accounts")
            .Select(account => new FeedAccountData(
                ConnectionName(account, connections),
                Text(account, "id") ?? "",
                Text(account, "name") ?? "",
                Array(account, "transactions").Select(transaction => ToTransaction(transaction, toLocalDate)).ToArray()))
            .Where(account => account.AccountId.Length > 0)
            .ToArray();

        var errors = Array(root, "errlist")
            .Select(error => new FeedError(
                Text(error, "code") ?? "error",
                Text(error, "msg") ?? "",
                Text(error, "conn_id") is { } id ? connections.GetValueOrDefault(id, id) : null))
            // Protocol v1 reported errors as plain strings.
            .Concat(Array(root, "errors")
                .Where(error => error.ValueKind == JsonValueKind.String)
                .Select(error => new FeedError("error", error.GetString()!)))
            .ToArray();

        return new FeedSnapshot(accounts, errors);
    }

    private static FeedTransaction ToTransaction(JsonElement transaction, Func<DateTimeOffset, DateOnly> toLocalDate)
    {
        // The Bridge sends 0 for "posted" while a transaction is pending.
        var posted = Epoch(transaction, "posted");
        var transacted = Epoch(transaction, "transacted_at");
        var pending = transaction.TryGetProperty("pending", out var flag) && flag.ValueKind == JsonValueKind.True;

        return new FeedTransaction(
            Text(transaction, "id") ?? "",
            posted is null ? null : toLocalDate(posted.Value),
            transacted is null ? null : toLocalDate(transacted.Value),
            Amount(transaction),
            Text(transaction, "description") ?? Text(transaction, "payee") ?? Text(transaction, "memo") ?? "",
            pending || posted is null,
            transaction.GetRawText());
    }

    private static string ConnectionName(JsonElement account, Dictionary<string, string> connections)
    {
        if (Text(account, "conn_id") is { } id)
        {
            return connections.GetValueOrDefault(id, id);
        }

        // Protocol v1 put the institution on the account itself.
        return account.TryGetProperty("org", out var org) && org.ValueKind == JsonValueKind.Object
            ? Text(org, "name") ?? Text(org, "domain") ?? ""
            : "";
    }

    private static Money Amount(JsonElement transaction)
    {
        var raw = transaction.TryGetProperty("amount", out var amount)
            ? amount.ValueKind == JsonValueKind.Number ? amount.GetRawText() : amount.GetString()
            : null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)
            ? new Money((long)decimal.Round(dollars * 100m, MidpointRounding.AwayFromZero))
            : Money.Zero;
    }

    private static DateTimeOffset? Epoch(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
}
