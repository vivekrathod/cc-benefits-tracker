using System.Net;
using System.Text;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;
using Chitragupta.Infrastructure.Catalog;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>
/// The refresh end to end, against a stand-in for the source's server: the real store,
/// the real conversion and the saved copy in a temporary data folder.
/// </summary>
public class CatalogRefreshTests : IAsyncLifetime
{
    // What the source serves next: the earlier dataset with Gold renamed, its dining and hotel
    // credits and the Cash Back card dropped, and a new card added.
    private const string Newer = """
        {
          "schemaVersion": 2,
          "cards": [
            { "id": "example-gold", "name": "Gold Card", "issuer": "AMERICAN_EXPRESS", "annualFee": 350,
              "benefits": [ { "id": "lounge", "name": "Lounge passes", "type": "perk", "frequency": "annual", "alignment": "anniversary" } ] },
            { "id": "example-new", "name": "Brand New", "issuer": "CHASE", "annualFee": 95, "benefits": [] }
          ]
        }
        """;

    private readonly TempFolder _folder = new();
    private readonly FixedClock _clock = new();
    private readonly Source _source = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LiveCatalog Catalog => _services.GetRequiredService<LiveCatalog>();

    private CatalogRefresher Refresher => _services.GetRequiredService<CatalogRefresher>();

    public async ValueTask InitializeAsync()
    {
        // The tracker has run before: it holds a copy of the source fetched eight days ago.
        var earlier = DatasetImporter.Read(Bytes(CatalogTests.Dataset)) with { RetrievedAt = _clock.Now.AddDays(-8) };
        _folder.Write("catalog-source.json", earlier.ToJson());

        var services = new ServiceCollection().AddLogging().AddTracker(new TrackerOptions { DataDirectory = _folder.Path });
        services.AddSingleton<IClock>(_clock);
        services.AddHttpClient<CatalogRefresher>().ConfigurePrimaryHttpMessageHandler(() => _source);
        _services = services.BuildServiceProvider();
        await _services.MigrateTrackerDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task A_refresh_follows_the_source_and_keeps_what_the_household_relies_on()
    {
        var store = _services.GetRequiredService<ITrackerStore>();
        await store.SaveCardAsync(new Card(Guid.NewGuid(), Guid.NewGuid(), "example-cash", DateOnly.Parse("2024-03-01"), new Anniversary(3, 1)), Ct);
        var gold = new Card(Guid.NewGuid(), Guid.NewGuid(), "example-gold", DateOnly.Parse("2024-03-01"), new Anniversary(3, 1));
        await store.SaveCardAsync(gold, Ct);
        var november = new BenefitPeriod(
            Guid.NewGuid(), gold.Id, "example-gold:dining", new DateRange(DateOnly.Parse("2026-11-01"), DateOnly.Parse("2026-11-30")), Money.FromDollars(10));
        november.Use(Money.FromDollars(10), UsageSource.Manual);
        await store.SavePeriodsAsync([november], Ct);
        _source.Serves(Newer);

        Assert.True(await Refresher.RunIfDueAsync(Ct));

        // Names, fees and new cards are the source's.
        Assert.Equal(("Amex Gold Card", Money.FromDollars(350)), (Catalog.FindProduct("example-gold")!.DisplayName, Catalog.FindProduct("example-gold")!.AnnualFee));
        Assert.NotNull(Catalog.FindProduct("example-new"));

        // The dining credit has recorded use, so it stays as ended today. The hotel credit had none and goes.
        Assert.Equal(_clock.Today, Catalog.FindBenefit("example-gold:dining")!.LastDay);
        Assert.Null(Catalog.FindBenefit("example-gold:hotel"));

        // A card still has the dropped product, so it stays too. Both are flagged.
        Assert.NotNull(Catalog.FindProduct("example-cash"));
        var status = Catalog.Status;
        Assert.Equal((_clock.Now, _clock.Now, null), (status.RetrievedAt, status.CheckedAt, status.Problem));
        Assert.Contains(status.Flags, flag => flag.StartsWith("Some Bank Cash Back left the source on 2026-11-25.", StringComparison.Ordinal));
        Assert.Contains(status.Flags, flag => flag.StartsWith("Amex Gold Card: \"Dining credit\" left the source on 2026-11-25.", StringComparison.Ordinal));

        // The copy was saved, so a restart starts from it and is not due again for a week.
        var restarted = LiveCatalog.Open(new TrackerOptions { DataDirectory = _folder.Path });
        Assert.Equal(status, restarted.Status with { Flags = status.Flags });
        Assert.Equal(status.Flags, restarted.Status.Flags);
        Assert.False(await Refresher.RunIfDueAsync(Ct));
        Assert.Equal(1, _source.Requests);
    }

    [Fact]
    public async Task A_refresh_that_brings_nothing_new_changes_nothing()
    {
        _source.Serves(CatalogTests.Dataset);

        Assert.False(await Refresher.RunIfDueAsync(Ct));

        Assert.Equal(_clock.Now, Catalog.Status.RetrievedAt);
        Assert.Equal("Amex Gold", Catalog.FindProduct("example-gold")!.DisplayName);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "HttpRequestException")]
    [InlineData(HttpStatusCode.OK, "<html>not the dataset</html>", "JsonReaderException")]
    [InlineData(HttpStatusCode.OK, """{ "schemaVersion": 3, "cards": [] }""", "The source is in format 3")]
    [InlineData(HttpStatusCode.OK, """{ "schemaVersion": 2, "cards": [] }""", "The source lists 0 cards where it had 2")]
    [InlineData(HttpStatusCode.OK, """{ "schemaVersion": 2, "cards": [ { "id": "a", "name": "A", "issuer": "X", "benefits": [] }, { "id": "a", "name": "A", "issuer": "X", "benefits": [] } ] }""", "ArgumentException")]
    public async Task A_source_that_is_down_or_broken_leaves_the_catalog_as_it_was(HttpStatusCode code, string body, string problem)
    {
        _source.Serves(body, code);

        Assert.False(await Refresher.RunIfDueAsync(Ct));

        var status = Catalog.Status;
        Assert.Contains(problem, status.Problem);
        Assert.Equal((_clock.Now.AddDays(-8), _clock.Now), (status.RetrievedAt, status.CheckedAt));
        Assert.Equal(["example-cash", "example-gold"], Catalog.Products.Select(product => product.Id).Where(id => id.StartsWith("example", StringComparison.Ordinal)).Order());

        // It is not asked again every few minutes, and a refresh on request still goes through.
        Assert.False(await Refresher.RunIfDueAsync(Ct));
        Assert.Equal(1, _source.Requests);
        _source.Serves(Newer);
        Assert.True(await Refresher.RunAsync(Ct));
        Assert.Null(Catalog.Status.Problem);
    }

