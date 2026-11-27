using Chitragupta.Application.Feeds;
using Chitragupta.Application.Review;
using Chitragupta.Application.Sync;
using Chitragupta.Domain;

namespace Chitragupta.Application.Ports;

/// <summary>
/// Persistence for everything the household owns. One port, so a use case reads and
/// writes through a single unit and the database stays a detail of Infrastructure.
/// </summary>
public interface ITrackerStore
{
    // People and cards
    Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken cancellationToken);

    Task SavePersonAsync(Person person, CancellationToken cancellationToken);

    Task<IReadOnlyList<Card>> GetCardsAsync(CancellationToken cancellationToken);

    Task<Card?> FindCardAsync(Guid cardId, CancellationToken cancellationToken);

    Task SaveCardAsync(Card card, CancellationToken cancellationToken);

    /// <summary>Removes a card with its periods and review items. Its feed accounts stay.</summary>
    Task DeleteCardAsync(Guid cardId, CancellationToken cancellationToken);

    // Feed accounts
    Task<IReadOnlyList<FeedAccount>> GetFeedAccountsAsync(CancellationToken cancellationToken);

    Task<FeedAccount?> FindFeedAccountAsync(Guid feedAccountId, CancellationToken cancellationToken);

    Task SaveFeedAccountAsync(FeedAccount account, CancellationToken cancellationToken);

    // Transactions
    /// <summary>
    /// Inserts new transactions and overwrites stored ones whose content changed. Stored
    /// pending transactions missing from the batch are removed: they either posted under
    /// a new id or were dropped. Returns how many rows were inserted, changed or removed.
    /// </summary>
    Task<int> UpsertTransactionsAsync(
        Guid feedAccountId, IReadOnlyCollection<FeedTransaction> transactions, DateTimeOffset seenAt, CancellationToken cancellationToken);

    Task DeleteTransactionsAsync(Guid feedAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredTransaction>> GetTransactionsAsync(Guid feedAccountId, CancellationToken cancellationToken);

    Task<StoredTransaction?> FindTransactionAsync(Guid transactionId, CancellationToken cancellationToken);

    Task<DateOnly?> GetNewestPostedDateAsync(string feed, CancellationToken cancellationToken);

    // Periods and usage
    Task<IReadOnlyList<BenefitPeriod>> GetPeriodsAsync(Guid? cardId, CancellationToken cancellationToken);

    Task<BenefitPeriod?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken);

    /// <summary>Saves each period with its usage, replacing what was stored for it.</summary>
    Task SavePeriodsAsync(IReadOnlyCollection<BenefitPeriod> periods, CancellationToken cancellationToken);

    Task DeletePeriodsAsync(IReadOnlyCollection<Guid> periodIds, CancellationToken cancellationToken);

    // Review queue, a person's decisions and learned rules
    Task<IReadOnlyList<ReviewItem>> GetReviewItemsAsync(CancellationToken cancellationToken);

    Task<ReviewItem?> FindReviewItemAsync(Guid reviewItemId, CancellationToken cancellationToken);

    Task ReplaceReviewItemsAsync(Guid cardId, IReadOnlyCollection<ReviewFlag> flags, CancellationToken cancellationToken);

    /// <summary>Decisions by transaction id: a benefit id, or null for "not a benefit".</summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetPinsAsync(CancellationToken cancellationToken);

    Task SavePinAsync(Guid transactionId, string? benefitId, CancellationToken cancellationToken);

    /// <summary>Flags a person has seen and accepted, so a replay does not raise them again.</summary>
    Task<IReadOnlySet<(Guid TransactionId, ReviewReason Reason)>> GetAcknowledgedFlagsAsync(CancellationToken cancellationToken);

    Task AcknowledgeFlagAsync(Guid transactionId, ReviewReason reason, CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchRule>> GetLearnedRulesAsync(CancellationToken cancellationToken);

    Task AddLearnedRuleAsync(MatchRule rule, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetLearnedIgnoreKeywordsAsync(CancellationToken cancellationToken);

    Task AddLearnedIgnoreKeywordAsync(string keyword, CancellationToken cancellationToken);

    // Sync log
    Task AddSyncRunAsync(SyncRun run, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncRun>> GetRecentSyncRunsAsync(int count, CancellationToken cancellationToken);

    // Reminders already sent, so a nudge goes out once
    Task<bool> TryRecordReminderAsync(string key, DateTimeOffset sentAt, CancellationToken cancellationToken);

    Task<bool> HasReminderAsync(string key, CancellationToken cancellationToken);

    Task ForgetReminderAsync(string key, CancellationToken cancellationToken);

    // Miles and points
    /// <summary>The newest reading of every balance, one per person and program.</summary>
    Task<IReadOnlyList<ProgramBalance>> GetLatestBalancesAsync(CancellationToken cancellationToken);

    Task AddBalanceAsync(ProgramBalance balance, CancellationToken cancellationToken);

    /// <summary>Removes every reading of one person's balance in one program.</summary>
    Task DeleteBalancesAsync(Guid personId, string program, CancellationToken cancellationToken);

    /// <summary>The newest attempt of every account: how each is doing now.</summary>
    Task<IReadOnlyList<BalanceCheck>> GetBalanceChecksAsync(CancellationToken cancellationToken);

    /// <summary>Every attempt since a moment, oldest first.</summary>
    Task<IReadOnlyList<BalanceCheck>> GetBalanceAttemptsAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Adds an attempt to an account's history. An attempt already known, by account and time, is replaced.</summary>
    Task SaveBalanceCheckAsync(BalanceCheck check, CancellationToken cancellationToken);

    /// <summary>Forgets every attempt of one account. Its balances stay.</summary>
    Task<int> DeleteBalanceChecksAsync(string account, CancellationToken cancellationToken);

    /// <summary>Removes attempts older than a moment, except the newest of each account.</summary>
    Task<int> DeleteBalanceAttemptsBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>Writes a consistent copy of the whole database to a file.</summary>
    Task BackupAsync(string path, CancellationToken cancellationToken);
}

/// <summary>A transaction as stored, with the feed account it came from.</summary>
public sealed record StoredTransaction(
    Guid Id,
    Guid FeedAccountId,
    string FeedId,
    DateOnly? PostedOn,
    DateOnly? TransactedOn,
    Money Amount,
    string Description,
    bool Pending);
