using CardBenefits.Application.Feeds;
using CardBenefits.Application.Ports;
using CardBenefits.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CardBenefits.Infrastructure.Tests;

public class StoreTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ITrackerStore Store => _services.GetRequiredService<ITrackerStore>();

    public async ValueTask InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddLogging()
            .AddTracker(new TrackerOptions { DataDirectory = _folder.Path })
            .BuildServiceProvider();
        await _services.MigrateTrackerDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task A_card_comes_back_with_its_product_history_and_fee()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "first", DateOnly.Parse("2024-02-29"), new Anniversary(2, 29), "0042");
        card.ChangeProduct("second", DateOnly.Parse("2025-06-01"));
        card.SetAnnualFee(Money.FromDollars(95));
        card.Close(DateOnly.Parse("2026-03-10"));

        await Store.SaveCardAsync(card, Ct);
        await Store.SaveCardAsync(card, Ct);
        var restored = await Store.FindCardAsync(card.Id, Ct);

        Assert.NotNull(restored);
        Assert.Equal(card.Products, restored.Products);
        Assert.Equal(
            (card.PersonId, "0042", new Anniversary(2, 29), (DateOnly?)DateOnly.Parse("2026-03-10"), (Money?)Money.FromDollars(95)),
            (restored.PersonId, restored.LastFour, restored.Anniversary, restored.ClosedOn, restored.AnnualFeeOverride));
    }

    [Fact]
    public async Task A_period_comes_back_with_its_usage_in_order()
    {
        var transaction = Guid.NewGuid();
        var period = new BenefitPeriod(
            Guid.NewGuid(), Guid.NewGuid(), "gold:dining", new DateRange(DateOnly.Parse("2026-11-01"), DateOnly.Parse("2026-11-30")), Money.FromDollars(10));
        period.Use(Money.FromDollars(2), UsageSource.Manual, null, "by hand");
        period.Use(Money.FromDollars(7.5m), UsageSource.Automatic, transaction);

        await Store.SavePeriodsAsync([period], Ct);
        var restored = await Store.FindPeriodAsync(period.Id, Ct);

        Assert.NotNull(restored);
        Assert.Equal(period.Usage, restored.Usage);
        Assert.Equal(Money.FromDollars(0.5m), restored.Remaining);

        restored.ClearManualUsage();
        await Store.SavePeriodsAsync([restored], Ct);
        Assert.Equal(Money.FromDollars(7.5m), Assert.Single(await Store.GetPeriodsAsync(period.CardId, Ct)).Used);
    }

    [Fact]
    public async Task Upsert_counts_only_real_changes()
    {
        var account = Guid.NewGuid();
        FeedTransaction Row(string id, decimal amount, string description = "X") =>
            new(id, DateOnly.Parse("2026-11-10"), null, Money.FromDollars(amount), description, false, "{}");

        Assert.Equal(2, await Store.UpsertTransactionsAsync(account, [Row("a", 1), Row("b", 2)], DateTimeOffset.UtcNow, Ct));
        Assert.Equal(0, await Store.UpsertTransactionsAsync(account, [Row("a", 1), Row("b", 2)], DateTimeOffset.UtcNow, Ct));
        Assert.Equal(1, await Store.UpsertTransactionsAsync(account, [Row("a", 1), Row("b", -2, "CORRECTED")], DateTimeOffset.UtcNow, Ct));

        // A posted transaction missing from a later, narrower window is history, not a deletion.
        Assert.Equal(0, await Store.UpsertTransactionsAsync(account, [Row("a", 1)], DateTimeOffset.UtcNow, Ct));
        var stored = await Store.GetTransactionsAsync(account, Ct);
        Assert.Equal(2, stored.Count);
        Assert.Equal((Money.FromDollars(-2), "CORRECTED"), stored.Where(row => row.FeedId == "b").Select(row => (row.Amount, row.Description)).Single());
    }

    [Fact]
    public async Task A_reminder_is_recorded_once_until_it_is_forgotten()
    {
        Assert.True(await Store.TryRecordReminderAsync("nudge:1", DateTimeOffset.UtcNow, Ct));
        Assert.False(await Store.TryRecordReminderAsync("nudge:1", DateTimeOffset.UtcNow, Ct));

        await Store.ForgetReminderAsync("nudge:1", Ct);
        Assert.True(await Store.TryRecordReminderAsync("nudge:1", DateTimeOffset.UtcNow, Ct));
    }

    [Fact]
    public async Task One_backup_a_day_is_kept_up_to_the_limit()
    {
        await Store.SavePersonAsync(new Person(Guid.NewGuid(), "Alex"), Ct);
        var backups = Path.Combine(_folder.Path, "backups");
        Directory.CreateDirectory(backups);
        foreach (var day in Enumerable.Range(10, 9))
        {
            File.WriteAllText(Path.Combine(backups, $"tracker-202611{day}.db"), "old");
        }

        var backup = _services.GetRequiredService<DatabaseBackup>();
        Assert.True(await backup.RunIfDueAsync(Ct));
        Assert.False(await backup.RunIfDueAsync(Ct));

        var kept = Directory.GetFiles(backups).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(7, kept.Length);
        Assert.Equal("tracker-20261113.db", kept[0]);
        var newest = Path.Combine(backups, kept[^1]!);
        Assert.True(new FileInfo(newest).Length > 4096);

        if (!OperatingSystem.IsWindows())
        {
            // Older backups and SQLite's side files hold the same data as the database itself.
            var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var files = Directory.GetFiles(backups).Concat(Directory.GetFiles(_folder.Path, "tracker.db*")).ToArray();
            Assert.Contains(Path.Combine(_folder.Path, "tracker.db"), files);
            foreach (var file in files)
            {
                Assert.Equal(ownerOnly, File.GetUnixFileMode(file));
            }
        }
    }
}
