namespace Chitragupta.Contracts;

/// <summary>
/// Finds card products from what a person types. The catalog keeps the names its source
/// uses, and people say "Citi World Executive" or "American Express Gold" where it says
/// "Citi AAdvantage Executive" or "Amex Gold". So words may come in any order, may be
/// part of a word, an issuer answers to its other names, and a product to the other
/// names recorded for it.
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
                Length: Words(candidate.Product.DisplayName).Length))
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
            .Concat((product.AlsoCalled ?? []).SelectMany(Words))
            .ToArray();

    private static string[] Words(string text) =>
        new string(text.Select(letter => char.IsLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
