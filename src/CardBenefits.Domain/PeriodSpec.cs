namespace CardBenefits.Domain;

/// <summary>A benefit period as the generator computes it: its dates and the amount available in it.</summary>
public sealed record PeriodSpec(DateRange Range, Money Amount);
