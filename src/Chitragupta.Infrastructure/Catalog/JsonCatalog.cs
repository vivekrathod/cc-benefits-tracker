using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Infrastructure.Catalog;

/// <summary>The catalog built from a catalog file, once its corrections have been laid on top.</summary>
internal sealed class JsonCatalog : ICatalog
{
    private readonly Dictionary<string, CardProduct> _products = [];
    private readonly Dictionary<string, List<BenefitDefinition>> _benefitsByProduct = [];
    private readonly Dictionary<string, BenefitDefinition> _benefits = [];
    private readonly Dictionary<string, string> _notes = [];
    private readonly Dictionary<string, int> _graceDays = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _aliases = [];
    private readonly List<MatchRule> _rules = [];

    public JsonCatalog(CatalogFile file)
    {
        IgnoreKeywords = file.IgnoreKeywords.ToArray();

        foreach (var product in file.Products)
        {
            _products.Add(product.Id, new CardProduct(
                product.Id, product.Issuer, product.Name, Money.FromDollars(product.AnnualFee), product.IsBusiness, product.PointsProgram));
            _benefitsByProduct.Add(product.Id, []);
            if (product.Aliases is { Count: > 0 })
            {
                _aliases.Add(product.Id, product.Aliases);
            }

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

    public IReadOnlyList<string> AliasesOf(string productId) => _aliases.GetValueOrDefault(productId) ?? [];

    /// <summary>The catalog shipped inside the app, with this project's corrections applied.</summary>
    public static JsonCatalog BuiltIn() => new(CatalogOverrides.BuiltIn().Apply(CatalogFile.BuiltIn()).Catalog);

    /// <summary>A catalog file on disk, taken exactly as it is.</summary>
    public static JsonCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static JsonCatalog Read(Stream stream) => new(CatalogFile.Read(stream));
}
