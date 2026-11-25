namespace CardBenefits.Domain;

/// <summary>Part or all of a credit counted against a benefit period.</summary>
public sealed record Allocation(Guid TransactionId, Guid PeriodId, Money Amount);

public enum ReviewReason
{
    /// <summary>A credit that matched no rule.</summary>
    Unmatched,

    /// <summary>The part of a credit beyond what the period had left.</summary>
    Excess,

    /// <summary>A credit that matched a benefit with no period on that date.</summary>
    NoPeriod,

    /// <summary>A debit whose description matches a benefit, such as a clawed-back credit.</summary>
    Reversal,
}

/// <summary>Something the matcher could not settle and a person should look at.</summary>
public sealed record ReviewFlag(Guid TransactionId, ReviewReason Reason, Money Amount, string? BenefitId);

public sealed record MatchResult(IReadOnlyList<Allocation> Allocations, IReadOnlyList<ReviewFlag> Review);
