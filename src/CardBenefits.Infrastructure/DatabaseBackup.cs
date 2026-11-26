using System.Text.RegularExpressions;
using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure;

/// <summary>
/// Copies of the database: one dated copy a day, keeping the most recent few, and named
/// copies taken on request. Both are made by SQLite through the tracker's own connection.
/// Copying or opening the file from outside, such as from the host of a container, can
/// lose what the running service wrote last.
/// </summary>
public sealed partial class DatabaseBackup(ITrackerStore store, IClock clock, TrackerOptions options)
{
    private string Folder => Path.Combine(options.DataDirectory, "backups");

    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        var todays = Path.Combine(Folder, $"tracker-{clock.Today:yyyyMMdd}.db");
        var due = !File.Exists(todays);
        if (due)
        {
            await store.BackupAsync(todays, cancellationToken);
        }

        // Only the dated copies are counted and pruned. Named ones stay until a person removes them.
        var backups = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.db") : [];
        var dated = backups.Where(file => Dated().IsMatch(Path.GetFileName(file)));
        foreach (var old in dated.OrderDescending().Skip(Math.Max(1, options.BackupsToKeep)))
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

    /// <summary>A copy taken now under a name, for before a risky change. Safe while the service runs.</summary>
    public async Task<string> CopyNowAsync(string label, CancellationToken cancellationToken)
    {
        if (!Label().IsMatch(label))
        {
            throw new ArgumentException("A copy's name may hold letters, digits, dots and dashes only.", nameof(label));
        }

        var copy = Path.Combine(Folder, $"copy-{label}.db");
        await store.BackupAsync(copy, cancellationToken);
        TrackerServices.OwnerOnly(copy);
        return copy;
    }

    [GeneratedRegex(@"^tracker-\d{8}\.db$")]
    private static partial Regex Dated();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,60}$")]
    private static partial Regex Label();
}
