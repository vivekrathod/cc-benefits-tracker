using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chitragupta.Application;
using Chitragupta.Application.Feeds;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Infrastructure.Feeds.Csv;

/// <summary>
/// Reads the transaction exports of the common US issuers. The format is recognised
/// from the header row. Issuers disagree on the sign of a purchase, so each known
/// header carries its convention and the result is always "credit to the card is positive".
/// </summary>
internal sealed class CsvStatementParser : IStatementParser
{
    private static readonly string[] PostedColumns = ["post date", "posted date", "posting date"];
    private static readonly string[] DateColumns = ["transaction date", "trans. date", "trans date", "date"];
    private static readonly string[] DescriptionColumns = ["description", "payee", "name", "merchant"];
    private static readonly string[] DateFormats = ["M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "yyyy-MM-dd", "yyyy/MM/dd", "MM-dd-yyyy"];

    public IReadOnlyList<FeedTransaction> Parse(string csv)
    {
        var rows = CsvText.Read(csv.TrimStart('﻿'));
        var headerIndex = rows.FindIndex(row => Find(Normalise(row), DateColumns.Concat(PostedColumns)) >= 0
            && Find(Normalise(row), DescriptionColumns) >= 0);
        if (headerIndex < 0)
        {
            throw new RequestRejectedException(
                "This does not look like a card statement export: no header row with a date and a description column.");
        }

        var header = Normalise(rows[headerIndex]);
        var posted = Find(header, PostedColumns);
        var date = Find(header, DateColumns);
        var description = Find(header, DescriptionColumns);
        var amount = Find(header, ["amount"]);
        var debit = Find(header, ["debit"]);
        var credit = Find(header, ["credit"]);
        if (amount < 0 && (debit < 0 || credit < 0))
        {
            throw new RequestRejectedException("The export has neither an Amount column nor Debit and Credit columns.");
        }

        var parsed = new List<(DateOnly PostedOn, DateOnly? TransactedOn, decimal Amount, string Description, string Raw)>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            var when = Date(Cell(row, posted)) ?? Date(Cell(row, date));
            if (when is null)
            {
                continue;
            }

            decimal value;
            if (amount >= 0)
            {
                if (Number(Cell(row, amount)) is not { } single)
                {
                    continue;
                }

                value = single;
            }
            else
            {
                // Some exports write a credit as a negative number in the Credit column.
                value = Math.Abs(Number(Cell(row, credit)) ?? 0m) - Math.Abs(Number(Cell(row, debit)) ?? 0m);
            }

            parsed.Add((when.Value, Date(Cell(row, date)), value, Cell(row, description), string.Join(',', row)));
        }

        if (amount >= 0 && PurchasesArePositive(header, parsed.Select(item => item.Amount)))
        {
            parsed = parsed.Select(item => item with { Amount = -item.Amount }).ToList();
        }

        // An export has no transaction ids, so one is derived from the content. Identical
        // rows on the same day are numbered, which keeps ids stable when files overlap.
        var seen = new Dictionary<string, int>();
        return parsed
            .Select(item =>
            {
                var cents = (long)decimal.Round(item.Amount * 100m, MidpointRounding.AwayFromZero);
                var key = $"{item.PostedOn:yyyyMMdd}:{cents}:{Hash(item.Description)}";
                var occurrence = seen[key] = seen.GetValueOrDefault(key) + 1;
                return new FeedTransaction(
                    $"{key}:{occurrence}", item.PostedOn, item.TransactedOn, new Money(cents), item.Description, false, item.Raw);
            })
            .ToArray();
    }

    /// <summary>
    /// Amex and Discover write a purchase as a positive amount; Chase, Bank of America,
    /// U.S. Bank and Barclays write it as a negative one. An unknown header falls back
    /// to the fact that on a card statement purchases outnumber credits.
    /// </summary>
    private static bool PurchasesArePositive(string[] header, IEnumerable<decimal> amounts)
    {
        if (header.Contains("card member") || header.Contains("extended details") || header.Contains("trans. date")
            || header.SequenceEqual(["date", "description", "amount"]))
        {
            return true;
        }

        if (header.Contains("type") || header.Contains("reference number") || header.Contains("memo")
            || header.Contains("post date") || header.Contains("posted date"))
        {
            return false;
        }

        var values = amounts.ToArray();
        return values.Count(value => value > 0m) > values.Count(value => value < 0m);
    }

    private static string[] Normalise(string[] row) => row.Select(cell => cell.Trim().ToLowerInvariant()).ToArray();

    private static int Find(string[] header, IEnumerable<string> names) =>
        names.Select(name => Array.IndexOf(header, name)).FirstOrDefault(index => index >= 0, -1);

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : "";

    private static DateOnly? Date(string text) =>
        DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? Number(string text)
    {
        var cleaned = text.Replace("$", "").Replace(",", "").Trim();
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            cleaned = "-" + cleaned[1..^1];
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Hash(string description)
    {
        var normalised = string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..12];
    }
}
