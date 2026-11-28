namespace Chitragupta.Contracts;

// What the API returns. Plain data only, so the web app can share these types
// without pulling in the domain. Amounts are dollars.

public sealed record PersonDto(Guid Id, string Name);

/// <summary>A card product under the name its source gives it. <c>AlsoCalled</c> holds other names, for searching only.</summary>
public sealed record ProductDto(
    string Id, string DisplayName, decimal AnnualFee, bool IsBusiness, string? PointsProgram, IReadOnlyList<string>? AlsoCalled = null);

public sealed record BenefitDto(
    string Id, string Name, bool Manual, decimal Amount, string Cadence, string YearBasis, string? Notes);

/// <summary>
/// A card of the household. <c>HasBenefits</c> is false when there is nothing recurring to track on it,
/// which is what lets the benefit views leave such a card out.
/// </summary>
public sealed record CardDto(
    Guid Id,
    Guid PersonId,
    string PersonName,
    string ProductId,
    string ProductName,
    string? LastFour,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int AnniversaryMonth,
    int AnniversaryDay,
    decimal AnnualFee,
    DateOnly NextFeeDate,
    string? PointsProgram,
    IReadOnlyList<string> LinkedAccounts,
    bool HasBenefits = true);

public sealed record PeriodDto(
    Guid Id,
    Guid CardId,
    string CardName,
    string PersonName,
    string BenefitId,
    string BenefitName,
    string? Notes,
    bool Manual,
    DateOnly Start,
    DateOnly End,
    decimal Amount,
    decimal Used,
    decimal Remaining,
    string Status,
    int DaysLeft,
    bool HasDollarValue,
    bool HasManualUsage);

public sealed record CardPeriodsDto(CardDto Card, IReadOnlyList<PeriodDto> Periods);

public sealed record CardValueDto(
    CardDto Card, DateOnly YearStart, DateOnly YearEnd, decimal Used, decimal Available, decimal AnnualFee, decimal Net);

public sealed record BenefitChoiceDto(string Id, string Name);

public sealed record ReviewItemDto(
    Guid Id,
    string Reason,
    Guid CardId,
    string CardName,
    string PersonName,
    Guid TransactionId,
    DateOnly? PostedOn,
    decimal Amount,
    decimal TransactionAmount,
    string Description,
    string? BenefitId,
    string? BenefitName,
    IReadOnlyList<BenefitChoiceDto> Benefits);

public sealed record NewAccountDto(
    Guid Id,
    string Feed,
    string ConnectionName,
    string Name,
    string? LastFour,
    int TransactionCount,
    bool LooksLikeCard,
    string? SuggestedProductId,
    IReadOnlyList<Guid> SuggestedCardIds,
    int? InferredAnniversaryMonth,
    int? InferredAnniversaryDay);

public sealed record FeedAccountDto(Guid Id, string Feed, string ConnectionName, string Name, string Status, Guid? CardId);

public sealed record SyncRunDto(
    DateTimeOffset StartedAt,
    string Feed,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    int Accounts,
    int Received,
    int Changed,
    DateOnly? NewestReturned,
    DateOnly? NewestStored,
    string? Failure,
    IReadOnlyList<string> Errors);

public sealed record ConnectionHealthDto(
    string ConnectionName, DateTimeOffset? LastOkAt, string? LastError, DateTimeOffset? LastErrorAt, int Cards);

public sealed record SyncHealthDto(
    bool FeedConfigured,
    DateTimeOffset? LastRunAt,
    string? LastFailure,
    DateTimeOffset? NextManualSyncAt,
    IReadOnlyList<ConnectionHealthDto> Connections,
    IReadOnlyList<SyncRunDto> RecentRuns);

public sealed record SyncNowResponse(bool Started, DateTimeOffset? RetryAfter, SyncRunDto? Run);

public sealed record CatalogStatusDto(
    bool FollowsSource,
    bool RefreshesItself,
    string? Source,
    int Products,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset? CheckedAt,
    string? Problem,
    IReadOnlyList<string> Flags);

/// <summary>The counts beside the navigation. <c>BalanceProblems</c> is the automatic balance checks that need a look.</summary>
public sealed record SummaryDto(int ExpiringSoon, int ReviewItems, int NewAccounts, int SyncProblems, int BalanceProblems = 0);

public sealed record ImportResultDto(int Received, int Changed);

/// <summary>
/// A person's miles or points in one program, as last read. <c>AgeDays</c> says how old the reading is.
/// <c>ExpiresSoon</c> is true from the first expiry reminder onward. <c>Stale</c> is null while the
/// reading is recent; otherwise it says why it is old: the status of the check that is failing,
/// "Overdue" when the check simply has not run, or "NotChecked" when nothing checks it any more.
/// </summary>
public sealed record BalanceDto(
    Guid PersonId,
    string PersonName,
    string Program,
    long Amount,
    DateTimeOffset AsOf,
    int AgeDays,
    string Source,
    DateOnly? ExpiresOn,
    int? DaysUntilExpiry,
    bool ExpiresSoon,
    string? Stale = null);

/// <summary>
/// How the latest automatic check of one account went, with its recent history. Any status but
/// "Ok" needs a look, and so does a check that is <c>Overdue</c>: nothing was heard for over a day.
/// </summary>
public sealed record BalanceCheckDto(
    string Account, string Owner, string Program, string Status, string? Problem, DateTimeOffset CheckedAt)
{
    public string? ErrorCode { get; init; }

    /// <summary>What a person can do about the problem, when there is something.</summary>
    public string? Advice { get; init; }

    /// <summary>When the failures the account is in began.</summary>
    public DateTimeOffset? FailingSince { get; init; }

    public bool Overdue { get; init; }

    /// <summary>True while the failure is of a kind that is tried again without anyone asking.</summary>
    public bool RetriesItself { get; init; }

    public int SignInsToday { get; init; }

    /// <summary>True when a person may ask for a check now: the automation answers and today's limit is not reached.</summary>
    public bool CanCheckNow { get; init; }

    /// <summary>True while a check a person asked for is waiting or running.</summary>
    public bool Queued { get; init; }

    /// <summary>The latest attempts, oldest first.</summary>
    public IReadOnlyList<BalanceAttemptDto> Recent { get; init; } = [];
}

public sealed record BalanceAttemptDto(DateTimeOffset CheckedAt, string Status, bool SignedIn, string? ErrorCode);

/// <summary>The automation the tracker asks for checks. <c>UnreachableSince</c> is set while it does not answer.</summary>
public sealed record BalanceCheckerDto(
    DateTimeOffset? UnreachableSince, string? Problem, string? Running, TimeOnly DailyAt, int SignInsPerDay);

/// <summary><c>Checker</c> is null when no automation is connected and results only arrive pushed.</summary>
public sealed record BalancesDto(
    IReadOnlyList<BalanceDto> Balances, IReadOnlyList<BalanceCheckDto> Checks, BalanceCheckerDto? Checker = null);

/// <summary>What became of one account in an automation's output. <c>Recorded</c> is false when the reading was already known.</summary>
public sealed record BalanceImportLineDto(string Account, string Status, bool Recorded, string? Problem);

/// <summary>How reminders and alerts leave by email.</summary>
/// <param name="CanSetUp">Whether the service has what a sign-in needs.</param>
/// <param name="IsSetUp">Whether somebody approved sending.</param>
/// <param name="Account">The mailbox that sends, when known.</param>
public sealed record EmailAlertsDto(bool CanSetUp, bool IsSetUp, string? Account);

/// <summary>Where to send the browser to approve sending mail.</summary>
public sealed record EmailSignInDto(string Url);
