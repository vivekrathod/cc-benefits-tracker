namespace CardBenefits.Domain;

/// <summary>
/// Ties a statement credit to a benefit: the keyword must appear in the description,
/// and the credit may not exceed the ceiling when one is set.
/// </summary>
public sealed record MatchRule(string BenefitId, string Keyword, Money? AmountCeiling = null);
