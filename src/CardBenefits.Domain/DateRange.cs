namespace CardBenefits.Domain;

/// <summary>A span of whole days. Both ends are included.</summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException($"A range cannot end ({end:O}) before it starts ({start:O}).", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool IsOpenEnded => End == DateOnly.MaxValue;

    /// <summary>A range with no known end, such as an open card.</summary>
    public static DateRange From(DateOnly start) => new(start, DateOnly.MaxValue);

    public bool Contains(DateOnly date) => Start <= date && date <= End;

    public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;

    public DateRange? Intersect(DateRange other)
    {
        if (!Overlaps(other))
        {
            return null;
        }

        return new DateRange(Start > other.Start ? Start : other.Start, End < other.End ? End : other.End);
    }

    public override string ToString() => IsOpenEnded ? $"{Start:O}.." : $"{Start:O}..{End:O}";
}
