using System.Text;
using System.Text.Json;
using CardBenefits.Application.Cards;
using CardBenefits.Domain;
using CardBenefits.Infrastructure.Catalog;

namespace CardBenefits.Infrastructure.Tests;

public class CatalogTests
{
    private const string Dataset = """
        {
          "schemaVersion": 2,
          "programs": { "POINTS": { "name": "Example Points" } },
          "cards": [
            {
              "id": "example-gold", "name": "Gold", "issuer": "AMERICAN_EXPRESS", "isBusiness": false, "currency": "POINTS", "annualFee": 325,
              "benefits": [
                { "id": "dining", "name": "Dining credit", "type": "credit", "value": 10, "frequency": "monthly", "alignment": "calendar",
                  "inApp": false, "matchKeywords": ["dining"], "matchVerified": true, "notes": "At a few restaurants." },
                { "id": "hotel", "name": "Hotel credit", "type": "credit", "value": 100, "frequency": "annual", "alignment": "anniversary",
                  "matchKeywords": ["hotel", "stays"], "matchVerified": false, "endDate": "2027-12-31" },
                { "id": "rides", "name": "Ride cash", "type": "credit", "value": 10, "frequency": "monthly", "alignment": "calendar", "inApp": true, "matchKeywords": [] },
                { "id": "lounge", "name": "Lounge passes", "type": "perk", "value": null, "frequency": "annual", "alignment": "anniversary" },
                { "id": "status", "name": "Elite status", "type": "perk", "value": null, "frequency": "annual", "alignment": "anniversary", "autoApplied": true },
                { "id": "entry", "name": "Global Entry", "type": "credit", "value": 120, "frequency": "custom", "intervalMonths": 48, "alignment": "anniversary", "matchKeywords": ["global"] },
                { "id": "bonus", "name": "Spend bonus", "type": "credit", "value": 100, "frequency": "annual", "alignment": "calendar", "matchKeywords": ["bonus"],
                  "unlockSpend": { "amount": 10000, "window": "calendar" } }
              ]
            },
            { "id": "example-cash", "name": "Cash Back", "issuer": "SOME_BANK", "isBusiness": true, "currency": "USD", "annualFee": 0, "benefits": [] }
          ]
        }
        """;

    [Fact]
    public void The_dataset_converts_into_products_benefits_and_capped_rules()
    {
        var catalog = Convert();

        var gold = catalog.FindProduct("example-gold")!;
        Assert.Equal(("Amex Gold", Money.FromDollars(325), "Example Points"), (gold.DisplayName, gold.AnnualFee, gold.PointsProgram));
        var cash = catalog.FindProduct("example-cash")!;
        Assert.Equal(("Some Bank Cash Back", true, null), (cash.DisplayName, cash.IsBusiness, cash.PointsProgram));

        var dining = catalog.FindBenefit("example-gold:dining")!;
        Assert.Equal(DetectionMode.Automatic, dining.Detection);
        Assert.Equal(new BenefitTerms(new DateOnly(2000, 1, 1), Money.FromDollars(10), Cadence.Monthly, YearBasis.Calendar), dining.Terms.Single());
        Assert.Equal("At a few restaurants.", catalog.NotesOf(dining.Id));
        Assert.Contains(new MatchRule(dining.Id, "dining", Money.FromDollars(10), Verified: true), catalog.Rules);
    }

    [Fact]
    public void Keywords_nobody_verified_become_rules_that_only_suggest()
    {
        var catalog = Convert();

        var hotel = catalog.FindBenefit("example-gold:hotel")!;
        Assert.Equal(YearBasis.Cardmember, hotel.Terms.Single().YearBasis);
        Assert.Equal(DateOnly.Parse("2027-12-31"), hotel.LastDay);
        Assert.Equal(
            [new MatchRule(hotel.Id, "hotel", Money.FromDollars(100), Verified: false), new MatchRule(hotel.Id, "stays", Money.FromDollars(100), Verified: false)],
            catalog.Rules.Where(rule => rule.BenefitId == hotel.Id));
    }

    [Fact]
    public void What_cannot_be_seen_on_a_statement_is_manual_and_what_cannot_be_modelled_is_left_out()
    {
        var catalog = Convert();

        Assert.Equal(DetectionMode.Manual, catalog.FindBenefit("example-gold:rides")!.Detection);
        var lounge = catalog.FindBenefit("example-gold:lounge")!;
        Assert.Equal((DetectionMode.Manual, Money.Zero), (lounge.Detection, lounge.Terms.Single().Amount));

        Assert.Null(catalog.FindBenefit("example-gold:status"));
        Assert.Null(catalog.FindBenefit("example-gold:entry"));
        Assert.Null(catalog.FindBenefit("example-gold:bonus"));
        Assert.Equal(4, catalog.BenefitsOf("example-gold").Count);
        Assert.Contains("PAYMENT THANK YOU", catalog.IgnoreKeywords);
    }

