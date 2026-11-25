namespace CardBenefits.Domain;

public enum UsageSource
{
    /// <summary>Matched from a statement credit. Rebuilt on every replay.</summary>
    Automatic,

    /// <summary>Entered by a person. Never touched by a replay.</summary>
    Manual,
}
