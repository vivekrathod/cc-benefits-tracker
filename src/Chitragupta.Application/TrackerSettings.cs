namespace Chitragupta.Application;

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

    /// <summary>Miles lapse rarely and take planning to save, so their reminders start much earlier.</summary>
    public IReadOnlyList<int> BalanceNudgeDaysBefore { get; init; } = [90, 30];

    public DayOfWeek DigestDay { get; init; } = DayOfWeek.Sunday;

    public TimeOnly DigestAt { get; init; } = new(9, 0);

    /// <summary>A balance read by the automation is marked stale once it is this many days old.</summary>
    public int BalanceStaleAfterDays { get; init; } = 2;

    /// <summary>A check that has not worked for this many days earns one more alert.</summary>
    public int BalanceStillFailingAfterDays { get; init; } = 2;

    /// <summary>An account nothing was heard about for this long counts as not checked.</summary>
    public int BalanceOverdueAfterHours { get; init; } = 26;

    /// <summary>The time of day from which each account is checked once.</summary>
    public TimeOnly BalanceCheckAt { get; init; } = new(12, 30);

    /// <summary>
    /// The most sign-ins tried for one account in a day, the scheduled one included. Programs lock
    /// an account that signs in too often, so this stays small.
    /// </summary>
    public int BalanceSignInsPerDay { get; init; } = 2;

    /// <summary>How long after a failure that may clear by itself the one retry of the day waits.</summary>
    public int BalanceRetryAfterHours { get; init; } = 4;

    /// <summary>How long the automation may go without answering before an alert says so.</summary>
    public int BalanceCheckerDownAfterMinutes { get; init; } = 30;

    /// <summary>How long the history of attempts is kept. The newest attempt of an account is always kept.</summary>
    public int BalanceHistoryDays { get; init; } = 90;

    /// <summary>The name live transactions are stored under. CSV imports use "csv".</summary>
    public const string CsvFeedName = "csv";
}
