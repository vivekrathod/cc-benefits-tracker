using CardBenefits.Application.Tests.Support;
using CardBenefits.Contracts;
using CardBenefits.Domain;
using static CardBenefits.Application.Tests.Support.FakeFeed;

namespace CardBenefits.Application.Tests;

public class CardTests
{
    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task The_queue_suggests_the_product_and_the_anniversary_from_the_annual_fee()
    {
        await using var tracker = await Tracker.StartAsync();
        tracker.Feed
            .Account("a1", "Chase Sapphire Preferred (4321)", "Chase", Posted("fee", "2026-10-01", -95, "ANNUAL MEMBERSHIP FEE"))
            .Account("a2", "Everyday Checking (9999)", "Chase")
            .Account("a3", "Sapphire Reserve Visa Infinite (1111)", "Chase");
        await tracker.SyncAsync();

        var queue = await tracker.Cards.GetNewAccountsAsync(Ct);

        var preferred = Assert.Single(queue, account => account.LastFour == "4321");
        Assert.Equal(TestCatalog.Preferred, preferred.SuggestedProductId);
        Assert.Equal((10, 1), (preferred.InferredAnniversaryMonth, preferred.InferredAnniversaryDay));
        Assert.True(preferred.LooksLikeCard);

        var checking = Assert.Single(queue, account => account.LastFour == "9999");
        Assert.Null(checking.SuggestedProductId);
        Assert.False(checking.LooksLikeCard);
        Assert.Equal(checking, queue[^1]);

        Assert.Equal(TestCatalog.Reserve, Assert.Single(queue, account => account.LastFour == "1111").SuggestedProductId);
    }

    [Fact]
    public async Task A_seeded_card_without_a_feed_is_offered_to_the_matching_new_account()
    {
        await using var tracker = await Tracker.StartAsync();
        var alex = await tracker.PersonAsync("Alex");
        var sam = await tracker.PersonAsync("Sam");
        var alexCard = await tracker.Cards.AddCardAsync(
            new AddCardRequest(alex.Id, TestCatalog.Preferred, DateOnly.Parse("2011-10-25"), null, null, null, null), Ct);
        var samCard = await tracker.Cards.AddCardAsync(
            new AddCardRequest(sam.Id, TestCatalog.Preferred, DateOnly.Parse("2025-05-06"), null, null, null, null), Ct);

        tracker.Feed.Account("a1", "Chase Sapphire Preferred (4321)", "Chase");
        await tracker.SyncAsync();

        // Two people hold the same product, so the service offers both and a person picks.
        var waiting = Assert.Single(await tracker.Cards.GetNewAccountsAsync(Ct));
        Assert.Equal(new[] { alexCard.Id, samCard.Id }.Order(), waiting.SuggestedCardIds.Order());

        await tracker.Cards.MapAsync(waiting.Id, samCard.Id, Ct);
        var cards = await tracker.Queries.GetCardsAsync(Ct);
        Assert.Equal(["Chase: Chase Sapphire Preferred (4321)"], cards.Single(card => card.Id == samCard.Id).LinkedAccounts);
        Assert.Empty(cards.Single(card => card.Id == alexCard.Id).LinkedAccounts);
    }

    [Fact]
    public async Task A_card_keeps_the_last_four_from_its_account_and_a_default_opening_date()
    {
        await using var tracker = await Tracker.StartAsync();

        var card = await tracker.CardAsync(openedOn: null);

        Assert.Equal("1234", card.LastFour);
        Assert.Equal(DateOnly.Parse("2024-05-06"), card.OpenedOn);
        Assert.Equal(new Anniversary(5, 6), card.Anniversary);
    }

    [Fact]
    public async Task A_card_opened_mid_period_gets_a_short_first_period()
    {
        await using var tracker = await Tracker.StartAsync();

        await tracker.CardAsync(openedOn: "2026-11-12", anniversary: "11-12");

        var first = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-20");
        Assert.Equal(new DateRange(DateOnly.Parse("2026-11-12"), DateOnly.Parse("2026-11-30")), first.Range);
        Assert.Equal(Money.FromDollars(10), first.Amount);
        Assert.DoesNotContain(await tracker.Store.GetPeriodsAsync(null, Ct), period => period.Range.End < DateOnly.Parse("2026-11-12"));
    }

    [Fact]
    public async Task Closing_a_card_cuts_its_periods_at_the_closing_date()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync();

