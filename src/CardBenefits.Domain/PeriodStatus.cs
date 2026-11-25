namespace CardBenefits.Domain;

public enum PeriodStatus
{
    /// <summary>The period has not started.</summary>
    Upcoming,

    /// <summary>Running, with nothing used yet.</summary>
    Open,

    /// <summary>Running, with some value left.</summary>
    PartlyUsed,

    /// <summary>Nothing left to use.</summary>
    Used,

    /// <summary>Ended with value left.</summary>
    Expired,
}
