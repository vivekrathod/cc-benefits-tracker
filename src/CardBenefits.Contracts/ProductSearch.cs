namespace CardBenefits.Contracts;

/// <summary>
/// Finds card products from what a person types. People say "Citi World Executive" or
/// "American Express Gold" where the catalog says "Citi AAdvantage Executive World Elite"
/// or "Amex Gold", so words may come in any order, may be part of a word, and an issuer
/// answers to its other names.
/// </summary>
public static class ProductSearch
{
    private static readonly (string Issuer, string[] AlsoCalled)[] Issuers =
    [
        ("Amex ", ["american", "express"]),
        ("Bank of America ", ["boa", "bofa"]),
        ("Capital One ", ["capitalone"]),
        ("Citi ", ["citibank"]),
        ("U.S. Bank ", ["us", "usbank"]),
    ];

    /// <summary>
    /// The products that carry every typed word, in the order given. When none does,
    /// the ones that carry the most words, best first.
    /// </summary>
    public static IReadOnlyList<ProductDto> Find(IReadOnlyList<ProductDto> products, string? typed)
    {
        var wanted = Words(typed ?? "");
        if (wanted.Length == 0)
        {
            return products;
        }

        var scored = products
            .Select(product => (Product: product, Words: Searchable(product)))
            .Select(candidate => (
                candidate.Product,
                Hits: wanted.Count(word => candidate.Words.Any(known => known.Contains(word, StringComparison.Ordinal))),
                Length: candidate.Words.Length))
            .Where(candidate => candidate.Hits > 0)
            .ToArray();

        // Among near misses the shorter name is the closer one: "gold card" means Gold, not Business Gold.
        var complete = scored.Where(candidate => candidate.Hits == wanted.Length).ToArray();
        var ranked = complete.Length > 0
            ? complete
            : scored.OrderByDescending(candidate => candidate.Hits).ThenBy(candidate => candidate.Length).ToArray();
        return ranked.Select(candidate => candidate.Product).ToArray();
    }

    private static string[] Searchable(ProductDto product) =>
        Words(product.DisplayName)
            .Concat(Issuers
                .Where(issuer => product.DisplayName.StartsWith(issuer.Issuer, StringComparison.OrdinalIgnoreCase))
                .SelectMany(issuer => issuer.AlsoCalled))
            .ToArray();

    private static string[] Words(string text) =>
        new string(text.Select(letter => char.IsLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
