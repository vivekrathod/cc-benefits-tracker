namespace CardBenefits.Domain.Tests;

public class BenefitDefinitionTests
{
    private static DateOnly D(string date) => DateOnly.Parse(date);

    private static BenefitTerms Terms(string effectiveFrom, decimal dollars = 50) =>
        new(D(effectiveFrom), Money.FromDollars(dollars), Cadence.Semiannual, YearBasis.Calendar);

    private static BenefitDefinition Define(BenefitTerms[] terms, DateOnly? lastDay = null) =>
        new("amex-gold/resy-credit", "amex-gold", "Resy credit", DetectionMode.Automatic, terms, lastDay);

    [Fact]
    public void Keeps_its_terms_in_date_order()
    {
        var benefit = Define([Terms("2026-07-01", 75), Terms("2024-01-01")]);

        Assert.Equal([D("2024-01-01"), D("2026-07-01")], benefit.Terms.Select(terms => terms.EffectiveFrom));
    }

    [Fact]
    public void Lives_from_its_first_terms_until_it_is_discontinued()
    {
        Assert.Equal(DateRange.From(D("2024-01-01")), Define([Terms("2024-01-01")]).Lifetime);
        Assert.Equal(
            new DateRange(D("2024-01-01"), D("2026-06-30")),
            Define([Terms("2024-01-01")], D("2026-06-30")).Lifetime);
    }

    [Fact]
    public void Needs_at_least_one_set_of_terms()
    {
        Assert.Throws<ArgumentException>(() => Define([]));
    }

    [Fact]
    public void Two_sets_of_terms_cannot_start_on_the_same_day()
    {
        Assert.Throws<ArgumentException>(() => Define([Terms("2024-01-01"), Terms("2024-01-01", 75)]));
    }

    [Fact]
    public void An_amount_must_be_positive()
    {
        Assert.Throws<ArgumentException>(() => Define([Terms("2024-01-01", 0)]));
    }

    [Fact]
    public void Cannot_be_discontinued_before_it_started()
    {
        Assert.Throws<ArgumentException>(() => Define([Terms("2024-01-01")], D("2023-12-31")));
    }
}
