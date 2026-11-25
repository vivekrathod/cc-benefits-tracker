using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure;

/// <summary>One dated copy of the database a day, keeping the most recent few.</summary>
public sealed class DatabaseBackup(ITrackerStore store, IClock clock, TrackerOptions options)
{
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        var folder = Path.Combine(options.DataDirectory, "backups");
        var todays = Path.Combine(folder, $"tracker-{clock.Today:yyyyMMdd}.db");
        var due = !File.Exists(todays);
        if (due)
        {
            await store.BackupAsync(todays, cancellationToken);
        }

        var backups = Directory.Exists(folder) ? Directory.GetFiles(folder, "tracker-*.db") : [];
        foreach (var old in backups.OrderDescending().Skip(Math.Max(1, options.BackupsToKeep)))
        {
            File.Delete(old);
        }

        // Every pass, so copies made by an earlier version are protected too.
        foreach (var kept in backups.Where(File.Exists))
        {
            TrackerServices.OwnerOnly(kept);
        }

        return due;
    }
}
