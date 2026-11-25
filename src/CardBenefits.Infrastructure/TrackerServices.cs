using CardBenefits.Application;
using CardBenefits.Application.Cards;
using CardBenefits.Application.Matching;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Reminders;
using CardBenefits.Application.Review;
using CardBenefits.Application.Statements;
using CardBenefits.Application.Sync;
using CardBenefits.Application.Usage;
using CardBenefits.Application.Views;
using CardBenefits.Infrastructure.Catalog;
using CardBenefits.Infrastructure.Feeds.Csv;
using CardBenefits.Infrastructure.Feeds.SimpleFin;
using CardBenefits.Infrastructure.Notifications;
using CardBenefits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardBenefits.Infrastructure;

/// <summary>The one place the tracker is wired together, shared by the API host and the CLI.</summary>
public static class TrackerServices
{
    public static IServiceCollection AddTracker(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Tracker").Get<TrackerOptions>() ?? new TrackerOptions();
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
            DigestDay = options.Reminders.DigestDay,
            DigestAt = options.Reminders.DigestAt,
        });

        services.AddSingleton<IClock>(new SystemClock(TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)));
        services.AddSingleton<ICatalog>(_ => JsonCatalog.Load(options.CatalogPath));

        services.AddDbContextFactory<TrackerDbContext>(db => db.UseSqlite($"Data Source={options.DatabasePath}"));
        services.AddSingleton<ITrackerStore, SqliteTrackerStore>();

        services.AddSingleton<IStatementParser, CsvStatementParser>();
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
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>The live feed's name, or null while it is not configured.</summary>
    public static string LiveFeedName => SimpleFinFeed.FeedName;
}
