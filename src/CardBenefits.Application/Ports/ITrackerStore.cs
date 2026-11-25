using CardBenefits.Application.Feeds;
using CardBenefits.Application.Review;
using CardBenefits.Application.Sync;
using CardBenefits.Domain;

namespace CardBenefits.Application.Ports;

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

    // Feed accounts
    Task<IReadOnlyList<FeedAccount>> GetFeedAccountsAsync(CancellationToken cancellationToken);

    Task<FeedAccount?> FindFeedAccountAsync(Guid feedAccountId, CancellationToken cancellationToken);

    Task SaveFeedAccountAsync(FeedAccount account, CancellationToken cancellationToken);

    // Transactions
    /// <summary>
    /// Inserts new transactions and overwrites stored ones whose content changed.
    /// Returns how many rows were inserted or changed.
    /// </summary>
    Task<int> UpsertTransactionsAsync(
        Guid feedAccountId, IReadOnlyCollection<FeedTransaction> transactions, DateTimeOffset seenAt, CancellationToken cancellationToken);

    Task DeleteTransactionsAsync(Guid feedAccountId, CancellationToken cancellationToken);

    /// <summary>Every stored transaction on the feed accounts mapped to the card.</summary>
    Task<IReadOnlyList<CardTransaction>> GetCardTransactionsAsync(Guid cardId, CancellationToken cancellationToken);

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
    Task<IReadOnlyDictionary<Guid, string?>> GetPinsAsync(Guid cardId, CancellationToken cancellationToken);

    Task SavePinAsync(Guid transactionId, string? benefitId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchRule>> GetLearnedRulesAsync(CancellationToken cancellationToken);

    Task AddLearnedRuleAsync(MatchRule rule, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetLearnedIgnoreKeywordsAsync(CancellationToken cancellationToken);

    Task AddLearnedIgnoreKeywordAsync(string keyword, CancellationToken cancellationToken);

    // Sync log
    Task AddSyncRunAsync(SyncRun run, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncRun>> GetRecentSyncRunsAsync(int count, CancellationToken cancellationToken);

    // Reminders already sent, so a nudge goes out once
    Task<bool> TryRecordReminderAsync(string key, DateTimeOffset sentAt, CancellationToken cancellationToken);
}

/// <summary>A stored transaction with the account it belongs to, for the review queue.</summary>
public sealed record StoredTransaction(
    Guid Id, Guid FeedAccountId, Guid? CardId, DateOnly? PostedOn, Money Amount, string Description, bool Pending);
