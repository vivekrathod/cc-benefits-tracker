namespace CardBenefits.Domain.Tests;

public class BenefitPeriodTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 6, 30);

    private static BenefitPeriod Period(decimal dollars = 50) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "amex-gold/resy-credit", new DateRange(Start, End), Money.FromDollars(dollars));

    [Fact]
    public void Starts_unused()
    {
        var period = Period();

        Assert.Equal(Money.Zero, period.Used);
        Assert.Equal(Money.FromDollars(50), period.Remaining);
        Assert.Empty(period.Usage);
    }

    [Fact]
    public void Used_is_the_sum_of_its_usage_entries()
    {
        var period = Period();

        period.Use(Money.FromDollars(20), UsageSource.Automatic, Guid.NewGuid());
        period.Use(Money.FromDollars(12.50m), UsageSource.Manual, note: "used in the app");

        Assert.Equal(Money.FromDollars(32.50m), period.Used);
        Assert.Equal(Money.FromDollars(17.50m), period.Remaining);
        Assert.Equal(2, period.Usage.Count);
    }

    [Fact]
    public void Cannot_use_more_than_remains()
    {
        var period = Period();
        period.Use(Money.FromDollars(40), UsageSource.Automatic, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => period.Use(Money.FromDollars(10.01m), UsageSource.Manual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Usage_must_be_a_positive_amount(decimal dollars)
    {
        Assert.Throws<ArgumentException>(() => Period().Use(Money.FromDollars(dollars), UsageSource.Manual));
    }

    [Fact]
    public void Automatic_usage_names_its_transaction()
    {
        Assert.Throws<ArgumentException>(() => Period().Use(Money.FromDollars(5), UsageSource.Automatic));
    }

    [Fact]
    public void Clearing_automatic_usage_keeps_manual_entries()
    {
        var period = Period();
        period.Use(Money.FromDollars(20), UsageSource.Automatic, Guid.NewGuid());
        period.Use(Money.FromDollars(10), UsageSource.Manual);

        period.ClearAutomaticUsage();

        Assert.Equal(Money.FromDollars(10), period.Used);
        Assert.All(period.Usage, entry => Assert.Equal(UsageSource.Manual, entry.Source));
    }

    [Fact]
    public void Status_follows_usage_and_the_date()
    {
        var period = Period();

        Assert.Equal(PeriodStatus.Upcoming, period.StatusOn(Start.AddDays(-1)));
        Assert.Equal(PeriodStatus.Open, period.StatusOn(Start));
        Assert.Equal(PeriodStatus.Open, period.StatusOn(End));
        Assert.Equal(PeriodStatus.Expired, period.StatusOn(End.AddDays(1)));

        period.Use(Money.FromDollars(20), UsageSource.Manual);
        Assert.Equal(PeriodStatus.PartlyUsed, period.StatusOn(End));
        Assert.Equal(PeriodStatus.Expired, period.StatusOn(End.AddDays(1)));

        period.Use(Money.FromDollars(30), UsageSource.Manual);
        Assert.Equal(PeriodStatus.Used, period.StatusOn(Start));
        Assert.Equal(PeriodStatus.Used, period.StatusOn(End.AddDays(1)));
    }

    [Fact]
    public void Is_created_from_a_generated_period()
    {
        var cardId = Guid.NewGuid();
        var spec = new PeriodSpec(new DateRange(Start, End), Money.FromDollars(50));

        var period = BenefitPeriod.From(spec, cardId, "amex-gold/resy-credit");

        Assert.Equal(cardId, period.CardId);
        Assert.Equal(spec.Range, period.Range);
        Assert.Equal(spec.Amount, period.Amount);
        Assert.NotEqual(Guid.Empty, period.Id);
    }

    [Fact]
    public void An_amount_cannot_be_negative()
    {
        Assert.Throws<ArgumentException>(() => Period(-1));
    }

    [Fact]
    public void Marking_used_takes_everything_that_remains()
    {
        var period = Period();
        period.Use(Money.FromDollars(20), UsageSource.Automatic, Guid.NewGuid());

        var entry = period.MarkUsed("used at the airport");

        Assert.Equal(Money.FromDollars(30), entry.Amount);
        Assert.Equal(UsageSource.Manual, entry.Source);
        Assert.Equal(PeriodStatus.Used, period.StatusOn(Start));
    }

    [Fact]
    public void A_perk_with_no_dollar_value_is_open_until_it_is_marked_used()
    {
        var perk = Period(0);

        Assert.Equal(PeriodStatus.Open, perk.StatusOn(Start));
        Assert.Equal(PeriodStatus.Expired, perk.StatusOn(End.AddDays(1)));

        perk.MarkUsed();

        Assert.Equal(PeriodStatus.Used, perk.StatusOn(Start));
        Assert.Equal(Money.Zero, perk.Used);
    }

    [Fact]
    public void Marking_used_twice_is_refused()
    {
        var period = Period();
        period.MarkUsed();

        Assert.Throws<InvalidOperationException>(() => period.MarkUsed());
    }

    [Fact]
    public void Clearing_manual_usage_reopens_the_period()
    {
        var period = Period();
        period.Use(Money.FromDollars(20), UsageSource.Automatic, Guid.NewGuid());
        period.MarkUsed();

        period.ClearManualUsage();

        Assert.Equal(Money.FromDollars(20), period.Used);
    }

    [Fact]
    public void Is_restored_with_its_usage()
    {
        var original = Period();
        original.Use(Money.FromDollars(20), UsageSource.Manual, note: "n");

        var restored = BenefitPeriod.Restore(
            original.Id, original.CardId, original.BenefitId, original.Range, original.Amount, original.Usage);

        Assert.Equal(original.Used, restored.Used);
        Assert.Equal(original.Usage, restored.Usage);
    }
}
