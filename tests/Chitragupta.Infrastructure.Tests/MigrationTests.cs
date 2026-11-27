using Chitragupta.Application.Ports;
using Chitragupta.Domain;
using Chitragupta.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Chitragupta.Infrastructure.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task What_was_known_about_each_account_becomes_the_first_line_of_its_history()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddTracker(new TrackerOptions { DataDirectory = _folder.Path })
            .BuildServiceProvider();

        // A database as the version before left it: one row per account, the latest attempt only.
        await using (var db = await services.GetRequiredService<IDbContextFactory<TrackerDbContext>>().CreateDbContextAsync(Ct))
        {
            await db.GetService<IMigrator>().MigrateAsync("BalanceExpiryAndChecks", Ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "BalanceChecks" ("Account", "Owner", "Program", "Status", "Problem", "CheckedAt")
                VALUES ('alex-aa', 'alex', 'American AAdvantage', 'NeedsUser', 'the sign-in form came back', 1795800600000),
                       ('alex-united', 'alex', 'United MileagePlus', 'Ok', NULL, 1795801500000);
                """,
                Ct);
        }

        await services.MigrateTrackerDatabaseAsync(Ct);

        var checks = (await services.GetRequiredService<ITrackerStore>().GetBalanceChecksAsync(Ct)).OrderBy(check => check.Account).ToArray();
        Assert.Equal(
            [
                new BalanceCheck(
                    "alex-aa", "alex", "American AAdvantage", BalanceCheckStatus.NeedsUser, "the sign-in form came back",
                    DateTimeOffset.FromUnixTimeMilliseconds(1795800600000)),
                new BalanceCheck(
                    "alex-united", "alex", "United MileagePlus", BalanceCheckStatus.Ok, null, DateTimeOffset.FromUnixTimeMilliseconds(1795801500000)),
            ],
            checks);
    }
}
