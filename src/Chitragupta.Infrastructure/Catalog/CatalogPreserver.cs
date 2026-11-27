namespace Chitragupta.Infrastructure.Catalog;

/// <summary>
/// Decides what of the old catalog must outlive a refresh. The source may drop or rename
/// anything; a card the household holds and the use recorded against a benefit may not
/// disappear with it. So a dropped product stays while a card has it, and a dropped
/// benefit stays, as ended, while a period of it has recorded use. Both carry the day
/// they left the source, which is what gets them flagged.
/// </summary>
internal static class CatalogPreserver
{
    public static CatalogFile Keep(
        CatalogFile previous,
        CatalogFile fresh,
        IReadOnlyCollection<string> productsOnCards,
        IReadOnlyCollection<string> benefitsWithUse,
        DateOnly today)
    {
        var held = productsOnCards.ToHashSet();
        var used = benefitsWithUse.ToHashSet();
        var before = previous.Products.ToDictionary(product => product.Id);
        var arriving = fresh.Products.Select(product => product.Id).ToHashSet();

        var products = fresh.Products
            .Select(product => before.TryGetValue(product.Id, out var old)
                ? product with { Benefits = [.. product.Benefits, .. Dropped(old, product, used, today)] }
                : product)
            .Concat(previous.Products
                .Where(old => !arriving.Contains(old.Id) && held.Contains(old.Id))
                .Select(old => old with { GoneFromSource = old.GoneFromSource ?? today }))
            .ToArray();

        return fresh with { Products = products };
    }

    private static IEnumerable<CatalogBenefit> Dropped(
        CatalogProduct old, CatalogProduct now, HashSet<string> benefitsWithUse, DateOnly today)
    {
        var still = now.Benefits.Select(benefit => benefit.Id).ToHashSet();
        return old.Benefits
            .Where(benefit => !still.Contains(benefit.Id) && benefitsWithUse.Contains(benefit.Id))
            .Select(benefit => benefit.GoneFromSource is not null
                ? benefit
                : benefit with
                {
                    GoneFromSource = today,
                    LastDay = benefit.LastDay is { } last && last < today ? last : today,
                });
    }
}