    [Fact]
    public void The_built_in_catalog_loads_and_every_entry_satisfies_the_domain()
    {
        // Building the catalog runs every product and benefit through the domain's own checks.
        var catalog = JsonCatalog.Load(null);

        Assert.True(catalog.Products.Count > 200);
        Assert.NotNull(catalog.FindProduct("amex-gold"));
        Assert.NotEmpty(catalog.BenefitsOf("amex-gold"));
        Assert.All(catalog.Rules, rule =>
        {
            var benefit = catalog.FindBenefit(rule.BenefitId);
            Assert.NotNull(benefit);
            Assert.Equal(DetectionMode.Automatic, benefit.Detection);
            Assert.False(string.IsNullOrWhiteSpace(rule.Keyword));
            Assert.NotNull(rule.AmountCeiling);
        });
        Assert.All(catalog.Products.SelectMany(product => catalog.BenefitsOf(product.Id)), benefit =>
            Assert.Equal(benefit.Detection == DetectionMode.Automatic, catalog.Rules.Any(rule => rule.BenefitId == benefit.Id)));
    }

    [Theory]
    // Account names in the shape institutions report them, with made-up digits.
    [InlineData("American Express", "American Express Gold Card (1234)", "amex-gold")]
    [InlineData("American Express", "Amex EveryDay® Card (1234)", "amex-everyday")]
    [InlineData("American Express", "Blue Cash Everyday® (1234)", "amex-blue-cash-everyday")]
    [InlineData("Chase Bank", "Chase Sapphire Preferred (1234)", "chase-sapphire-preferred")]
    [InlineData("Citibank", "AT&T Points Plus® Card from Citi-1234 (1234)", "citi-at-t-points-plus")]
    [InlineData("Coinbase", "Cash (USD)", null)]
    [InlineData("Chase Bank", "TOTAL CHECKING (1234)", null)]
    public void The_built_in_catalog_recognises_accounts_the_way_institutions_name_them(
        string connection, string account, string? expected) =>
        Assert.Equal(expected, ProductSuggester.Suggest(connection, account, JsonCatalog.Load(null).Products)?.Id);

    [Fact]
    public void Entries_corrected_against_the_issuers_terms_stay_corrected()
    {
        var catalog = JsonCatalog.Load(null);
        // Two Alaska Lounge passes arrive each quarter and lapse with it; they are not a yearly bundle.
        var lounge = catalog.FindBenefit("boa-atmos-rewards-summit:alaska-lounge-passes");
        Assert.NotNull(lounge);
        Assert.Equal(Cadence.Quarterly, Assert.Single(lounge.Terms).Cadence);

        // Not a statement credit, so it has to be listed to be remembered at all.
        var visits = catalog.FindBenefit("usbank-altitude-connect:priority-pass-visits");
        Assert.NotNull(visits);
        Assert.Equal(DetectionMode.Manual, visits.Detection);

        // No longer on the issuer's benefit pages.
        Assert.Null(catalog.FindBenefit("amex-blue-cash-everyday:home-chef-credit"));

        // An announced benefit waits for its first day instead of showing up early.
        var inflight = catalog.FindBenefit("citi-aadvantage-platinum-select-world-elite:inflight-credit");
        Assert.NotNull(inflight);
        Assert.Equal(new DateOnly(2027, 1, 24), inflight.Lifetime.Start);
    }

    [Fact]
    public void A_catalog_file_on_disk_replaces_the_built_in_one()
    {
        using var folder = new TempFolder();
        var file = folder.Write("catalog.json", DatasetImporter.Convert(new MemoryStream(Encoding.UTF8.GetBytes(Dataset))));

        var catalog = JsonCatalog.Load(file);

        Assert.Equal(["example-cash", "example-gold"], catalog.Products.Select(product => product.Id).Order());
    }

    private static JsonCatalog Convert()
    {
        var json = DatasetImporter.Convert(new MemoryStream(Encoding.UTF8.GetBytes(Dataset)));
        Assert.Equal("MIT, see catalog/NOTICE.md", JsonDocument.Parse(json).RootElement.GetProperty("license").GetString());
        return JsonCatalog.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }
}
