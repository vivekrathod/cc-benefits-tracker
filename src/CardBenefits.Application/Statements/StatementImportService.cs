using CardBenefits.Application.Feeds;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Sync;
using CardBenefits.Domain;

namespace CardBenefits.Application.Statements;

/// <summary>
/// Backfills a card from an issuer's CSV export. The file is treated as one more
/// feed, so it goes through the same upsert and replay as the live sync, and
/// importing the same file twice changes nothing.
/// </summary>
public sealed class StatementImportService(ITrackerStore store, IStatementParser parser, SyncService sync)
{
    public async Task<SyncRun> ImportAsync(Guid cardId, string csv, CancellationToken cancellationToken)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken) ?? throw new NotFoundException("The card");
        var transactions = parser.Parse(csv);
        if (transactions.Count == 0)
        {
            throw new RequestRejectedException("The file has no transactions.");
        }

        var feed = new StatementFeed(card.Id, transactions);
        return await sync.RunAsync(feed, feed.Window, card.Id, cancellationToken);
    }

    /// <summary>One card's imported statements, presented as a feed with a single account.</summary>
    private sealed class StatementFeed(Guid cardId, IReadOnlyList<FeedTransaction> transactions) : ITransactionFeed
    {
        public string Name => TrackerSettings.CsvFeedName;

        public bool IsConfigured => true;

        public DateRange Window { get; } = new(
            transactions.Min(transaction => transaction.PostedOn!.Value),
            transactions.Max(transaction => transaction.PostedOn!.Value));

        public Task<FeedSnapshot> FetchAsync(DateRange window, CancellationToken cancellationToken) =>
            Task.FromResult(new FeedSnapshot(
                [new FeedAccountData("CSV import", cardId.ToString(), "Imported statements", transactions)], []));
    }
}
