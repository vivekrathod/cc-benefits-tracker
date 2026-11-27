using Chitragupta.Application.Cards;
using Chitragupta.Application.Feeds;
using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Sync;

/// <summary>
/// Pulls one feed into the store and replays matching. Used by the daily worker,
/// by "sync now", and by CSV imports, which are just another feed.
/// </summary>
public sealed class SyncService(
    ITrackerStore store, CardService cards, Rematcher rematcher, IClock clock, TrackerSettings settings)
{
    // The Bridge cuts a request off at 90 days.
    private const int LongestWindowDays = 89;

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <param name="mapNewAccountsTo">
    /// Set by a CSV import, where the person has already said which card the file belongs to.
    /// </param>
    public async Task<SyncRun> RunAsync(
        ITransactionFeed feed, DateRange window, Guid? mapNewAccountsTo, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            return await RunLockedAsync(feed, window, mapNewAccountsTo, cancellationToken);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Runs the daily sync when today's has not started yet. Returns null when it is not due.</summary>
    public async Task<SyncRun?> RunDailyIfDueAsync(ITransactionFeed feed, CancellationToken cancellationToken)
    {
        var last = await LastRunAsync(feed.Name, cancellationToken);
        var lastStarted = last is null ? (DateTimeOffset?)null : clock.ToLocal(last.StartedAt);
        if (!IsDailyDue(clock.Now, lastStarted, settings.DailySyncAt))
        {
            return null;
        }

        return await RunAsync(feed, await NextWindowAsync(feed.Name, cancellationToken), null, cancellationToken);
    }

    /// <summary>"Sync now". Refused while the previous request is too recent; the result says when to retry.</summary>
    public async Task<(SyncRun? Run, DateTimeOffset? RetryAfter)> RunNowAsync(
        ITransactionFeed feed, CancellationToken cancellationToken)
    {
        var retryAfter = await NextManualSyncAsync(feed.Name, cancellationToken);
        if (retryAfter is not null)
        {
            return (null, retryAfter);
        }

        return (await RunAsync(feed, await NextWindowAsync(feed.Name, cancellationToken), null, cancellationToken), null);
    }

    /// <summary>When "sync now" is next allowed, or null when it is allowed already.</summary>
    public async Task<DateTimeOffset?> NextManualSyncAsync(string feedName, CancellationToken cancellationToken)
    {
        var last = await LastRunAsync(feedName, cancellationToken);
        var allowedAt = last?.StartedAt.AddMinutes(settings.MinutesBetweenSyncs);
        return allowedAt > clock.Now ? clock.ToLocal(allowedAt.Value) : null;
    }

    /// <summary>
    /// The rolling window: normally the last two weeks, wider on the first run or after
    /// a gap, so nothing between two successful syncs is ever skipped.
    /// </summary>
    public async Task<DateRange> NextWindowAsync(string feedName, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var runs = await store.GetRecentSyncRunsAsync(200, cancellationToken);
        var lastGood = runs.FirstOrDefault(run => run.Feed == feedName && run.Succeeded);

        var days = lastGood is null
            ? settings.FirstSyncWindowDays
            : Math.Max(
                settings.SyncWindowDays,
                today.DayNumber - clock.ToLocalDate(lastGood.StartedAt).DayNumber + settings.SyncWindowDays);

        return new DateRange(today.AddDays(-Math.Min(days, LongestWindowDays)), today);
    }

    public static bool IsDailyDue(DateTimeOffset now, DateTimeOffset? lastStarted, TimeOnly dailyAt)
    {
        var todaysSlot = now.Date + dailyAt.ToTimeSpan();
        return now.DateTime >= todaysSlot && (lastStarted is null || lastStarted.Value.DateTime < todaysSlot);
    }

    private async Task<SyncRun?> LastRunAsync(string feedName, CancellationToken cancellationToken) =>
        (await store.GetRecentSyncRunsAsync(200, cancellationToken)).FirstOrDefault(run => run.Feed == feedName);

    private async Task<SyncRun> RunLockedAsync(
        ITransactionFeed feed, DateRange window, Guid? mapNewAccountsTo, CancellationToken cancellationToken)
    {
        var startedAt = clock.Now;
        FeedSnapshot snapshot;
        try
        {
            snapshot = await feed.FetchAsync(window, cancellationToken);
        }
        catch (FeedUnavailableException failure)
        {
            var failed = new SyncRun(
                Guid.NewGuid(), feed.Name, startedAt, clock.Now, window, 0, 0, 0, null,
                await store.GetNewestPostedDateAsync(feed.Name, cancellationToken), failure.Message, []);
            await store.AddSyncRunAsync(failed, cancellationToken);
            return failed;
        }

        var known = (await store.GetFeedAccountsAsync(cancellationToken))
            .Where(account => account.Feed == feed.Name)
            .ToDictionary(account => account.AccountId);

        var received = 0;
        var changed = 0;
        DateOnly? newestReturned = null;

        foreach (var data in snapshot.Accounts)
        {
            var account = await TrackAsync(feed.Name, data, known, mapNewAccountsTo, startedAt, cancellationToken);
            if (account.Status == FeedAccountStatus.Dismissed)
            {
                continue;
            }

            received += data.Transactions.Count;
            changed += await store.UpsertTransactionsAsync(account.Id, data.Transactions, startedAt, cancellationToken);

            var newest = data.Transactions.Max(transaction => transaction.PostedOn);
            if (newest is not null && (newestReturned is null || newest > newestReturned))
            {
                newestReturned = newest;
            }
        }

        // A new account that can only be one card is linked here, so its credits count at once.
        await cards.LinkCertainAsync(cancellationToken);
        await rematcher.RematchAllAsync(cancellationToken);

        var run = new SyncRun(
            Guid.NewGuid(),
            feed.Name,
            startedAt,
            clock.Now,
            window,
            snapshot.Accounts.Count,
            received,
            changed,
            newestReturned,
            await store.GetNewestPostedDateAsync(feed.Name, cancellationToken),
            null,
            snapshot.Errors.Select(error => new SyncError(error.Code, error.Message, error.ConnectionName)).ToArray());
        await store.AddSyncRunAsync(run, cancellationToken);
        return run;
    }

    /// <summary>
    /// Finds the feed account or records it as new. An account the service has never
    /// seen waits in the queue; it is never dropped and never becomes a new card by itself.
    /// </summary>
    private async Task<FeedAccount> TrackAsync(
        string feedName,
        FeedAccountData data,
        Dictionary<string, FeedAccount> known,
        Guid? mapNewAccountsTo,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (known.TryGetValue(data.AccountId, out var account))
        {
            if (account.Name != data.Name || account.ConnectionName != data.ConnectionName)
            {
                account.Rename(data.ConnectionName, data.Name);
                await store.SaveFeedAccountAsync(account, cancellationToken);
            }

            return account;
        }

        account = new FeedAccount(Guid.NewGuid(), feedName, data.ConnectionName, data.AccountId, data.Name, now);
        if (mapNewAccountsTo is not null)
        {
            account.MapTo(mapNewAccountsTo.Value);
        }

        known[data.AccountId] = account;
        await store.SaveFeedAccountAsync(account, cancellationToken);
        return account;
    }
}
