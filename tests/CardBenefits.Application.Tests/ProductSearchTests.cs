using CardBenefits.Contracts;

namespace CardBenefits.Application.Tests;

public class ProductSearchTests
{
    private static readonly ProductDto[] Products =
    [
        Product("amex-gold", "Amex Gold"),
        Product("amex-platinum", "Amex Platinum"),
        Product("boa-premium-rewards", "Bank of America Premium Rewards"),
        Product("chase-sapphire-preferred", "Chase Sapphire Preferred"),
        Product("citi-aadvantage-executive", "Citi AAdvantage Executive World Elite"),
        Product("citi-aadvantage-platinum-select-world-elite", "Citi AAdvantage Platinum Select World Elite"),
        Product("citi-strata", "Citi Strata"),
        Product("usbank-altitude-connect", "U.S. Bank Altitude Connect"),
    ];

    [Theory]
    // The way people say a card's name is rarely the way a catalog spells it.
    [InlineData("Citi World Executive", "citi-aadvantage-executive")]
    [InlineData("executive", "citi-aadvantage-executive")]
    [InlineData("citi advantage executive", "citi-aadvantage-executive")]
    [InlineData("Citi Executive card", "citi-aadvantage-executive")]
    [InlineData("citibank aadvantage executive mastercard", "citi-aadvantage-executive")]
    [InlineData("american express gold", "amex-gold")]
    [InlineData("AMEX  gold", "amex-gold")]
    [InlineData("us bank altitude", "usbank-altitude-connect")]
    [InlineData("bofa premium", "boa-premium-rewards")]
    [InlineData("sapph pref", "chase-sapphire-preferred")]
    public void A_card_is_found_the_way_people_name_it(string typed, string expected) =>
        Assert.Equal(expected, ProductSearch.Find(Products, typed)[0].Id);

    [Fact]
    public void Every_word_narrows_the_list_and_nothing_typed_shows_everything()
    {
        Assert.Equal(Products.Length, ProductSearch.Find(Products, "  ").Count);
        Assert.Equal(
            ["citi-aadvantage-executive", "citi-aadvantage-platinum-select-world-elite", "citi-strata"],
            ProductSearch.Find(Products, "citi").Select(product => product.Id));
        Assert.Equal(
            ["citi-aadvantage-platinum-select-world-elite"],
            ProductSearch.Find(Products, "citi platinum").Select(product => product.Id));
    }

    [Fact]
    public void When_no_card_has_every_word_the_closest_ones_come_first()
    {
        var found = ProductSearch.Find(Products, "citi executive visa infinite");

        Assert.Equal("citi-aadvantage-executive", found[0].Id);
        Assert.DoesNotContain(found, product => product.Id == "amex-gold");
    }

    [Fact]
    public void Words_found_nowhere_find_nothing()
    {
        Assert.Empty(ProductSearch.Find(Products, "zzzz"));
    }

    private static ProductDto Product(string id, string name) => new(id, name, 0, false, null);
}
