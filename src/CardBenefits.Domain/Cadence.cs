namespace CardBenefits.Domain;

/// <summary>How often a benefit renews.</summary>
public enum Cadence
{
    Monthly,
    Quarterly,
    Semiannual,
    Annual,
}

public static class CadenceExtensions
{
    public static int Months(this Cadence cadence) => cadence switch
    {
        Cadence.Monthly => 1,
        Cadence.Quarterly => 3,
        Cadence.Semiannual => 6,
        Cadence.Annual => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, null),
    };
}
