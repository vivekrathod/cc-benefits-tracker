using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure;

/// <summary>One dated copy of the database a day, keeping the most recent few.</summary>
public sealed class DatabaseBackup(ITrackerStore store, IClock clock, TrackerOptions options)
{
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        var folder = Path.Combine(options.DataDirectory, "backups");
        var todays = Path.Combine(folder, $"tracker-{clock.Today:yyyyMMdd}.db");
        if (File.Exists(todays))
        {
            return false;
        }

        await store.BackupAsync(todays, cancellationToken);

        foreach (var old in Directory.EnumerateFiles(folder, "tracker-*.db").OrderDescending().Skip(Math.Max(1, options.BackupsToKeep)))
        {
            File.Delete(old);
        }

        return true;
    }
}
