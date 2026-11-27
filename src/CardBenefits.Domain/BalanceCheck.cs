namespace CardBenefits.Domain;

public enum BalanceCheckStatus
{
    Ok,

    /// <summary>The program's site turned the automation away. It pauses by itself and tries again later.</summary>
    Blocked,

    /// <summary>The automation stopped at something only a person can resolve, such as a rejected login.</summary>
    NeedsUser,

    Failed,

    /// <summary>The check worked, but nobody in the tracker goes by the owner's name.</summary>
    UnknownOwner,

    /// <summary>The automation reported a program the tracker cannot read a balance for.</summary>
    UnknownProgram,
}

/// <summary>
/// How the latest attempt to read one account's balance went. Kept apart from the balances,
/// so a failed attempt never replaces the last number that was read.
/// </summary>
public sealed record BalanceCheck(
    string Account, string Owner, string Program, BalanceCheckStatus Status, string? Problem, DateTimeOffset CheckedAt)
{
    public bool NeedsAttention => Status != BalanceCheckStatus.Ok;
}
