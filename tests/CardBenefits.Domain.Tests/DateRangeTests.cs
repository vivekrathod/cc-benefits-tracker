namespace CardBenefits.Domain.Tests;

public class DateRangeTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void End_may_not_precede_start()
    {
        Assert.Throws<ArgumentException>(() => new DateRange(D(2026, 2, 1), D(2026, 1, 31)));
    }

    [Fact]
    public void A_single_day_is_a_valid_range()
    {
        Assert.Equal(1, new DateRange(D(2026, 1, 1), D(2026, 1, 1)).Days);
    }

    [Fact]
    public void Contains_both_ends()
    {
        var january = new DateRange(D(2026, 1, 1), D(2026, 1, 31));

        Assert.True(january.Contains(D(2026, 1, 1)));
        Assert.True(january.Contains(D(2026, 1, 31)));
        Assert.False(january.Contains(D(2025, 12, 31)));
        Assert.False(january.Contains(D(2026, 2, 1)));
    }

    [Fact]
    public void Overlaps_when_a_single_day_is_shared()
    {
        var january = new DateRange(D(2026, 1, 1), D(2026, 1, 31));

        Assert.True(january.Overlaps(new DateRange(D(2026, 1, 31), D(2026, 2, 10))));
        Assert.False(january.Overlaps(new DateRange(D(2026, 2, 1), D(2026, 2, 10))));
    }

    [Fact]
    public void Intersect_returns_the_shared_days_or_nothing()
    {
        var january = new DateRange(D(2026, 1, 1), D(2026, 1, 31));

        Assert.Equal(
            new DateRange(D(2026, 1, 20), D(2026, 1, 31)),
            january.Intersect(new DateRange(D(2026, 1, 20), D(2026, 3, 1))));
        Assert.Null(january.Intersect(new DateRange(D(2026, 2, 1), D(2026, 3, 1))));
    }

    [Fact]
    public void From_is_open_ended()
    {
        var range = DateRange.From(D(2026, 1, 1));

        Assert.True(range.Contains(D(2999, 1, 1)));
        Assert.True(range.IsOpenEnded);
    }
}
