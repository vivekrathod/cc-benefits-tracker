using CardBenefits.Application.Tests.Support;
using CardBenefits.Contracts;
using static CardBenefits.Application.Tests.Support.FakeFeed;

namespace CardBenefits.Application.Tests;

public class ViewTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task Expiring_soon_lists_unused_and_partly_used_periods_of_both_people_by_end_date()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-25 10:00");
        await tracker.CardAsync(person: "Alex", transactions: Posted("t1", "2026-11-09", 4, "AMEX DINING CREDIT"));
        await tracker.CardAsync(
            person: "Sam", accountId: "acct-2", accountName: "Gold Card (5678)", anniversary: "12-03", openedOn: "2025-12-03");
        var sam = (await tracker.Queries.GetCardsAsync(Ct)).Single(card => card.PersonName == "Sam");
        var samsUber = (await tracker.Store.GetPeriodsAsync(sam.Id, Ct))
            .Single(period => period.BenefitId == TestCatalog.Uber && period.Range.Contains(tracker.Clock.Today));
        await tracker.Usage.MarkUsedAsync(samsUber.Id, null, null, Ct);

        var expiring = await tracker.Queries.GetExpiringSoonAsync(14, Ct);

        // November's monthly credits end in 5 days; Sam's cardmember year ends Dec 2, in 7.
        Assert.Equal(
            [
                ("Alex", "Dining credit", 6m, 5), ("Alex", "Uber Cash", 10m, 5), ("Sam", "Dining credit", 10m, 5),
                ("Sam", "Hotel credit", 100m, 7), ("Sam", "Lounge passes", 0m, 7),
            ],
            expiring
                .OrderBy(period => period.DaysLeft).ThenBy(period => period.PersonName).ThenBy(period => period.BenefitName)
                .Select(period => (period.PersonName, period.BenefitName, period.Remaining, period.DaysLeft)));
        Assert.Equal(expiring.Select(period => period.End).Order(), expiring.Select(period => period.End));
        Assert.Equal("PartlyUsed", expiring.First(period => period.Remaining == 6m).Status);
        Assert.True(expiring.Single(period => period.BenefitName == "Lounge passes").Manual);
    }

    [Fact]
    public async Task This_period_shows_every_benefit_of_every_open_card_with_used_and_remaining()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-09", 4, "AMEX DINING CREDIT"));

        var group = Assert.Single(await tracker.Queries.GetThisPeriodAsync(Ct));

        Assert.Equal("Amex Gold", group.Card.ProductName);
        Assert.Equal("Membership Rewards", group.Card.PointsProgram);
        Assert.Equal(
            ["Dining credit", "Uber Cash", "Resy credit", "Hotel credit", "Lounge passes"],
            group.Periods.Select(period => period.BenefitName));
        var dining = group.Periods[0];
        Assert.Equal((10m, 4m, 6m), (dining.Amount, dining.Used, dining.Remaining));
    }

    [Fact]
    public async Task Card_value_compares_what_was_used_this_cardmember_year_with_the_fee()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(anniversary: "05-06", transactions:
        [
            Posted("t0", "2026-04-10", 10, "AMEX DINING CREDIT"),   // before this cardmember year
            Posted("t1", "2026-06-09", 10, "AMEX DINING CREDIT"),
            Posted("t2", "2026-07-09", 10, "AMEX DINING CREDIT"),
            Posted("t3", "2026-09-15", 50, "RESY CREDIT"),
        ]);

        var value = Assert.Single(await tracker.Queries.GetCardValueAsync(Ct));

        Assert.Equal((DateOnly.Parse("2026-05-06"), DateOnly.Parse("2027-05-05")), (value.YearStart, value.YearEnd));
        Assert.Equal(70m, value.Used);
        Assert.Equal(325m, value.AnnualFee);
        Assert.Equal(-255m, value.Net);
        Assert.Equal(DateOnly.Parse("2027-05-06"), value.Card.NextFeeDate);
    }

    [Fact]
    public async Task The_summary_counts_what_needs_attention()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.CardAsync(transactions: Posted("t1", "2026-11-18", 31.07m, "AMAZON REFUND"));
        tracker.Feed.Account("chk", "Everyday Checking", "Chase");
        tracker.Feed.Errors.Add(new Feeds.FeedError("con.auth", "Sign in again.", "Citi"));
        tracker.Clock.Advance(TimeSpan.FromHours(1));
        await tracker.SyncAsync();

        var summary = await tracker.Queries.GetSummaryAsync("simplefin", Ct);

        Assert.Equal(new SummaryDto(ExpiringSoon: 2, ReviewItems: 1, NewAccounts: 1, SyncProblems: 1), summary);
    }

    [Fact]
    public async Task An_institution_that_recovers_stops_showing_its_error()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed.Account("acct-1", "Gold Card (1234)", "Amex");
        tracker.Feed.Errors.Add(new Feeds.FeedError("con.auth", "Sign in again.", "Amex"));
        await tracker.SyncAsync();
        Assert.Equal("Sign in again.", (await tracker.Queries.GetSyncHealthAsync("simplefin", null, Ct)).Connections.Single().LastError);

        tracker.Feed.Errors.Clear();
        tracker.Clock.Advance(TimeSpan.FromDays(1));
        await tracker.SyncAsync();

        var amex = (await tracker.Queries.GetSyncHealthAsync("simplefin", null, Ct)).Connections.Single();
        Assert.Null(amex.LastError);
        Assert.Equal(tracker.Clock.Now, amex.LastOkAt);
    }
}
