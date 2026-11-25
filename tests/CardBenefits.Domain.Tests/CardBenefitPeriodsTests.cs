namespace CardBenefits.Domain.Tests;

/// <summary>Period generation for a real card and benefit: lifetime, product changes and discontinued benefits.</summary>
public class CardBenefitPeriodsTests
{
    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static DateRange Range(string start, string end) => new(D(start), D(end));

    private static string[] Dates(IEnumerable<PeriodSpec> periods) =>
        periods.Select(period => $"{period.Range.Start:O}..{period.Range.End:O}").ToArray();

    private static Card OpenCard(string productId, string openedOn) =>
        new(Guid.NewGuid(), Guid.NewGuid(), productId, D(openedOn), Anniversary.Of(D(openedOn)));

    private static BenefitDefinition Benefit(
        string productId, Cadence cadence, YearBasis basis, string effectiveFrom = "2020-01-01", DateOnly? lastDay = null) =>
        new(
            $"{productId}/credit",
            productId,
            "Credit",
            DetectionMode.Automatic,
            [new BenefitTerms(D(effectiveFrom), Money.FromDollars(100), cadence, basis)],
            lastDay);

    [Fact]
    public void A_cards_periods_start_when_it_opens()
    {
        var card = OpenCard("amex-gold", "2026-03-10");
        var benefit = Benefit("amex-gold", Cadence.Semiannual, YearBasis.Calendar);

        var periods = PeriodGenerator.Generate(card, benefit, Range("2026-01-01", "2026-12-31"));

        Assert.Equal(["2026-03-10..2026-06-30", "2026-07-01..2026-12-31"], Dates(periods));
    }

    [Fact]
    public void A_cardmember_year_benefit_follows_the_cards_anniversary()
    {
        var card = OpenCard("chase-sapphire-reserve", "2023-08-31");
        var benefit = Benefit("chase-sapphire-reserve", Cadence.Annual, YearBasis.Cardmember);

        var periods = PeriodGenerator.Generate(card, benefit, Range("2026-01-01", "2026-12-31"));

        Assert.Equal(["2025-08-31..2026-08-30", "2026-08-31..2027-08-30"], Dates(periods));
    }

    [Fact]
    public void A_closed_card_has_no_periods_after_its_closing_date()
    {
        var card = OpenCard("amex-gold", "2022-01-01");
        card.Close(D("2026-05-20"));
        var benefit = Benefit("amex-gold", Cadence.Quarterly, YearBasis.Calendar);

        var periods = PeriodGenerator.Generate(card, benefit, Range("2026-01-01", "2026-12-31"));

        Assert.Equal(["2026-01-01..2026-03-31", "2026-04-01..2026-05-20"], Dates(periods));
    }

    [Fact]
    public void A_product_change_ends_the_old_benefits_and_starts_the_new_ones_on_the_change_date()
    {
        var card = OpenCard("chase-sapphire-preferred", "2022-05-15");
        card.ChangeProduct("chase-sapphire-reserve", D("2026-02-01"));
        var window = Range("2026-01-01", "2026-12-31");

        var oldProduct = PeriodGenerator.Generate(
            card, Benefit("chase-sapphire-preferred", Cadence.Annual, YearBasis.Cardmember), window);
        var newProduct = PeriodGenerator.Generate(
            card, Benefit("chase-sapphire-reserve", Cadence.Annual, YearBasis.Cardmember), window);

        Assert.Equal(["2025-05-15..2026-01-31"], Dates(oldProduct));
        Assert.Equal(["2026-02-01..2026-05-14", "2026-05-15..2027-05-14"], Dates(newProduct));
    }

    [Fact]
    public void A_card_that_returns_to_a_product_gets_that_products_periods_for_both_spans()
    {
        var card = OpenCard("a", "2022-01-01");
        card.ChangeProduct("b", D("2024-04-01"));
        card.ChangeProduct("a", D("2025-10-01"));

        var periods = PeriodGenerator.Generate(
            card, Benefit("a", Cadence.Annual, YearBasis.Calendar), Range("2024-01-01", "2025-12-31"));

        Assert.Equal(["2024-01-01..2024-03-31", "2025-10-01..2025-12-31"], Dates(periods));
    }

    [Fact]
    public void A_benefit_of_a_product_the_card_never_held_gives_no_periods()
    {
        var card = OpenCard("amex-gold", "2022-01-01");

        var periods = PeriodGenerator.Generate(
            card, Benefit("amex-platinum", Cadence.Monthly, YearBasis.Calendar), Range("2026-01-01", "2026-12-31"));

        Assert.Empty(periods);
    }

    [Fact]
    public void A_discontinued_benefit_ends_its_last_period_on_its_last_day()
    {
        var card = OpenCard("amex-gold", "2022-01-01");
        var benefit = Benefit("amex-gold", Cadence.Monthly, YearBasis.Calendar, lastDay: D("2026-03-15"));

        var periods = PeriodGenerator.Generate(card, benefit, Range("2026-02-01", "2026-12-31"));

        Assert.Equal(["2026-02-01..2026-02-28", "2026-03-01..2026-03-15"], Dates(periods));
    }
}
