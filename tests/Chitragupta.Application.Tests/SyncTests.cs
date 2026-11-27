using Chitragupta.Application.Feeds;
using Chitragupta.Application.Tests.Support;
using Chitragupta.Contracts;
using Chitragupta.Domain;
using static Chitragupta.Application.Tests.Support.FakeFeed;

namespace Chitragupta.Application.Tests;

public class SyncTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task An_account_never_seen_before_waits_in_the_queue_and_is_not_made_a_card()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex", Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));

        var run = await tracker.SyncAsync();

        Assert.True(run.Succeeded);
        Assert.Equal(1, run.AccountsSeen);
        Assert.Empty(await tracker.Store.GetCardsAsync(Ct));
        var waiting = Assert.Single(await tracker.Cards.GetNewAccountsAsync(Ct));
        Assert.Equal("1234", waiting.LastFour);
        Assert.Equal(1, waiting.TransactionCount);
    }

    [Fact]
    public async Task Confirming_a_card_matches_the_credits_already_stored()
    {
        await using var tracker = await Tracker.StartAsync();

        await tracker.CardAsync(transactions: Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));

        var november = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10");
        Assert.Equal(Money.FromDollars(10), november.Used);
        Assert.Equal(UsageSource.Automatic, Assert.Single(november.Usage).Source);
        Assert.Empty(await tracker.Cards.GetNewAccountsAsync(Ct));
    }

    [Fact]
    public async Task A_corrected_transaction_overwrites_the_stored_one_and_is_matched_again()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-10", -10, "PROVISIONAL"));
        Assert.Equal(Money.Zero, (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10")).Used);

        // The issuer later corrects amount, sign and description under the same id.
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex", Posted("t1", "2026-11-10", 7.5m, "AMEX DINING CREDIT"));
        var run = await tracker.SyncAsync();

        Assert.Equal(1, run.TransactionsChanged);
        Assert.Equal(Money.FromDollars(7.5m), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10")).Used);
    }

    [Fact]
    public async Task Syncing_the_same_data_again_changes_nothing()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));

        var again = await tracker.SyncAsync();

        Assert.Equal(1, again.TransactionsReceived);
        Assert.Equal(0, again.TransactionsChanged);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10")).Used);
    }

    [Fact]
    public async Task A_pending_credit_counts_only_once_it_posts_even_under_a_new_id()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Pending("p1", "2026-11-20", 10, "AMEX DINING CREDIT"));
        Assert.Equal(Money.Zero, (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-20")).Used);

        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex", Posted("t9", "2026-11-22", 10, "AMEX DINING CREDIT"));
        await tracker.SyncAsync();

        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-20")).Used);
        var account = Assert.Single(await tracker.Store.GetFeedAccountsAsync(Ct));
        Assert.Equal("t9", Assert.Single(await tracker.Store.GetTransactionsAsync(account.Id, Ct)).FeedId);
    }

    [Fact]
    public async Task A_dismissed_account_stays_hidden_and_keeps_no_transactions()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("chk", "Everyday Checking", "Chase", Posted("t1", "2026-11-10", 2500, "PAYROLL"));
        await tracker.SyncAsync();
        var checking = Assert.Single(await tracker.Store.GetFeedAccountsAsync(Ct));

        await tracker.Cards.DismissAsync([checking.Id], Ct);
        var run = await tracker.SyncAsync();

        Assert.Empty(await tracker.Cards.GetNewAccountsAsync(Ct));
        Assert.Empty(await tracker.Store.GetTransactionsAsync(checking.Id, Ct));
        Assert.Equal(0, run.TransactionsReceived);
    }

    [Fact]
    public async Task A_relinked_connection_maps_to_the_same_card_without_counting_credits_twice()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync(transactions:
        [
            Posted("old-1", "2026-10-08", 10, "AMEX DINING CREDIT"),
            Posted("old-2", "2026-11-10", 10, "AMEX DINING CREDIT"),
        ]);

        // The bank connection is deleted and added again: a new account id, new transaction
        // ids, and a history that only reaches back to November.
        tracker.Clock.Advance(TimeSpan.FromDays(1));
        tracker.Feed.Remove("acct-1");
        tracker.Feed.Account("acct-2", "Gold Card (1234)", "Amex", Posted("new-2", "2026-11-10", 10, "AMEX DINING CREDIT"));
        await tracker.SyncAsync();

        // Same product and same last four digits: it can only be that card, so nobody is asked.
        Assert.Empty(await tracker.Cards.GetNewAccountsAsync(Ct));
        var linked = (await tracker.Store.GetFeedAccountsAsync(Ct)).Single(account => account.AccountId == "acct-2");
        Assert.Equal(card.Id, linked.CardId);

        Assert.Single(await tracker.Store.GetCardsAsync(Ct));
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-10-08")).Used);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10")).Used);
        Assert.Empty(await tracker.Store.GetReviewItemsAsync(Ct));
    }

    [Fact]
    public async Task One_institution_in_trouble_does_not_fail_the_sync()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex", Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));
        tracker.Feed.Errors.Add(new FeedError("con.auth", "Needs to be re-authenticated.", "Citi"));

        var run = await tracker.SyncAsync();

        Assert.True(run.Succeeded);
        Assert.Equal(1, run.TransactionsChanged);
        var error = Assert.Single(run.Errors);
        Assert.Equal("Citi", error.ConnectionName);

        var health = await tracker.Queries.GetSyncHealthAsync("simplefin", true, null, Ct);
        var citi = Assert.Single(health.Connections, connection => connection.ConnectionName == "Citi");
        Assert.Equal("Needs to be re-authenticated.", citi.LastError);
        Assert.Null(citi.LastOkAt);
        var amex = Assert.Single(health.Connections, connection => connection.ConnectionName == "Amex");
        Assert.Null(amex.LastError);
        Assert.NotNull(amex.LastOkAt);
    }

    [Fact]
    public async Task Institutions_from_an_earlier_pull_still_show_when_no_live_feed_is_set_up()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex", Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));
        tracker.Feed.Errors.Add(new FeedError("con.auth", "Needs to be re-authenticated.", "Citi"));
        await tracker.SyncAsync();

        var health = await tracker.Queries.GetSyncHealthAsync("simplefin", false, null, Ct);

        Assert.False(health.FeedConfigured);
        Assert.Equal(["Amex", "Citi"], health.Connections.Select(connection => connection.ConnectionName).Order());
    }

    [Fact]
    public async Task An_outage_is_recorded_and_leaves_the_data_alone()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"));

        tracker.Clock.Advance(TimeSpan.FromDays(1));
        tracker.Feed.Outage = "The SimpleFIN Bridge answered 503.";
        var run = await tracker.SyncAsync();

        Assert.False(run.Succeeded);
        Assert.Equal("The SimpleFIN Bridge answered 503.", run.Failure);
        Assert.Equal(DateOnly.Parse("2026-11-10"), run.NewestStored);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-10")).Used);
        Assert.Equal(run.Failure, (await tracker.Queries.GetSyncHealthAsync("simplefin", true, null, Ct)).LastFailure);
    }

    [Fact]
    public async Task Each_run_records_what_was_asked_for_and_what_came_back()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex",
            Posted("t1", "2026-11-10", 10, "AMEX DINING CREDIT"), Posted("t2", "2026-11-21", -40, "RESTAURANT"));

        await tracker.SyncAsync();

        var run = Assert.Single(await tracker.Store.GetRecentSyncRunsAsync(10, Ct));
        Assert.Equal(new DateRange(DateOnly.Parse("2026-10-11"), DateOnly.Parse("2026-11-25")), run.Window);
        Assert.Equal(DateOnly.Parse("2026-11-21"), run.NewestReturned);
        Assert.Equal(DateOnly.Parse("2026-11-21"), run.NewestStored);
        Assert.Equal(2, run.TransactionsReceived);
    }

    [Fact]
    public async Task The_window_is_wide_at_first_then_two_weeks_and_widens_again_after_a_gap()
    {
        await using var tracker = await Tracker.StartAsync();

        await tracker.SyncAsync();
        tracker.Clock.Advance(TimeSpan.FromDays(1));
        await tracker.SyncAsync();
        tracker.Clock.Advance(TimeSpan.FromDays(20));
        await tracker.SyncAsync();

        Assert.Equal([46, 16, 35], tracker.Feed.Requests.Select(window => window.Days));
    }

    [Fact]
    public async Task Sync_now_is_refused_while_the_last_request_is_recent()
    {
        await using var tracker = await Tracker.StartAsync();

        var first = await tracker.Sync.RunNowAsync(tracker.Feed, Ct);
        tracker.Clock.Advance(TimeSpan.FromMinutes(30));
        var tooSoon = await tracker.Sync.RunNowAsync(tracker.Feed, Ct);
        tracker.Clock.Advance(TimeSpan.FromMinutes(91));
        var later = await tracker.Sync.RunNowAsync(tracker.Feed, Ct);

        Assert.NotNull(first.Run);
        Assert.Null(tooSoon.Run);
        Assert.Equal(first.Run.StartedAt.AddMinutes(120), tooSoon.RetryAfter);
        Assert.NotNull(later.Run);
        Assert.Equal(2, tracker.Feed.Requests.Count);
    }

    [Fact]
    public async Task The_daily_sync_runs_once_a_day_after_its_time()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-25 04:00");

        Assert.Null(await tracker.Sync.RunDailyIfDueAsync(tracker.Feed, Ct));
        tracker.Clock.Set("2026-11-25 05:20");
        Assert.NotNull(await tracker.Sync.RunDailyIfDueAsync(tracker.Feed, Ct));
        tracker.Clock.Set("2026-11-25 18:00");
        Assert.Null(await tracker.Sync.RunDailyIfDueAsync(tracker.Feed, Ct));
        tracker.Clock.Set("2026-11-26 05:20");
        Assert.NotNull(await tracker.Sync.RunDailyIfDueAsync(tracker.Feed, Ct));
    }
}
