using CardBenefits.Domain;

namespace CardBenefits.Application.Balances;

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
    ReportedBalance? Balance);

public sealed record ReportedBalance(long Amount, DateOnly? ExpiresOn, DateTimeOffset AsOf);
