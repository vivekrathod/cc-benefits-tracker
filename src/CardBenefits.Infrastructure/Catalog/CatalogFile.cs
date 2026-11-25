using System.Text.Json;
using System.Text.Json.Serialization;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Catalog;

// The shape of catalog/catalog.json. It is this project's own format: the community
// dataset is converted into it once, and it can then be corrected by hand.

internal sealed record CatalogFile(
    string? Source, string? License, IReadOnlyList<string> IgnoreKeywords, IReadOnlyList<CatalogProduct> Products)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

internal sealed record CatalogProduct(
    string Id,
    string Issuer,
    string Name,
    decimal AnnualFee,
    bool IsBusiness,
    string? PointsProgram,
    IReadOnlyList<CatalogBenefit> Benefits);

internal sealed record CatalogBenefit(
    string Id,
    string Name,
    DetectionMode Detection,
    IReadOnlyList<CatalogTerms> Terms,
    IReadOnlyList<CatalogRule>? Rules = null,
    DateOnly? LastDay = null,
    int? GraceDays = null,
    string? Notes = null);

internal sealed record CatalogTerms(DateOnly EffectiveFrom, decimal Amount, Cadence Cadence, YearBasis YearBasis);

internal sealed record CatalogRule(string Keyword, decimal? AmountCeiling = null, bool Verified = false);