        await tracker.Cards.CloseAsync(card.Id, DateOnly.Parse("2026-11-20"), Ct);

        var last = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-20");
        Assert.Equal(DateOnly.Parse("2026-11-20"), last.Range.End);
        Assert.DoesNotContain(await tracker.Store.GetPeriodsAsync(null, Ct), period => period.Range.Start > DateOnly.Parse("2026-11-20"));
        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Cards.CloseAsync(card.Id, DateOnly.Parse("2026-11-21"), Ct));
    }

    [Fact]
    public async Task A_product_change_keeps_the_card_and_switches_its_benefits_on_the_day()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync(
            product: TestCatalog.Reserve,
            accountName: "Sapphire Reserve (1234)",
            transactions: [Posted("t1", "2026-07-02", 300, "TRAVEL CREDIT")]);

        await tracker.Cards.ChangeProductAsync(card.Id, TestCatalog.Gold, DateOnly.Parse("2026-11-10"), Ct);

        // The old product's period ends the day before; what was used on it stays.
        var travel = await tracker.PeriodAsync(TestCatalog.Travel, "2026-07-02");
        Assert.Equal(DateOnly.Parse("2026-11-09"), travel.Range.End);
        Assert.Equal(Money.FromDollars(300), travel.Used);

        var dining = await tracker.PeriodAsync(TestCatalog.Dining, "2026-11-25");
        Assert.Equal(DateOnly.Parse("2026-11-10"), dining.Range.Start);

        var view = Assert.Single(await tracker.Queries.GetCardsAsync(Ct));
        Assert.Equal(card.Id, view.Id);
        Assert.Equal("Amex Gold", view.ProductName);
        Assert.Single(await tracker.Store.GetFeedAccountsAsync(Ct), account => account.CardId == card.Id);
    }

    [Fact]
    public async Task Correcting_the_anniversary_moves_the_cardmember_year_periods()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync(anniversary: "05-06");
        Assert.Equal(DateOnly.Parse("2026-05-06"), (await tracker.PeriodAsync(TestCatalog.Hotel, "2026-11-25")).Range.Start);

        await tracker.Cards.UpdateAsync(card.Id, new UpdateCardRequest(9, 1, 250m), Ct);

        Assert.Equal(DateOnly.Parse("2026-09-01"), (await tracker.PeriodAsync(TestCatalog.Hotel, "2026-11-25")).Range.Start);
        var view = Assert.Single(await tracker.Queries.GetCardsAsync(Ct));
        Assert.Equal(250m, view.AnnualFee);
        Assert.Equal(DateOnly.Parse("2027-09-01"), view.NextFeeDate);
    }

    [Fact]
    public async Task Removing_a_card_puts_its_account_back_in_the_queue()
    {
        await using var tracker = await Tracker.StartAsync();
        var card = await tracker.CardAsync(transactions: Posted("t1", "2026-11-18", 31.07m, "AMAZON REFUND"));
        Assert.Single(await tracker.Review.GetQueueAsync(Ct));

        await tracker.Cards.RemoveAsync(card.Id, Ct);

        Assert.Empty(await tracker.Store.GetCardsAsync(Ct));
        Assert.Empty(await tracker.Store.GetPeriodsAsync(null, Ct));
        Assert.Empty(await tracker.Review.GetQueueAsync(Ct));
        Assert.Single(await tracker.Cards.GetNewAccountsAsync(Ct));
    }

    [Fact]
    public async Task Requests_that_make_no_sense_are_rejected_with_a_message()
    {
        await using var tracker = await Tracker.StartAsync();
        var alex = await tracker.PersonAsync();

        var unknownProduct = await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Cards.AddCardAsync(
            new AddCardRequest(alex.Id, "no-such-card", DateOnly.Parse("2026-01-01"), null, null, null, null), Ct));
        Assert.Contains("no-such-card", unknownProduct.Message);

        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Cards.AddCardAsync(
            new AddCardRequest(alex.Id, TestCatalog.Gold, DateOnly.Parse("2026-01-01"), 2, 30, null, null), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => tracker.Cards.AddCardAsync(
            new AddCardRequest(Guid.NewGuid(), TestCatalog.Gold, DateOnly.Parse("2026-01-01"), null, null, null, null), Ct));
        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Cards.AddPersonAsync("  ", Ct));
        Assert.Equal(alex.Id, (await tracker.Cards.AddPersonAsync("alex", Ct)).Id);
    }
}
