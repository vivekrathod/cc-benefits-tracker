namespace CardBenefits.Domain;

/// <summary>
/// What a benefit is worth and how often it renews, from a date onward.
/// A benefit whose amount or cadence changes has one set of terms per change.
/// </summary>
public sealed record BenefitTerms(DateOnly EffectiveFrom, Money Amount, Cadence Cadence, YearBasis YearBasis);
