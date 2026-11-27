namespace Chitragupta.Domain;

/// <summary>What a benefit's year is counted from.</summary>
public enum YearBasis
{
    /// <summary>Periods start on 1 January.</summary>
    Calendar,

    /// <summary>Periods start on the card's anniversary.</summary>
    Cardmember,
}
