namespace Chitragupta.Domain.Tests;

public class CardTests
{
    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static Card OpenCard(string productId = "amex-gold", string openedOn = "2022-05-15") =>
        new(Guid.NewGuid(), Guid.NewGuid(), productId, D(openedOn), Anniversary.Of(D(openedOn)));

    [Fact]
    public void A_new_card_holds_its_product_from_the_opening_date()
    {
        var card = OpenCard();

        Assert.Equal("amex-gold", card.CurrentProductId);
        Assert.Equal([DateRange.From(D("2022-05-15"))], card.DaysOn("amex-gold"));
        Assert.False(card.IsClosed);
    }

    [Fact]
    public void Closing_ends_the_product_on_the_closing_date()
    {
        var card = OpenCard();

        card.Close(D("2026-03-10"));

        Assert.True(card.IsClosed);
        Assert.Equal([new DateRange(D("2022-05-15"), D("2026-03-10"))], card.DaysOn("amex-gold"));
    }

    [Fact]
    public void A_product_change_ends_the_old_product_the_day_before_and_keeps_the_anniversary()
    {
        var card = OpenCard("chase-sapphire-preferred");

        card.ChangeProduct("chase-sapphire-reserve", D("2026-02-01"));

        Assert.Equal("chase-sapphire-reserve", card.CurrentProductId);
        Assert.Equal([new DateRange(D("2022-05-15"), D("2026-01-31"))], card.DaysOn("chase-sapphire-preferred"));
        Assert.Equal([DateRange.From(D("2026-02-01"))], card.DaysOn("chase-sapphire-reserve"));
        Assert.Equal(new Anniversary(5, 15), card.Anniversary);
    }

    [Fact]
    public void Changing_back_gives_the_first_product_two_separate_spans()
    {
        var card = OpenCard("a");
        card.ChangeProduct("b", D("2024-01-01"));
        card.ChangeProduct("a", D("2025-01-01"));

        Assert.Equal(
            [new DateRange(D("2022-05-15"), D("2023-12-31")), DateRange.From(D("2025-01-01"))],
            card.DaysOn("a"));
    }

    [Fact]
    public void A_product_the_card_never_held_has_no_days()
    {
        Assert.Empty(OpenCard().DaysOn("something-else"));
    }

    [Fact]
    public void A_product_change_must_come_after_the_previous_one_and_name_a_different_product()
    {
        var card = OpenCard("a");
        card.ChangeProduct("b", D("2024-01-01"));

        Assert.Throws<InvalidOperationException>(() => card.ChangeProduct("b", D("2025-01-01")));
        Assert.Throws<InvalidOperationException>(() => card.ChangeProduct("c", D("2024-01-01")));
        Assert.Throws<InvalidOperationException>(() => card.ChangeProduct("c", D("2023-06-01")));
    }

    [Fact]
    public void A_closed_card_cannot_change_product_or_close_again()
    {
        var card = OpenCard();
        card.Close(D("2026-03-10"));

        Assert.Throws<InvalidOperationException>(() => card.ChangeProduct("b", D("2026-04-01")));
        Assert.Throws<InvalidOperationException>(() => card.Close(D("2026-04-01")));
    }

    [Fact]
    public void A_card_cannot_close_before_it_opened_or_before_its_latest_product_change()
    {
        var card = OpenCard("a");
        card.ChangeProduct("b", D("2024-01-01"));

        Assert.Throws<InvalidOperationException>(() => card.Close(D("2023-12-31")));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("123")]
    public void Last_four_must_be_four_digits(string lastFour)
    {
        Assert.Throws<ArgumentException>(
            () => new Card(Guid.NewGuid(), Guid.NewGuid(), "a", D("2022-05-15"), new Anniversary(5, 15), lastFour));
    }
}
