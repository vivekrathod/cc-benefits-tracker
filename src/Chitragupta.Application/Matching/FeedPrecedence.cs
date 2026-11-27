using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Matching;

/// <summary>
/// A card can have several feed accounts: a CSV backfill next to the live feed, or the
/// old and new account of a re-linked bank connection. Their histories overlap, and
/// counting both would count a credit twice. The rule: a better source owns every day
/// from its earliest transaction onward, and a lesser source is only used for the days
/// before that.
/// </summary>
public static class FeedPrecedence
{
    public static IReadOnlyList<CardTransaction> Merge(
        Guid cardId,
        IEnumerable<(FeedAccount Account, IReadOnlyList<StoredTransaction> Transactions)> sources,
        DateOnly today)
    {
        var merged = new List<CardTransaction>();
        var cutover = DateOnly.MaxValue;

        // Live feeds outrank CSV imports; within a feed, the account seen last is the current one.
        var ordered = sources
            .OrderBy(source => source.Account.Feed == TrackerSettings.CsvFeedName ? 1 : 0)
            .ThenByDescending(source => source.Account.FirstSeenAt)
            .ThenBy(source => source.Account.Id);

        foreach (var (_, transactions) in ordered)
        {
            if (transactions.Count == 0)
            {
                continue;
            }

            merged.AddRange(transactions
                .Select(transaction => (Transaction: transaction, Date: DateOf(transaction, today)))
                .Where(dated => dated.Date < cutover)
                .Select(dated => new CardTransaction(
                    dated.Transaction.Id,
                    cardId,
                    dated.Transaction.FeedId,
                    dated.Date,
                    dated.Transaction.Amount,
                    dated.Transaction.Description,
                    dated.Transaction.Pending)));

            var earliest = transactions.Min(transaction => DateOf(transaction, today));
            if (earliest < cutover)
            {
                cutover = earliest;
            }
        }

        return merged;
    }

    private static DateOnly DateOf(StoredTransaction transaction, DateOnly today) =>
        transaction.PostedOn ?? transaction.TransactedOn ?? today;
}
