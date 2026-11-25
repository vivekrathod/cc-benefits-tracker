namespace CardBenefits.Infrastructure;

/// <summary>The "Tracker" section of configuration. Every value has a working default.</summary>
public sealed class TrackerOptions
{
    /// <summary>Where the database and its backups live.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>A catalog file to use instead of the built-in one.</summary>
    public string? CatalogPath { get; set; }

    /// <summary>The household's time zone: periods, "today" and the schedule all follow it.</summary>
    public string TimeZone { get; set; } = "America/New_York";

    public DateOnly? TrackingStart { get; set; }

    public int HorizonDays { get; set; } = 60;

    public int ExpiringSoonDays { get; set; } = 14;

    public int BackupsToKeep { get; set; } = 7;

    public SimpleFinOptions SimpleFin { get; set; } = new();

    public ReminderOptions Reminders { get; set; } = new();

    public SmtpOptions Smtp { get; set; } = new();

    public string DatabasePath => Path.Combine(DataDirectory, "tracker.db");
}

public sealed class SimpleFinOptions
{
    /// <summary>The file holding the access URL. The URL itself never goes into configuration.</summary>
    public string? AccessUrlFile { get; set; }

    public TimeOnly DailyAt { get; set; } = new(5, 17);

    public int WindowDays { get; set; } = 14;

    public int FirstWindowDays { get; set; } = 45;

    public int MinutesBetweenSyncs { get; set; } = 120;
}

public sealed class ReminderOptions
{
    public int[] NudgeDaysBefore { get; set; } = [7, 2];

    public DayOfWeek DigestDay { get; set; } = DayOfWeek.Sunday;

    public TimeOnly DigestAt { get; set; } = new(9, 0);
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool UseTls { get; set; } = true;

    public string? User { get; set; }

    /// <summary>A file holding the SMTP password, mounted as a secret.</summary>
    public string? PasswordFile { get; set; }

    public string From { get; set; } = "";

    public string[] To { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From) && To.Length > 0;
}
