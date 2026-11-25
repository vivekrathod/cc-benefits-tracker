namespace CardBenefits.Application;

/// <summary>The few knobs the use cases have. The host fills them from configuration.</summary>
public sealed record TrackerSettings
{
    /// <summary>Nothing before this day is tracked. Left empty, the tracker looks back one year.</summary>
    public DateOnly? TrackingStart { get; init; }

    /// <summary>How far ahead periods are generated.</summary>
    public int HorizonDays { get; init; } = 60;

    /// <summary>Each daily sync asks for this many days, so late and corrected postings are caught.</summary>
    public int SyncWindowDays { get; init; } = 14;

    /// <summary>The first sync, or one after a long gap, asks for more. The Bridge recommends at most 45.</summary>
    public int FirstSyncWindowDays { get; init; } = 45;

    /// <summary>The Bridge disables a token after about 24 requests a day, so "sync now" is rationed.</summary>
    public int MinutesBetweenSyncs { get; init; } = 120;

    public TimeOnly DailySyncAt { get; init; } = new(5, 17);

    public int ExpiringSoonDays { get; init; } = 14;

    public IReadOnlyList<int> NudgeDaysBefore { get; init; } = [7, 2];

    public DayOfWeek DigestDay { get; init; } = DayOfWeek.Sunday;

    public TimeOnly DigestAt { get; init; } = new(9, 0);

    /// <summary>The name live transactions are stored under. CSV imports use "csv".</summary>
    public const string CsvFeedName = "csv";
}
