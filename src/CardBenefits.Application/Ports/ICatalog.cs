using CardBenefits.Domain;

namespace CardBenefits.Application.Ports;

/// <summary>The card products, their benefits and the rules that recognise their statement credits.</summary>
public interface ICatalog
{
    IReadOnlyList<CardProduct> Products { get; }

    CardProduct? FindProduct(string productId);

    IReadOnlyList<BenefitDefinition> BenefitsOf(string productId);

    BenefitDefinition? FindBenefit(string benefitId);

    /// <summary>Rules shipped with the catalog. Rules a person creates live in the store.</summary>
    IReadOnlyList<MatchRule> Rules { get; }

    /// <summary>Descriptions the matcher skips outright, such as card payments.</summary>
    IReadOnlyList<string> IgnoreKeywords { get; }
}
