namespace Chitragupta.Domain.Tests;

public class ProgramBalanceTests
{
    private static readonly Guid Alex = Guid.NewGuid();
    private static readonly DateTimeOffset Noon = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Keeps_what_it_was_given_with_the_program_name_trimmed()
    {
        var balance = new ProgramBalance(Alex, "  American AAdvantage ", 45_210, Noon, BalanceSource.Automation, DateOnly.Parse("2028-03-14"));

        Assert.Equal(
            (Alex, "American AAdvantage", 45_210L, Noon, BalanceSource.Automation, (DateOnly?)DateOnly.Parse("2028-03-14")),
            (balance.PersonId, balance.Program, balance.Amount, balance.AsOf, balance.Source, balance.ExpiresOn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Needs_a_program(string program)
    {
        Assert.Throws<ArgumentException>(() => new ProgramBalance(Alex, program, 1, Noon, BalanceSource.Manual));
    }

    [Fact]
    public void Cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgramBalance(Alex, "Avios", -1, Noon, BalanceSource.Manual));
    }

    [Theory]
    [InlineData("2028-03-14", 0)]
    [InlineData("2028-03-01", 13)]
    [InlineData("2028-03-20", -6)]
    public void Counts_the_days_until_the_miles_lapse(string today, int expected)
    {
        var balance = new ProgramBalance(Alex, "Avios", 500, Noon, BalanceSource.Automation, DateOnly.Parse("2028-03-14"));

        Assert.Equal(expected, balance.DaysUntilExpiry(DateOnly.Parse(today)));
    }

    [Fact]
    public void Has_nothing_to_lose_without_a_date_or_without_miles()
    {
        var today = DateOnly.Parse("2028-03-01");

        Assert.Null(new ProgramBalance(Alex, "Avios", 500, Noon, BalanceSource.Manual).DaysUntilExpiry(today));
        Assert.Null(new ProgramBalance(Alex, "Avios", 0, Noon, BalanceSource.Manual, DateOnly.Parse("2028-03-14")).DaysUntilExpiry(today));
    }

    [Fact]
    public void Is_the_same_account_for_the_same_person_and_program_whatever_the_spelling()
    {
        var balance = new ProgramBalance(Alex, "Avios", 500, Noon, BalanceSource.Manual);

        Assert.True(balance.IsFor(Alex, "avios "));
        Assert.False(balance.IsFor(Guid.NewGuid(), "Avios"));
        Assert.False(balance.IsFor(Alex, "Flying Blue"));
    }
}
