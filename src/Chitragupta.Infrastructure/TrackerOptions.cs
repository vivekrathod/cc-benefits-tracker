namespace Chitragupta.Infrastructure;

/// <summary>The "Chitragupta" section of configuration. Every value has a working default.</summary>
public sealed class TrackerOptions
{
    /// <summary>Where the database and its backups live.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>A catalog file to use exactly as it is. With one set, the source is not followed and no corrections are applied.</summary>
    public string? CatalogPath { get; set; }

    public CatalogOptions Catalog { get; set; } = new();

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

    /// <summary>The latest copy of the catalog's source, as the refresh converted it.</summary>
    public string CatalogSourceFile => Path.Combine(DataDirectory, "catalog-source.json");
}

public sealed class CatalogOptions
{
    /// <summary>The community dataset the catalog follows.</summary>
    public string SourceUrl { get; set; } = Catalog.DatasetImporter.DatasetUrl;

    /// <summary>How many days a copy of the source is used before it is fetched again. 0 never fetches.</summary>
    public int RefreshDays { get; set; } = 7;
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

    /// <summary>Days before miles or points lapse at which a reminder goes out.</summary>
    public int[] BalanceNudgeDaysBefore { get; set; } = [90, 30];

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

    /// <summary>The addresses to write to. Blank entries, as an unset environment variable leaves, are skipped.</summary>
    public IReadOnlyList<string> Recipients => To.Where(address => !string.IsNullOrWhiteSpace(address)).ToArray();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From) && Recipients.Count > 0;
}
