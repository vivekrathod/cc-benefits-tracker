namespace Chitragupta.Domain;

/// <summary>
/// Ties a statement credit to a benefit: the keyword must appear in the description,
/// and the credit may not exceed the ceiling when one is set. A rule that has not been
/// checked against a real statement is unverified: its matches are suggested to a
/// person instead of being counted.
/// </summary>
public sealed record MatchRule(string BenefitId, string Keyword, Money? AmountCeiling = null, bool Verified = true);
