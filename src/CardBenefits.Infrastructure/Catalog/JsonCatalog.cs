using System.Text.Json;
using CardBenefits.Application.Ports;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Catalog;

/// <summary>The catalog read from a JSON file: the one shipped inside the app, or one on disk.</summary>
internal sealed class JsonCatalog : ICatalog
{
    private readonly Dictionary<string, CardProduct> _products = [];
    private readonly Dictionary<string, List<BenefitDefinition>> _benefitsByProduct = [];
    private readonly Dictionary<string, BenefitDefinition> _benefits = [];
    private readonly Dictionary<string, string> _notes = [];
    private readonly Dictionary<string, int> _graceDays = [];
    private readonly List<MatchRule> _rules = [];

    public JsonCatalog(CatalogFile file)
    {
        IgnoreKeywords = file.IgnoreKeywords.ToArray();

        foreach (var product in file.Products)
        {
            _products.Add(product.Id, new CardProduct(
                product.Id, product.Issuer, product.Name, Money.FromDollars(product.AnnualFee), product.IsBusiness, product.PointsProgram));
            _benefitsByProduct.Add(product.Id, []);

            foreach (var benefit in product.Benefits)
            {
                var definition = new BenefitDefinition(
                    benefit.Id,
                    product.Id,
                    benefit.Name,
                    benefit.Detection,
                    benefit.Terms.Select(terms => new BenefitTerms(
                        terms.EffectiveFrom, Money.FromDollars(terms.Amount), terms.Cadence, terms.YearBasis)),
                    benefit.LastDay);

                _benefits.Add(definition.Id, definition);
                _benefitsByProduct[product.Id].Add(definition);

                if (benefit.Notes is not null)
                {
                    _notes.Add(benefit.Id, benefit.Notes);
                }

                if (benefit.GraceDays is not null)
                {
                    _graceDays.Add(benefit.Id, benefit.GraceDays.Value);
                }

                _rules.AddRange((benefit.Rules ?? []).Select(rule => new MatchRule(
                    benefit.Id,
                    rule.Keyword,
                    rule.AmountCeiling is null ? null : Money.FromDollars(rule.AmountCeiling.Value),
                    rule.Verified)));
            }
        }
    }

    public IReadOnlyList<CardProduct> Products => _products.Values.ToArray();

    public IReadOnlyList<MatchRule> Rules => _rules;

    public IReadOnlyList<string> IgnoreKeywords { get; }

    public IReadOnlyDictionary<string, int> GraceDays => _graceDays;

    public CardProduct? FindProduct(string productId) => _products.GetValueOrDefault(productId);

    public IReadOnlyList<BenefitDefinition> BenefitsOf(string productId) =>
        _benefitsByProduct.TryGetValue(productId, out var benefits) ? benefits : [];

    public BenefitDefinition? FindBenefit(string benefitId) => _benefits.GetValueOrDefault(benefitId);

    public string? NotesOf(string benefitId) => _notes.GetValueOrDefault(benefitId);

    /// <summary>The catalog built into the app, or the file at the path when one is given.</summary>
    public static JsonCatalog Load(string? path)
    {
        using var stream = string.IsNullOrWhiteSpace(path)
            ? typeof(JsonCatalog).Assembly.GetManifestResourceStream("catalog.json")
                ?? throw new InvalidOperationException("The built-in catalog is missing from the assembly.")
            : File.OpenRead(path);
        return Read(stream);
    }

    public static JsonCatalog Read(Stream stream) =>
        new(JsonSerializer.Deserialize<CatalogFile>(stream, CatalogFile.Json)
            ?? throw new InvalidOperationException("The catalog file is empty."));
}
