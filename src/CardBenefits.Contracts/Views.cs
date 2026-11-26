namespace CardBenefits.Contracts;

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

public sealed record SummaryDto(int ExpiringSoon, int ReviewItems, int NewAccounts, int SyncProblems);

public sealed record ImportResultDto(int Received, int Changed);
