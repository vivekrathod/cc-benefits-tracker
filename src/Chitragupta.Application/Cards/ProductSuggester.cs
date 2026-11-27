using Chitragupta.Domain;

namespace Chitragupta.Application.Cards;

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
        if (name.Count == 0)
        {
            return null;
        }

        return products
            .Select(product => (
                Product: product,
                Name: Words(product.Name),
                IssuerFits: IssuerFits(product.Issuer, connectionName, accountName)))
            .Where(candidate => candidate.Name.Count > 0 && candidate.Name.All(name.Contains))
            // "Gold", "Platinum" or "Cash" alone turn up on accounts anywhere, so a
            // one-word product name counts only with its issuer next to it.
            .Where(candidate => candidate.Name.Count > 1 || candidate.IssuerFits)
            // The most specific name wins; the institution settles a tie between issuers.
            .OrderByDescending(candidate => candidate.Name.Count)
            .ThenByDescending(candidate => candidate.IssuerFits)
            .ThenBy(candidate => candidate.Product.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Product)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether the connection or the account names the issuer, allowing for how
    /// institutions write themselves: "Citibank" for Citi, "US Bank" for U.S. Bank,
    /// "American Express" for Amex.
    /// </summary>
    private static bool IssuerFits(string issuer, string connectionName, string accountName)
    {
        var words = Words(issuer);
        if (words.Count == 0)
        {
            return false;
        }

        var key = string.Concat(WordList(issuer));
        var context = Words(connectionName).Concat(Words(accountName)).ToHashSet();
        return words.All(context.Contains)
            || context.Any(word => word.StartsWith(key, StringComparison.Ordinal))
            || string.Concat(WordList(connectionName)).Contains(key, StringComparison.Ordinal)
            || (key == "amex" && context.Contains("american") && context.Contains("express"));
    }

    public static bool LooksLikeCard(string accountName) =>
        CardWords.Any(word => accountName.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> Words(string text) => WordList(text).ToHashSet();

    private static string[] WordList(string text) =>
        new string(text.Select(letter => char.IsLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !(word.Length == 4 && word.All(char.IsAsciiDigit)))
            .ToArray();
}