    [Fact]
    public async Task Refreshing_can_be_switched_off()
    {
        var services = new ServiceCollection().AddLogging().AddTracker(
            new TrackerOptions { DataDirectory = _folder.Path, Catalog = new CatalogOptions { RefreshDays = 0 } });
        services.AddHttpClient<CatalogRefresher>().ConfigurePrimaryHttpMessageHandler(() => _source);
        await using var off = services.BuildServiceProvider();

        Assert.False(await off.GetRequiredService<CatalogRefresher>().RunIfDueAsync(Ct));
        Assert.Equal(0, _source.Requests);
    }

    [Fact]
    public void A_damaged_saved_copy_falls_back_to_the_shipped_catalog_and_says_so()
    {
        _folder.Write("catalog-source.json", "{ not json");

        var catalog = LiveCatalog.Open(new TrackerOptions { DataDirectory = _folder.Path });

        Assert.NotNull(catalog.FindProduct("amex-gold"));
        Assert.Contains("could not be read", catalog.Status.Problem);
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private sealed class Source : HttpMessageHandler
    {
        private string _body = "{}";
        private HttpStatusCode _code = HttpStatusCode.OK;

        public int Requests { get; private set; }

        public void Serves(string body, HttpStatusCode code = HttpStatusCode.OK) => (_body, _code) = (body, code);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(DatasetImporter.DatasetUrl, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body, Encoding.UTF8, "text/plain") });
        }
    }
}
