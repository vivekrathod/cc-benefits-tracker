using CardBenefits.Domain;

namespace CardBenefits.Application.Cards;

/// <summary>
/// Guesses the card product from what the feed calls the account, for example
/// "Chase Sapphire Preferred (1234)". A guess is only ever a suggestion to confirm.
/// </summary>
public static class ProductSuggester
{
    private static readonly string[] CardWords =
        ["card", "visa", "mastercard", "amex", "credit", "rewards", "cash back", "cashback", "signature", "platinum"];

    public static CardProduct? Suggest(string connectionName, string accountName, IEnumerable<CardProduct> products)
    {
        var name = Words(accountName);
        var context = Words(connectionName).Concat(name).ToHashSet();
        if (name.Count == 0)
        {
            return null;
        }

        return products
            .Select(product => (Product: product, Name: Words(product.Name), Issuer: Words(product.Issuer)))
            .Where(candidate => candidate.Name.Count > 0 && candidate.Name.All(name.Contains))
            // The most specific name wins; the institution settles a tie between issuers.
            .OrderByDescending(candidate => candidate.Name.Count)
            .ThenByDescending(candidate => candidate.Issuer.Count(context.Contains))
            .ThenBy(candidate => candidate.Product.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Product)
            .FirstOrDefault();
    }

    public static bool LooksLikeCard(string accountName) =>
        CardWords.Any(word => accountName.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> Words(string text) =>
        new string(text.Select(letter => char.IsLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !(word.Length == 4 && word.All(char.IsAsciiDigit)))
            .ToHashSet();
}
