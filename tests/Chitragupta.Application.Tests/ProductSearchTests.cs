using Chitragupta.Contracts;

namespace Chitragupta.Application.Tests;

public class ProductSearchTests
{
    private static readonly ProductDto[] Products =
    [
        Product("amex-gold", "Amex Gold"),
        Product("amex-platinum", "Amex Platinum"),
        Product("boa-premium-rewards", "Bank of America Premium Rewards"),
        Product("chase-sapphire-preferred", "Chase Sapphire Preferred"),
        // The source's own name, with the issuer's official name and the old one as other names.
        Product("citi-aadvantage-executive", "Citi AAdvantage Executive",
            "Citi / AAdvantage Executive World Legend Mastercard", "Citi AAdvantage Executive World Elite Mastercard"),
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

    [Fact]
    public void Other_names_help_find_a_card_but_do_not_push_it_down_the_list()
    {
        Assert.Equal("citi-aadvantage-executive", ProductSearch.Find(Products, "world legend")[0].Id);

        // Both Citi cards carry two of the three words; the one with the shorter name of its own comes first.
        Assert.Equal(
            ["citi-aadvantage-executive", "citi-aadvantage-platinum-select-world-elite"],
            ProductSearch.Find(Products, "citi world visa").Take(2).Select(product => product.Id));
    }

    private static ProductDto Product(string id, string name, params string[] alsoCalled) =>
        new(id, name, 0, false, null, alsoCalled.Length == 0 ? null : alsoCalled);
}
