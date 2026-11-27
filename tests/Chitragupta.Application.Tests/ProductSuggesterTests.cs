using Chitragupta.Application.Cards;
using Chitragupta.Domain;

namespace Chitragupta.Application.Tests;

public class ProductSuggesterTests
{
    private static readonly CardProduct[] Products =
    [
        new("amex-gold", "Amex", "Gold", Money.FromDollars(325)),
        new("amex-platinum", "Amex", "Platinum", Money.FromDollars(895)),
        new("capitalone-platinum", "Capital One", "Platinum", Money.Zero),
        new("usbank-cash", "U.S. Bank", "Cash+", Money.Zero),
        new("citi-strata", "Citi", "Strata", Money.Zero),
        new("chase-sapphire-preferred", "Chase", "Sapphire Preferred", Money.FromDollars(95)),
        new("discover-it", "Discover", "Discover It", Money.Zero),
    ];

    [Theory]
    [InlineData("American Express", "American Express Gold Card (1234)", "amex-gold")]
    [InlineData("Amex", "Gold Card (1234)", "amex-gold")]
    [InlineData("Capital One", "Platinum Mastercard (1234)", "capitalone-platinum")]
    [InlineData("American Express", "Platinum Card® (1234)", "amex-platinum")]
    [InlineData("US Bank", "Cash+ Visa Signature (1234)", "usbank-cash")]
    [InlineData("Citibank", "Citi Strata℠ Card-1234 (1234)", "citi-strata")]
    [InlineData("Chase Bank", "Chase Sapphire Preferred (1234)", "chase-sapphire-preferred")]
    // A card can sit behind another institution's connection when its name says what it is.
    [InlineData("Capital One", "Discover it Card (1234)", "discover-it")]
    public void The_product_is_read_from_the_account_and_its_institution(string connection, string account, string expected) =>
        Assert.Equal(expected, ProductSuggester.Suggest(connection, account, Products)?.Id);

    [Theory]
    // A one-word product name is too common a word to trust without its issuer.
    [InlineData("Coinbase", "Cash (USD)")]
    [InlineData("Charles Schwab US", "Platinum Checking (1234)")]
    [InlineData("Fidelity Investments", "Gold IRA (1234)")]
    [InlineData("Chase Bank", "TOTAL CHECKING (1234)")]
    public void An_account_elsewhere_that_only_shares_a_common_word_is_not_a_card(string connection, string account) =>
        Assert.Null(ProductSuggester.Suggest(connection, account, Products));
}
