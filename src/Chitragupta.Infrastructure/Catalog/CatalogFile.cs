using System.Text.Json;
using System.Text.Json.Serialization;
using Chitragupta.Domain;

namespace Chitragupta.Infrastructure.Catalog;

// The shape of catalog/catalog.json. It is this project's own format, and always a plain
// conversion of the community dataset: names and ids are the source's. Corrections are
// never made here. They live in catalog/overrides.json and are laid on top when the catalog loads.

internal sealed record CatalogFile(
    string? Source,
    string? License,
    IReadOnlyList<string> IgnoreKeywords,
    IReadOnlyList<CatalogProduct> Products,
    DateTimeOffset? RetrievedAt = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The conversion shipped inside the app, used until a refresh has fetched a newer one.</summary>
    public static CatalogFile BuiltIn()
    {
        using var stream = typeof(CatalogFile).Assembly.GetManifestResourceStream("catalog.json")
            ?? throw new InvalidOperationException("The built-in catalog is missing from the assembly.");
        return Read(stream);
    }

    public static CatalogFile Read(Stream stream) =>
        JsonSerializer.Deserialize<CatalogFile>(stream, Json) ?? throw new InvalidOperationException("The catalog file is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";
}

internal sealed record CatalogProduct(
    string Id,
    string Issuer,
    string Name,
    decimal AnnualFee,
    bool IsBusiness,
    string? PointsProgram,
    IReadOnlyList<CatalogBenefit> Benefits,
    IReadOnlyList<string>? Aliases = null,
    DateOnly? GoneFromSource = null)
{
    [JsonIgnore]
    public string DisplayName => Issuer.Length == 0 ? Name : $"{Issuer} {Name}";
}

internal sealed record CatalogBenefit(
    string Id,
    string Name,
    DetectionMode Detection,
    IReadOnlyList<CatalogTerms> Terms,
    IReadOnlyList<CatalogRule>? Rules = null,
    DateOnly? LastDay = null,
    int? GraceDays = null,
    string? Notes = null,
    DateOnly? GoneFromSource = null);

internal sealed record CatalogTerms(DateOnly EffectiveFrom, decimal Amount, Cadence Cadence, YearBasis YearBasis);

internal sealed record CatalogRule(string Keyword, decimal? AmountCeiling = null, bool Verified = false);
