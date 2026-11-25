using System.Text.Json;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Catalog;

/// <summary>
/// Converts the community dataset of US card benefits (MIT) into this project's
/// catalog. What it cannot express safely is left out instead of guessed at.
/// </summary>
public static class DatasetImporter
{
    public const string SourceUrl = "https://github.com/tiepnv2508/us-credit-card-benefits";

    // Benefits have had their terms for as long as anyone tracks; a real change gets a dated entry by hand.
    private static readonly DateOnly Always = new(2000, 1, 1);

    // Positive amounts on a card that are never a benefit: the household paying its bill.
    private static readonly string[] Payments =
    [
        "PAYMENT THANK YOU", "PAYMENT - THANK YOU", "AUTOMATIC PAYMENT", "AUTOPAY", "ONLINE PAYMENT", "MOBILE PAYMENT",
        "PAYMENT RECEIVED", "ELECTRONIC PAYMENT", "INTERNET PAYMENT", "MOBILE PYMT", "ONLINE PYMT", "PAYMENT FROM",
    ];

    private static readonly Dictionary<string, string> Issuers = new()
    {
        ["AMERICAN_EXPRESS"] = "Amex",
        ["BANK_OF_AMERICA"] = "Bank of America",
        ["CAPITAL_ONE"] = "Capital One",
        ["US_BANK"] = "U.S. Bank",
        ["WELLS_FARGO"] = "Wells Fargo",
        ["PENFED"] = "PenFed",
        ["FNBO"] = "FNBO",
        ["PNC"] = "PNC",
        ["WEB_BANK"] = "WebBank",
        ["FIRST"] = "",
        ["OTHER"] = "",
    };

    /// <summary>Returns the catalog as JSON text, ready to save as catalog/catalog.json.</summary>
    public static string Convert(Stream dataset)
    {
        using var document = JsonDocument.Parse(dataset);
        var root = document.RootElement;
        var programs = root.TryGetProperty("programs", out var list) ? list : default;

        var products = root.GetProperty("cards").EnumerateArray()
            .Select(card => ToProduct(card, programs))
            .OrderBy(product => product.Id, StringComparer.Ordinal)
            .ToArray();

        var file = new CatalogFile(SourceUrl, "MIT, see catalog/NOTICE.md", Payments, products);
        return JsonSerializer.Serialize(file, CatalogFile.Json) + "\n";
    }

    private static CatalogProduct ToProduct(JsonElement card, JsonElement programs)
    {
        var id = card.GetProperty("id").GetString()!;
        var issuerCode = card.GetProperty("issuer").GetString()!;
        var currency = Text(card, "currency");

        string? program = null;
        if (currency is not null and not "USD")
        {
            program = programs.ValueKind == JsonValueKind.Object
                && programs.TryGetProperty(currency, out var entry)
                && Text(entry, "name") is { } name
                    ? name
                    : currency;
        }

        return new CatalogProduct(
            id,
            Issuers.TryGetValue(issuerCode, out var issuer) ? issuer : TitleCase(issuerCode),
            card.GetProperty("name").GetString()!,
            card.TryGetProperty("annualFee", out var fee) && fee.ValueKind == JsonValueKind.Number ? fee.GetDecimal() : 0m,
            Flag(card, "isBusiness"),
            program,
            card.GetProperty("benefits").EnumerateArray()
                .Select(benefit => ToBenefit(id, benefit))
                .OfType<CatalogBenefit>()
                .ToArray());
    }

    private static CatalogBenefit? ToBenefit(string productId, JsonElement benefit)
    {
        var cadence = Text(benefit, "frequency") switch
        {
            "monthly" => Cadence.Monthly,
            "quarterly" => Cadence.Quarterly,
            "semiannual" => Cadence.Semiannual,
            "annual" => Cadence.Annual,
            _ => (Cadence?)null,
        };

        // Left out: cadences this tracker does not model (a credit every four years), benefits
        // that only unlock after a spend threshold, and perks that apply themselves, where
        // there is nothing to use or to forget.
        var isPerk = Text(benefit, "type") == "perk";
        if (cadence is null
            || (benefit.TryGetProperty("unlockSpend", out var unlock) && unlock.ValueKind == JsonValueKind.Object)
            || (isPerk && Flag(benefit, "autoApplied")))
        {
            return null;
        }

        var amount = benefit.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number
            ? decimal.Round(value.GetDecimal(), 2)
            : 0m;
        var keywords = benefit.TryGetProperty("matchKeywords", out var words) && words.ValueKind == JsonValueKind.Array
            ? words.EnumerateArray().Select(word => word.GetString()).OfType<string>().Where(word => word.Length > 0).ToArray()
            : [];

        // Only a dollar credit that posts to the statement can be detected. Credit loaded
        // into a third-party app, and perks, are marked used by hand.
        var automatic = !isPerk && !Flag(benefit, "inApp") && keywords.Length > 0 && amount > 0m;
        var verified = Flag(benefit, "matchVerified");

        return new CatalogBenefit(
            $"{productId}:{benefit.GetProperty("id").GetString()}",
            benefit.GetProperty("name").GetString()!,
            automatic ? DetectionMode.Automatic : DetectionMode.Manual,
            [new CatalogTerms(
                Always,
                amount,
                cadence.Value,
                Text(benefit, "alignment") == "anniversary" ? YearBasis.Cardmember : YearBasis.Calendar)],
            // The keywords are single words, so each rule is capped at the benefit amount, and a
            // rule nobody has checked against a real statement only ever suggests a match.
            automatic ? keywords.Select(keyword => new CatalogRule(keyword, amount, verified)).ToArray() : null,
            Text(benefit, "endDate") is { } endDate ? DateOnly.Parse(endDate) : null,
            null,
            Text(benefit, "notes"));
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string TitleCase(string code) =>
        string.Join(' ', code.Split('_').Select(word => word.Length == 0 ? word : word[..1] + word[1..].ToLowerInvariant()));
}
