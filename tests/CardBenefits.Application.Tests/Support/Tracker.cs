using CardBenefits.Application.Balances;
using CardBenefits.Application.Cards;
using CardBenefits.Application.Matching;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Reminders;
using CardBenefits.Application.Review;
using CardBenefits.Application.Statements;
using CardBenefits.Application.Sync;
using CardBenefits.Application.Usage;
using CardBenefits.Application.Views;
using CardBenefits.Contracts;
using CardBenefits.Domain;
using CardBenefits.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardBenefits.Application.Tests.Support;

/// <summary>
/// The whole tracker as the host wires it, on a real SQLite file in a temporary folder.
/// Only the edges are faked: the feed, the clock and the notifier.
/// </summary>
internal sealed class Tracker : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _folder;

    private Tracker(ServiceProvider services, string folder, FakeClock clock, FakeFeed feed, RecordingNotifier notifier)
    {
        _services = services;
        _folder = folder;
        Clock = clock;
        Feed = feed;
        Notifier = notifier;
    }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FakeClock Clock { get; }

    public FakeFeed Feed { get; }

    public RecordingNotifier Notifier { get; }

    public string DataFolder => _folder;

    public ITrackerStore Store => Get<ITrackerStore>();

    public SyncService Sync => Get<SyncService>();

    public CardService Cards => Get<CardService>();

    public ReviewService Review => Get<ReviewService>();

    public UsageService Usage => Get<UsageService>();

    public TrackerQueries Queries => Get<TrackerQueries>();

    public ReminderService Reminders => Get<ReminderService>();

    public Rematcher Rematcher => Get<Rematcher>();

    public StatementImportService Statements => Get<StatementImportService>();

    public BalanceService Balances => Get<BalanceService>();

    public static async Task<Tracker> StartAsync(string now = "2026-11-25 10:00")
    {
        var folder = Path.Combine(Path.GetTempPath(), "tracker-tests", Guid.NewGuid().ToString("N"));
        var clock = new FakeClock();
        clock.Set(now);
        var feed = new FakeFeed();
        var notifier = new RecordingNotifier();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTracker(new TrackerOptions { DataDirectory = folder });
        services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
        services.Replace(ServiceDescriptor.Singleton<ITransactionFeed>(feed));
        services.Replace(ServiceDescriptor.Singleton<INotifier>(notifier));
        services.Replace(ServiceDescriptor.Singleton<ICatalog>(new TestCatalog()));

        var tracker = new Tracker(services.BuildServiceProvider(), folder, clock, feed, notifier);
        await tracker._services.MigrateTrackerDatabaseAsync(Ct);
        return tracker;
    }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    // Shorthand for the steps most tests share.

    public async Task<SyncRun> SyncAsync() => await Sync.RunAsync(Feed, await Sync.NextWindowAsync(Feed.Name, Ct), null, Ct);

    public async Task<Person> PersonAsync(string name = "Alex") => await Cards.AddPersonAsync(name, Ct);

    /// <summary>Adds a feed account, syncs, and confirms it as a card.</summary>
    public async Task<Card> CardAsync(
        string product = TestCatalog.Gold,
        string accountId = "acct-1",
        string accountName = "Gold Card (1234)",
        string anniversary = "05-06",
        string? openedOn = "2024-05-06",
        string person = "Alex",
        params Feeds.FeedTransaction[] transactions)
    {
        Feed.Account(accountId, accountName, "Amex", transactions);
        await SyncAsync();
        var owner = await PersonAsync(person);
        var account = (await Store.GetFeedAccountsAsync(Ct)).Single(candidate => candidate.AccountId == accountId);
        var parts = anniversary.Split('-').Select(int.Parse).ToArray();
        return await Cards.ConfirmCardAsync(
            account.Id,
            new ConfirmCardRequest(owner.Id, product, parts[0], parts[1], openedOn is null ? null : DateOnly.Parse(openedOn), null),
            Ct);
    }

    public async Task<BenefitPeriod> PeriodAsync(string benefitId, string containing)
    {
        var day = DateOnly.Parse(containing);
        return (await Store.GetPeriodsAsync(null, Ct)).Single(period => period.BenefitId == benefitId && period.Range.Contains(day));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
