using Chitragupta.Domain;

namespace Chitragupta.Application.Balances;

/// <summary>
/// What a balance automation said about one of its accounts: how the check went and, when
/// it has one, a reading. A failed check may still carry the last reading that worked.
/// </summary>
public sealed record BalanceReport(
    string Account,
    string Owner,
    string Program,
    BalanceCheckStatus Status,
    string? Problem,
    DateTimeOffset? CheckedAt,
    ReportedBalance? Balance)
{
    public string? ErrorCode { get; init; }

    public bool Retryable { get; init; }

    /// <summary>False when the automation declined without signing in.</summary>
    public bool SignedIn { get; init; } = true;

    /// <summary>True when the automation only said "not now": nothing was tried and there is nothing to keep.</summary>
    public bool Deferred { get; init; }

    public string? Advice { get; init; }

    public double? DurationSeconds { get; init; }

    public string? ToolVersion { get; init; }

    public string? Trail { get; init; }
}

public sealed record ReportedBalance(long Amount, DateOnly? ExpiresOn, DateTimeOffset AsOf);
