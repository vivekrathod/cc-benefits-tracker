namespace CardBenefits.Domain;

/// <summary>How use of a benefit becomes known.</summary>
public enum DetectionMode
{
    /// <summary>A statement credit posts to the card and is matched.</summary>
    Automatic,

    /// <summary>Nothing reaches the statement, so a person marks it used.</summary>
    Manual,
}
