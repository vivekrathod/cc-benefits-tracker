using Chitragupta.Domain;
using Chitragupta.Infrastructure.Catalog;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>Local corrections sit on top of the source and say so when the source moves under them.</summary>
public class CatalogOverridesTests
{
    private static readonly CatalogBenefit Dining = Benefit("gold:dining", "Dining credit", 10);
    private static readonly CatalogBenefit Hotel = Benefit("gold:hotel", "Hotel credit", 100);

    private static readonly CatalogFile Source = new("the source", "MIT", [],
    [
        new CatalogProduct("gold", "Amex", "Gold", 325, false, null, [Dining, Hotel]),
        new CatalogProduct("plain", "Chase", "Freedom", 0, false, null, []),
    ]);

    [Fact]
    public void A_correction_changes_the_benefit_and_never_the_products_name_or_id()
    {
        var corrected = Benefit("gold:dining", "Dining credit", 12);
        var overrides = new CatalogOverrides(Products:
        [
            new ProductOverride("gold", ["American Express Gold Card"],
            [
                new BenefitOverride(OverrideAction.Replace, "the amount went up", WrittenAgainst: CatalogOverrides.Fingerprint(Dining), Benefit: corrected),
                new BenefitOverride(OverrideAction.Remove, "ended", Id: "gold:hotel", WrittenAgainst: CatalogOverrides.Fingerprint(Hotel)),
                new BenefitOverride(OverrideAction.Add, "missing from the source", Benefit: Benefit("gold:lounge", "Lounge passes", 0)),
            ]),
        ]);

        var (catalog, flags) = overrides.Apply(Source);

        Assert.Empty(flags);
        var gold = catalog.Products[0];
        Assert.Equal(("gold", "Amex", "Gold"), (gold.Id, gold.Issuer, gold.Name));
        Assert.Equal(["American Express Gold Card"], gold.Aliases);
        Assert.Equal([("gold:dining", 12m), ("gold:lounge", 0m)], gold.Benefits.Select(benefit => (benefit.Id, benefit.Terms[0].Amount)));
        Assert.Same(Source.Products[1], catalog.Products[1]);
    }

    [Fact]
    public void A_correction_whose_source_row_has_changed_still_wins_and_is_flagged()
    {
        var overrides = new CatalogOverrides(Products:
        [
            new ProductOverride("gold", Benefits:
            [
                new BenefitOverride(OverrideAction.Replace, "the amount went up", WrittenAgainst: "written-before", Benefit: Benefit("gold:dining", "Dining credit", 12)),
                new BenefitOverride(OverrideAction.Remove, "ended", Id: "gold:hotel", WrittenAgainst: "written-before"),
            ]),
        ]);

        var (catalog, flags) = overrides.Apply(Source);

        Assert.Equal([("gold:dining", 12m)], catalog.Products[0].Benefits.Select(benefit => (benefit.Id, benefit.Terms[0].Amount)));
        Assert.Equal(2, flags.Count);
        Assert.Contains($"the source changed \"Dining credit\" after it was corrected here", flags[0]);
        Assert.Contains(CatalogOverrides.Fingerprint(Dining), flags[0]);
        Assert.Contains("the source changed \"Hotel credit\", which is hidden here", flags[1]);
    }

    [Fact]
    public void A_correction_whose_source_row_has_gone_is_flagged()
    {
        var overrides = new CatalogOverrides(Products:
        [
            new ProductOverride("gold", Benefits:
            [
                new BenefitOverride(OverrideAction.Replace, "wrong cadence", WrittenAgainst: "any", Benefit: Benefit("gold:resy", "Resy credit", 50)),
                new BenefitOverride(OverrideAction.Remove, "ended", Id: "gold:gone", WrittenAgainst: "any"),
                new BenefitOverride(OverrideAction.Add, "missing from the source", Benefit: Benefit("gold:hotel", "Hotel credit", 120)),
            ]),
            new ProductOverride("retired", ["Old name"]),
        ]);

        var (catalog, flags) = overrides.Apply(Source);

        // The corrected benefit stays in use and the hidden one stays hidden; nothing a card relies on is lost.
        Assert.Equal(
            [("gold:dining", 10m), ("gold:hotel", 120m), ("gold:resy", 50m)],
            catalog.Products[0].Benefits.Select(benefit => (benefit.Id, benefit.Terms[0].Amount)));
        Assert.Collection(
            flags,
            flag => Assert.Contains("the source no longer has \"Resy credit\"", flag),
            flag => Assert.Contains("hiding it here is no longer needed", flag),
            flag => Assert.Contains("the source now lists \"Hotel credit\" itself", flag),
            flag => Assert.Contains("\"retired\" is no longer in the source", flag));
    }

    [Fact]
    public void A_product_added_here_gives_way_when_the_source_brings_its_own()
    {
        var added = new CatalogProduct("plain", "", "Other card", 0, false, null, []);
        var extra = new CatalogProduct("other-card", "", "Other card", 0, false, null, []);

        var (catalog, flags) = new CatalogOverrides(AddProducts: [added, extra]).Apply(Source);

        Assert.Equal(["gold", "plain", "other-card"], catalog.Products.Select(product => product.Id));
        Assert.Equal("Freedom", catalog.Products[1].Name);
        Assert.Contains("The source now has its own \"plain\"", Assert.Single(flags));
    }

    [Fact]
    public void A_source_row_keeps_its_mark_until_it_changes()
    {
        Assert.Equal(CatalogOverrides.Fingerprint(Dining), CatalogOverrides.Fingerprint(Benefit("gold:dining", "Dining credit", 10)));
        Assert.NotEqual(CatalogOverrides.Fingerprint(Dining), CatalogOverrides.Fingerprint(Dining with { Notes = "Now at more restaurants." }));
    }

    private static CatalogBenefit Benefit(string id, string name, decimal amount) =>
        new(id, name, DetectionMode.Manual, [new CatalogTerms(new DateOnly(2000, 1, 1), amount, Cadence.Monthly, YearBasis.Calendar)]);
}
