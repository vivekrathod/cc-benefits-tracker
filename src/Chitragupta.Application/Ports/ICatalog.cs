using Chitragupta.Domain;

namespace Chitragupta.Application.Ports;

/// <summary>The card products, their benefits and the rules that recognise their statement credits.</summary>
public interface ICatalog
{
    IReadOnlyList<CardProduct> Products { get; }

    CardProduct? FindProduct(string productId);

    IReadOnlyList<BenefitDefinition> BenefitsOf(string productId);

    BenefitDefinition? FindBenefit(string benefitId);

    /// <summary>Other names people know the product by, so a search finds it. The catalog keeps the source's own name.</summary>
    IReadOnlyList<string> AliasesOf(string productId);

    /// <summary>A line on how the benefit works, shown next to it.</summary>
    string? NotesOf(string benefitId);

    /// <summary>Rules shipped with the catalog. Rules a person creates live in the store.</summary>
    IReadOnlyList<MatchRule> Rules { get; }

    /// <summary>Descriptions the matcher skips outright, such as card payments.</summary>
    IReadOnlyList<string> IgnoreKeywords { get; }

    /// <summary>Grace windows that differ from the default, by benefit id.</summary>
    IReadOnlyDictionary<string, int> GraceDays { get; }
}
