namespace Chitragupta.Domain.Tests;

public class AnniversaryTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(2, 30)]
    [InlineData(4, 31)]
    [InlineData(1, 0)]
    public void Rejects_dates_that_exist_in_no_year(int month, int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anniversary(month, day));
    }

    [Fact]
    public void Is_taken_from_a_date()
    {
        Assert.Equal(new Anniversary(5, 15), Anniversary.Of(new DateOnly(2021, 5, 15)));
    }

    [Fact]
    public void Leap_day_falls_on_28_February_in_other_years()
    {
        var leapDay = new Anniversary(2, 29);

        Assert.Equal(new DateOnly(2028, 2, 29), leapDay.In(2028));
        Assert.Equal(new DateOnly(2027, 2, 28), leapDay.In(2027));
    }

    [Theory]
    [InlineData(1, 31, 2026, 0, "2026-01-31")]
    [InlineData(1, 31, 2026, 1, "2026-02-28")]
    [InlineData(1, 31, 2028, 1, "2028-02-29")]
    [InlineData(1, 31, 2026, 2, "2026-03-31")]
    [InlineData(1, 31, 2026, 3, "2026-04-30")]
    [InlineData(11, 30, 2026, 3, "2027-02-28")]
    [InlineData(5, 15, 2026, 12, "2027-05-15")]
    public void Month_offsets_clamp_to_the_last_day_and_recover_afterwards(
        int month, int day, int year, int monthsLater, string expected)
    {
        var anniversary = new Anniversary(month, day);

        Assert.Equal(DateOnly.Parse(expected), anniversary.PlusMonths(year, monthsLater));
    }
}
