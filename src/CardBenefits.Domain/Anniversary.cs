namespace CardBenefits.Domain;

/// <summary>
/// The month and day a cardmember year starts on. Dates derived from it clamp to the
/// last day of a shorter month and return to the anniversary day when the month allows,
/// because every date is computed from the anniversary and never from the previous date.
/// </summary>
public readonly record struct Anniversary
{
    private const int LeapYear = 2024;

    public Anniversary(int month, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, DateTime.DaysInMonth(LeapYear, month));

        Month = month;
        Day = day;
    }

    public int Month { get; }

    public int Day { get; }

    public static Anniversary Of(DateOnly date) => new(date.Month, date.Day);

    /// <summary>The anniversary date in the given year.</summary>
    public DateOnly In(int year) => PlusMonths(year, 0);

    /// <summary>The date a number of months after the anniversary in the given year.</summary>
    public DateOnly PlusMonths(int year, int months)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(months);

        var monthIndex = Month - 1 + months;
        var targetYear = year + Math.DivRem(monthIndex, 12, out var zeroBasedMonth);
        var targetMonth = zeroBasedMonth + 1;

        return new DateOnly(targetYear, targetMonth, Math.Min(Day, DateTime.DaysInMonth(targetYear, targetMonth)));
    }

    public override string ToString() => $"{Month:00}-{Day:00}";
}
