namespace CardBenefits.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("12.34", 1234)]
    [InlineData("300", 30000)]
    [InlineData("0.05", 5)]
    [InlineData("-7.50", -750)]
    public void FromDollars_keeps_whole_cents(string dollars, long expectedCents)
    {
        Assert.Equal(expectedCents, Money.FromDollars(decimal.Parse(dollars)).Cents);
    }

    [Fact]
    public void FromDollars_rejects_fractions_of_a_cent()
    {
        Assert.Throws<ArgumentException>(() => Money.FromDollars(1.005m));
    }

    [Fact]
    public void Adds_and_subtracts()
    {
        var ten = Money.FromDollars(10);
        var four = Money.FromDollars(4);

        Assert.Equal(Money.FromDollars(14), ten + four);
        Assert.Equal(Money.FromDollars(6), ten - four);
    }

    [Fact]
    public void Compares_by_amount()
    {
        Assert.True(Money.FromDollars(5) < Money.FromDollars(6));
        Assert.True(Money.FromDollars(6) >= Money.FromDollars(6));
        Assert.Equal(Money.FromDollars(5), Money.Min(Money.FromDollars(5), Money.FromDollars(6)));
    }

    [Fact]
    public void Sums_a_sequence()
    {
        Money[] amounts = [Money.FromDollars(1.25m), Money.FromDollars(2.50m)];

        Assert.Equal(Money.FromDollars(3.75m), Money.Sum(amounts));
        Assert.Equal(Money.Zero, Money.Sum([]));
    }

    [Theory]
    [InlineData(1234, "$12.34")]
    [InlineData(5, "$0.05")]
    [InlineData(-750, "-$7.50")]
    public void Formats_as_dollars(long cents, string expected)
    {
        Assert.Equal(expected, new Money(cents).ToString());
    }
}
