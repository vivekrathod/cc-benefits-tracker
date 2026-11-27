using Chitragupta.Application;
using Chitragupta.Application.Balances;
using Chitragupta.Application.Cards;
using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Reminders;
using Chitragupta.Application.Review;
using Chitragupta.Application.Statements;
using Chitragupta.Application.Sync;
using Chitragupta.Application.Usage;
using Chitragupta.Application.Views;
using Chitragupta.Infrastructure.Balances;
using Chitragupta.Infrastructure.Catalog;
using Chitragupta.Infrastructure.Feeds.Csv;
using Chitragupta.Infrastructure.Feeds.SimpleFin;
using Chitragupta.Infrastructure.Notifications;
using Chitragupta.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chitragupta.Infrastructure;

/// <summary>The one place the tracker is wired together, shared by the API host and the CLI.</summary>
public static class TrackerServices
{
    public static IServiceCollection AddTracker(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Chitragupta").Get<TrackerOptions>() ?? new TrackerOptions();
        return services.AddTracker(options);
    }

    public static IServiceCollection AddTracker(this IServiceCollection services, TrackerOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(new TrackerSettings
        {
            TrackingStart = options.TrackingStart,
            HorizonDays = options.HorizonDays,
            ExpiringSoonDays = options.ExpiringSoonDays,
            SyncWindowDays = options.SimpleFin.WindowDays,
            FirstSyncWindowDays = options.SimpleFin.FirstWindowDays,
            MinutesBetweenSyncs = options.SimpleFin.MinutesBetweenSyncs,
            DailySyncAt = options.SimpleFin.DailyAt,
            NudgeDaysBefore = options.Reminders.NudgeDaysBefore,
            BalanceNudgeDaysBefore = options.Reminders.BalanceNudgeDaysBefore,
            DigestDay = options.Reminders.DigestDay,
            DigestAt = options.Reminders.DigestAt,
        });

        services.AddSingleton<IClock>(new SystemClock(TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)));
        services.AddSingleton(_ => LiveCatalog.Open(options));
        services.AddSingleton<ICatalog>(provider => provider.GetRequiredService<LiveCatalog>());
        services.AddHttpClient<CatalogRefresher>(http => http.Timeout = TimeSpan.FromMinutes(1));

        services.AddDbContextFactory<TrackerDbContext>(db => db.UseSqlite($"Data Source={options.DatabasePath}"));
        services.AddSingleton<ITrackerStore, SqliteTrackerStore>();

        services.AddSingleton<IStatementParser, CsvStatementParser>();
        services.AddSingleton<IBalanceReportParser, UdaanBalanceParser>();
        services.AddSingleton(new SimpleFinAccess(options.SimpleFin.AccessUrlFile));
        services.AddHttpClient<SimpleFinFeed>(http => http.Timeout = TimeSpan.FromMinutes(2));
        services.AddSingleton<ITransactionFeed>(provider => provider.GetRequiredService<SimpleFinFeed>());

        if (options.Smtp.IsConfigured)
        {
            services.AddSingleton<INotifier>(new SmtpNotifier(options.Smtp));
        }
        else
        {
            services.AddSingleton<INotifier, LogNotifier>();
        }

        services.AddSingleton<Rematcher>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<CardService>();
        services.AddSingleton<UsageService>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<StatementImportService>();
        services.AddSingleton<BalanceService>();
        services.AddSingleton<TrackerQueries>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<DatabaseBackup>();
        return services;
    }

    /// <summary>Creates the database on first run and applies any new migrations.</summary>
    public static async Task MigrateTrackerDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<TrackerOptions>();
        Directory.CreateDirectory(options.DataDirectory);

        var contexts = services.GetRequiredService<IDbContextFactory<TrackerDbContext>>();
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        // SQLite's side files carry the same rows as the database. Ones it creates
        // later take the database's mode, so these only need setting once.
        OwnerOnly(options.DatabasePath);
        OwnerOnly(options.DatabasePath + "-wal");
        OwnerOnly(options.DatabasePath + "-shm");
    }

    /// <summary>The database holds household transactions, so only its owner may read it.</summary>
    internal static void OwnerOnly(string file)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(file))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // Some mounted file systems do not carry Unix modes. The folder's own protection applies there.
        }
    }

    /// <summary>The live feed's name, or null while it is not configured.</summary>
    public static string LiveFeedName => SimpleFinFeed.FeedName;
}
