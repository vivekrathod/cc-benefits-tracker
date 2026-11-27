using Chitragupta.Domain;
using Chitragupta.Infrastructure.Catalog;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>A refresh may bring anything; the household's cards and recorded use survive it.</summary>
public class CatalogPreserverTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly string[] Nothing = [];

    private static readonly CatalogFile Before = File(
        Product("gold", "Gold", Benefit("gold:dining"), Benefit("gold:hotel")),
        Product("cash", "Cash Back"));

    [Fact]
    public void What_the_source_still_has_is_taken_as_the_source_has_it()
    {
        var fresh = File(Product("gold", "Gold Card", Benefit("gold:dining")), Product("cash", "Cash Back"), Product("new", "New"));

        var kept = CatalogPreserver.Keep(Before, fresh, ["gold", "cash"], Nothing, Today);

        Assert.Equal(["gold", "cash", "new"], kept.Products.Select(product => product.Id));
        Assert.Equal("Gold Card", kept.Products[0].Name);
        Assert.Equal(["gold:dining"], kept.Products[0].Benefits.Select(benefit => benefit.Id));
        Assert.All(kept.Products, product => Assert.Null(product.GoneFromSource));
    }

    [Fact]
    public void A_product_the_source_dropped_stays_while_a_card_has_it()
    {
        var fresh = File(Product("new", "New"));

        var kept = CatalogPreserver.Keep(Before, fresh, ["gold"], Nothing, Today);

        Assert.Equal(["new", "gold"], kept.Products.Select(product => product.Id));
        var gold = kept.Products[1];
        Assert.Equal(Today, gold.GoneFromSource);
        Assert.Equal(["gold:dining", "gold:hotel"], gold.Benefits.Select(benefit => benefit.Id));
        Assert.All(gold.Benefits, benefit => Assert.Null(benefit.LastDay));

        // It keeps the day it first went missing, and returns to normal if the source brings it back.
        var later = CatalogPreserver.Keep(kept, fresh, ["gold"], Nothing, Today.AddDays(7));
        Assert.Equal(Today, later.Products[1].GoneFromSource);
        var back = CatalogPreserver.Keep(later, File(Product("gold", "Gold")), ["gold"], Nothing, Today.AddDays(14));
        Assert.Null(Assert.Single(back.Products).GoneFromSource);

        // Once no card has it, it goes.
        Assert.Equal(["new"], CatalogPreserver.Keep(kept, fresh, Nothing, Nothing, Today).Products.Select(product => product.Id));
    }

    [Fact]
    public void A_benefit_the_source_dropped_stays_as_ended_while_use_is_recorded_against_it()
    {
        var fresh = File(Product("gold", "Gold"), Product("cash", "Cash Back"));

        var kept = CatalogPreserver.Keep(Before, fresh, ["gold"], ["gold:dining"], Today);

        var dining = Assert.Single(kept.Products[0].Benefits);
        Assert.Equal(("gold:dining", Today, Today), (dining.Id, dining.GoneFromSource, dining.LastDay));

        // The next refresh leaves it as it is, with the day it ended.
        var later = CatalogPreserver.Keep(kept, fresh, ["gold"], ["gold:dining"], Today.AddDays(7));
        Assert.Equal(dining, Assert.Single(later.Products[0].Benefits));
    }

    [Fact]
    public void A_benefit_that_had_already_ended_keeps_its_own_last_day()
    {
        var ended = Benefit("gold:dining") with { LastDay = Today.AddDays(-30) };
        var before = File(Product("gold", "Gold", ended));

        var kept = CatalogPreserver.Keep(before, File(Product("gold", "Gold")), ["gold"], ["gold:dining"], Today);

        Assert.Equal(Today.AddDays(-30), Assert.Single(kept.Products[0].Benefits).LastDay);
    }

    private static CatalogFile File(params CatalogProduct[] products) => new("the source", "MIT", [], products);

    private static CatalogProduct Product(string id, string name, params CatalogBenefit[] benefits) =>
        new(id, "Amex", name, 0, false, null, benefits);

    private static CatalogBenefit Benefit(string id) =>
        new(id, id, DetectionMode.Manual, [new CatalogTerms(new DateOnly(2000, 1, 1), 10, Cadence.Monthly, YearBasis.Calendar)]);
}
