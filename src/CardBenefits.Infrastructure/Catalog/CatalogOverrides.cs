using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardBenefits.Infrastructure.Catalog;

/// <summary>
/// This project's own corrections to the community catalog, kept apart from it in
/// catalog/overrides.json and keyed by the source's ids. The catalog itself can then be
/// replaced by a newer copy of the source at any time without losing a correction, and
/// a correction whose source row has since changed or gone is flagged instead of
/// silently winning or silently vanishing.
/// </summary>
internal sealed record CatalogOverrides(
    IReadOnlyList<CatalogProduct>? AddProducts = null, IReadOnlyList<ProductOverride>? Products = null)
{
    public static readonly CatalogOverrides None = new();

    private static readonly JsonSerializerOptions Compact = new(CatalogFile.Json) { WriteIndented = false };

    public static CatalogOverrides BuiltIn()
    {
        using var stream = typeof(CatalogOverrides).Assembly.GetManifestResourceStream("catalog-overrides.json")
            ?? throw new InvalidOperationException("The built-in catalog overrides are missing from the assembly.");
        return Read(stream);
    }

    public static CatalogOverrides Read(Stream stream) =>
        JsonSerializer.Deserialize<CatalogOverrides>(stream, CatalogFile.Json)
            ?? throw new InvalidOperationException("The catalog overrides file is empty.");

    /// <summary>
    /// A short mark of a source row as it was when a correction was written against it.
    /// A different mark later means the source has changed that row since.
    /// </summary>
    public static string Fingerprint(CatalogBenefit benefit) =>
        System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(benefit, Compact))))[..12];

    /// <summary>The source with the corrections on top, and what a person should look at.</summary>
    public (CatalogFile Catalog, IReadOnlyList<string> Flags) Apply(CatalogFile source)
    {
        var flags = new List<string>();
        var corrections = (Products ?? []).ToDictionary(product => product.Id);

        var products = source.Products
            .Select(product => corrections.Remove(product.Id, out var correction) ? Correct(product, correction, flags) : product)
            .Select(product => Departed(product, flags))
            .ToList();

        flags.AddRange(corrections.Values.Select(orphan =>
            $"\"{orphan.Id}\" is no longer in the source, so what was corrected or added for it here is not applied."));

        foreach (var added in AddProducts ?? [])
        {
            if (products.Any(product => product.Id == added.Id))
            {
                flags.Add($"The source now has its own \"{added.Id}\", so the one added here was left out.");
                continue;
            }

            products.Add(added);
        }

        return (source with { Products = products }, flags);
    }

    private static CatalogProduct Correct(CatalogProduct product, ProductOverride correction, List<string> flags)
    {
        var benefits = product.Benefits.ToList();
        foreach (var change in correction.Benefits ?? [])
        {
            var at = benefits.FindIndex(benefit => benefit.Id == change.BenefitId);
            var current = at < 0 || benefits[at].GoneFromSource is not null ? null : benefits[at];
            var name = change.Benefit?.Name ?? current?.Name ?? change.BenefitId;
            var moved = current is not null && change.WrittenAgainst is not null && Fingerprint(current) != change.WrittenAgainst;

            if (at >= 0)
            {
                benefits.RemoveAt(at);
            }

            switch (change.Action)
            {
                case OverrideAction.Remove when current is null:
                    flags.Add($"{product.DisplayName}: the source no longer has \"{name}\" either, so hiding it here is no longer needed.");
                    break;

                case OverrideAction.Remove:
                    if (moved)
                    {
                        flags.Add(
                            $"{product.DisplayName}: the source changed \"{name}\", which is hidden here. It stays hidden; " +
                            $"check whether it is back (the source row is now {Fingerprint(current)}).");
                    }

                    break;

                case OverrideAction.Add when current is not null:
                    flags.Add($"{product.DisplayName}: the source now lists \"{name}\" itself. The version added here is still the one used; check whether it is still needed.");
                    benefits.Insert(at, Required(change));
                    break;

                case OverrideAction.Add:
                    benefits.Add(Required(change));
                    break;

                case OverrideAction.Replace when current is null:
                    flags.Add($"{product.DisplayName}: the source no longer has \"{name}\". The corrected version is still used; check whether the benefit ended.");
                    benefits.Add(Required(change));
                    break;

                case OverrideAction.Replace:
                    if (moved)
                    {
                        flags.Add(
                            $"{product.DisplayName}: the source changed \"{name}\" after it was corrected here. The correction is still used; " +
                            $"compare the two (the source row is now {Fingerprint(current)}).");
                    }

                    benefits.Insert(at, Required(change));
                    break;
            }
        }

        return product with { Benefits = benefits, Aliases = correction.Aliases ?? product.Aliases };
    }

    /// <summary>Flags what a refresh kept after the source dropped it.</summary>
    private static CatalogProduct Departed(CatalogProduct product, List<string> flags)
    {
        if (product.GoneFromSource is { } left)
        {
            flags.Add(
                $"{product.DisplayName} left the source on {left:yyyy-MM-dd}. Cards that have it keep its last known benefits; " +
                "if the source renamed it, change the card's product to the new one.");
        }

        flags.AddRange(product.Benefits
            .Where(benefit => benefit.GoneFromSource is not null)
            .Select(benefit =>
                $"{product.DisplayName}: \"{benefit.Name}\" left the source on {benefit.GoneFromSource:yyyy-MM-dd}. " +
                "It is kept as ended that day so the use recorded against it stays."));
        return product;
    }

    private static CatalogBenefit Required(BenefitOverride change) =>
        change.Benefit ?? throw new InvalidOperationException($"The override for \"{change.Id}\" must carry the benefit it adds or replaces.");
}

/// <summary>Corrections to one product of the source, found by the source's id for it.</summary>
internal sealed record ProductOverride(
    string Id, IReadOnlyList<string>? Aliases = null, IReadOnlyList<BenefitOverride>? Benefits = null);

/// <summary>
/// One correction to a benefit, with the reason it was made. Add and replace carry the
/// benefit; remove names it by id. Replace and remove also record the mark of the source
/// row they were written against.
/// </summary>
internal sealed record BenefitOverride(
    OverrideAction Action, string Reason, string? Id = null, string? WrittenAgainst = null, CatalogBenefit? Benefit = null)
{
    public string BenefitId =>
        Benefit?.Id ?? Id ?? throw new InvalidOperationException("An override must name a benefit, by its id or by carrying it.");
}

internal enum OverrideAction
{
    Add,
    Replace,
    Remove,
}
