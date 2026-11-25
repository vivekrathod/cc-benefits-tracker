using CardBenefits.Application.Tests.Support;
using CardBenefits.Contracts;
using CardBenefits.Domain;
using static CardBenefits.Application.Tests.Support.FakeFeed;

namespace CardBenefits.Application.Tests;

public class MatchingFlowTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task Clearing_matched_usage_and_replaying_gives_the_same_result()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions:
        [
            Posted("t1", "2026-10-03", 10, "AMEX DINING CREDIT"),
            Posted("t2", "2026-11-02", 6, "AMEX DINING CREDIT"),
            Posted("t3", "2026-11-12", 8, "AMEX DINING CREDIT"),
            Posted("t4", "2026-09-15", 50, "RESY CREDIT"),
            Posted("t5", "2026-11-18", 31.07m, "AMAZON REFUND"),
            Posted("t6", "2026-11-19", 1200, "PAYMENT THANK YOU"),
        ]);
        var before = await SnapshotAsync(tracker);

        var periods = await tracker.Store.GetPeriodsAsync(null, Ct);
        foreach (var period in periods)
        {
            period.ClearAutomaticUsage();
        }

        await tracker.Store.SavePeriodsAsync(periods, Ct);
        Assert.NotEqual(before, await SnapshotAsync(tracker));

        await tracker.Rematcher.RematchAllAsync(Ct);
        Assert.Equal(before, await SnapshotAsync(tracker));

        await tracker.Rematcher.RematchAllAsync(Ct);
        Assert.Equal(before, await SnapshotAsync(tracker));
    }

    [Fact]
    public async Task A_credit_posting_just_after_the_period_ends_counts_for_that_period()
    {
        await using var tracker = await Tracker.StartAsync();

        await tracker.CardAsync(transactions: Posted("t1", "2026-11-03", 10, "AMEX DINING CREDIT"));

        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-10-15")).Used);
        Assert.Equal(Money.Zero, (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-15")).Used);
    }

    [Fact]
    public async Task A_payment_is_ignored_and_an_unknown_credit_goes_to_review()
    {
        await using var tracker = await Tracker.StartAsync();

        await tracker.CardAsync(transactions:
        [
            Posted("t1", "2026-11-19", 1200, "PAYMENT THANK YOU - WEB"),
            Posted("t2", "2026-11-18", 31.07m, "AMAZON REFUND"),
            Posted("t3", "2026-11-17", -54.20m, "RESTAURANT"),
        ]);

        var item = Assert.Single(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal("Unmatched", item.Reason);
        Assert.Equal("AMAZON REFUND", item.Description);
        Assert.Equal(31.07m, item.Amount);
        Assert.Equal("Amex Gold", item.CardName);
        Assert.Contains(item.Benefits, choice => choice.Id == TestCatalog.Dining);
    }

    [Fact]
    public async Task Assigning_a_credit_with_a_keyword_teaches_a_rule_for_the_next_one()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-10-09", 10, "GRUBHUB STATEMENT CR"));
        var item = Assert.Single(await tracker.Review.GetQueueAsync(Ct));

        await tracker.Review.AssignAsync(item.Id, TestCatalog.Dining, "GRUBHUB STATEMENT CR", Ct);

        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-10-09")).Used);

        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex",
            Posted("t1", "2026-10-09", 10, "GRUBHUB STATEMENT CR"), Posted("t2", "2026-11-09", 10, "Grubhub Statement Cr"));
        await tracker.SyncAsync();

        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09")).Used);
    }

    [Fact]
    public async Task Dismissing_a_credit_with_a_keyword_ignores_similar_ones_from_then_on()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions:
        [
            Posted("t1", "2026-11-05", 12, "AMAZON REFUND"), Posted("t2", "2026-11-09", 30, "AMAZON REFUND"),
        ]);
        var queue = await tracker.Review.GetQueueAsync(Ct);
        Assert.Equal(2, queue.Count);

        await tracker.Review.DismissAsync(queue[0].Id, "AMAZON REFUND", Ct);

        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal(["AMAZON REFUND"], await tracker.Store.GetLearnedIgnoreKeywordsAsync(Ct));
    }

    [Fact]
    public async Task A_credit_above_what_is_left_is_capped_and_the_excess_is_flagged_until_acknowledged()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions:
        [
            Posted("t1", "2026-11-07", 8, "AMEX DINING CREDIT"), Posted("t2", "2026-11-09", 8, "AMEX DINING CREDIT"),
        ]);

        var november = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09");
        Assert.Equal(Money.FromDollars(10), november.Used);
        var excess = Assert.Single(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal("Excess", excess.Reason);
        Assert.Equal(6m, excess.Amount);
        Assert.Equal(8m, excess.TransactionAmount);

        await tracker.Review.AcknowledgeAsync(excess.Id, Ct);
        await tracker.Rematcher.RematchAllAsync(Ct);

        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09")).Used);
    }

    [Fact]
    public async Task An_unverified_rule_only_suggests_until_a_person_confirms_it()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-09-14", 100, "CHASE TRAVEL HOTEL CR"));

        var suggestion = Assert.Single(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal("Suggested", suggestion.Reason);
        Assert.Equal(TestCatalog.Hotel, suggestion.BenefitId);
        Assert.Equal("Hotel credit", suggestion.BenefitName);
        Assert.Equal(Money.Zero, (await tracker.PeriodAsync(TestCatalog.Hotel, "2026-09-14")).Used);

        await tracker.Review.AssignAsync(suggestion.Id, TestCatalog.Hotel, null, Ct);

        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Equal(Money.FromDollars(100), (await tracker.PeriodAsync(TestCatalog.Hotel, "2026-09-14")).Used);
    }

    [Fact]
    public async Task One_tap_marks_a_manual_benefit_used_and_undo_takes_it_back()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync();
        var uber = await tracker.PeriodAsync(TestCatalog.Uber, "2026-11-25");
        var lounge = await tracker.PeriodAsync(TestCatalog.Lounge, "2026-11-25");

        await tracker.Usage.MarkUsedAsync(uber.Id, null, "Ride to the airport", Ct);
        await tracker.Usage.MarkUsedAsync(lounge.Id, null, null, Ct);

        uber = await tracker.PeriodAsync(TestCatalog.Uber, "2026-11-25");
        Assert.True(uber.IsUsed);
        Assert.Equal("Ride to the airport", Assert.Single(uber.Usage).Note);
        Assert.True((await tracker.PeriodAsync(TestCatalog.Lounge, "2026-11-25")).IsUsed);

        await tracker.Usage.UndoManualAsync(uber.Id, Ct);
        Assert.False((await tracker.PeriodAsync(TestCatalog.Uber, "2026-11-25")).IsUsed);
        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Usage.MarkUsedAsync(lounge.Id, null, null, Ct));
    }

    [Fact]
    public async Task Manual_usage_survives_a_replay_and_counts_against_the_cap()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-09", 8, "AMEX DINING CREDIT"));
        var november = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09");

        // The person knows about a use that never showed up as a credit.
        await tracker.Usage.UndoManualAsync(november.Id, Ct);
        await tracker.Usage.MarkUsedAsync(november.Id, 2m, "Counted by hand", Ct);
        await tracker.Rematcher.RematchAllAsync(Ct);

        november = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09");
        Assert.Equal(Money.FromDollars(10), november.Used);
        Assert.Equal([UsageSource.Manual, UsageSource.Automatic], november.Usage.Select(entry => entry.Source));
        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));

        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Usage.MarkUsedAsync(november.Id, 1m, null, Ct));
    }

    [Fact]
    public async Task A_csv_backfill_and_the_live_feed_overlap_without_counting_twice()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync(transactions: Posted("t1", "2026-11-09", 10, "AMEX DINING CREDIT"));

        // The export reaches back further than the feed and repeats the feed's November credit.
        const string export = """
            Date,Description,Amount
            11/09/2026,AMEX Dining Credit,-10.00
            09/08/2026,AMEX Dining Credit,-10.00
            08/11/2026,AMEX Dining Credit,-10.00
            08/20/2026,RESTAURANT,54.20
            """;
        var run = await tracker.Statements.ImportAsync(card.Id, export, Ct);
        var again = await tracker.Statements.ImportAsync(card.Id, export, Ct);

        Assert.Equal(4, run.TransactionsChanged);
        Assert.Equal(0, again.TransactionsChanged);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-08-11")).Used);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-09-08")).Used);
        Assert.Equal(Money.FromDollars(10), (await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-09")).Used);
        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Empty(await tracker.Cards.GetNewAccountsAsync(Ct));
    }

    private static async Task<string> SnapshotAsync(Tracker tracker)
    {
        var periods = (await tracker.Store.GetPeriodsAsync(null, Ct))
            .OrderBy(period => period.BenefitId, StringComparer.Ordinal)
            .ThenBy(period => period.Range.Start)
            .Select(period => $"{period.BenefitId} {period.Range} {period.Used}");
        var review = (await tracker.Review.GetQueueAsync(Ct))
            .Select(item => $"{item.Reason} {item.Description} {item.Amount}")
            .Order(StringComparer.Ordinal);
        return string.Join('\n', periods.Concat(review));
    }
}
