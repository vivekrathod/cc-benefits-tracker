namespace Chitragupta.Domain;

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
/// How one attempt to read an account's balance went. Every attempt is kept, and the newest is
/// how the account is doing now. Kept apart from the balances, so a failed attempt never
/// replaces the last number that was read.
/// </summary>
public sealed record BalanceCheck(
    string Account, string Owner, string Program, BalanceCheckStatus Status, string? Problem, DateTimeOffset CheckedAt)
{
    /// <summary>The automation's own short name for what went wrong, such as "login_rejected".</summary>
    public string? ErrorCode { get; init; }

    /// <summary>True when trying again later, with nothing changed, has a fair chance of working.</summary>
    public bool Retryable { get; init; }

    /// <summary>False when the automation declined without signing in, as it does while an account is paused.</summary>
    public bool SignedIn { get; init; } = true;

    /// <summary>What a person can do about the problem, when there is something.</summary>
    public string? Advice { get; init; }

    public double? DurationSeconds { get; init; }

    public string? ToolVersion { get; init; }

    /// <summary>The automation's own account of the steps it took, kept as it was given for whoever looks into a failure.</summary>
    public string? Trail { get; init; }

    public bool NeedsAttention => Status != BalanceCheckStatus.Ok;

    /// <summary>A failure that a person has to act on: it will not clear by waiting or trying again.</summary>
    public bool NeedsAPerson => Status switch
    {
        BalanceCheckStatus.Ok or BalanceCheckStatus.Blocked => false,
        BalanceCheckStatus.Failed => !Retryable,
        _ => true,
    };
}
