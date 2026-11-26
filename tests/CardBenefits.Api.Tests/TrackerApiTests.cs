using System.Net;
using System.Net.Http.Json;
using System.Text;
using CardBenefits.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CardBenefits.Api.Tests;

/// <summary>
/// The whole service over HTTP: the real host, the built-in catalog and a SQLite file
/// in a temporary folder. No feed is configured, as on a fresh install.
/// </summary>
public sealed class TrackerApiTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tracker-api-tests-").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TrackerApiTests()
    {
        // The catalog's source lives on the internet, so the tests leave it alone.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("Tracker:DataDirectory", _folder)
            // The tests work out "today" from the UTC clock, so the service must count its days the same way.
            // Left on its default zone it is a day behind for a few hours every evening, and a card that
            // opens "today" then has its cardmember year end today instead of start today.
            .UseSetting("Tracker:TimeZone", "UTC")
            .UseSetting("Tracker:Catalog:RefreshDays", "0"));
        _client = _factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
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

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_fresh_install_is_empty_and_says_the_feed_is_not_configured()
    {
        Assert.Equal(new SummaryDto(0, 0, 0, 0), await GetAsync<SummaryDto>("/api/summary"));
        Assert.Empty(await GetAsync<List<CardDto>>("/api/cards"));
        Assert.False((await GetAsync<SyncHealthDto>("/api/sync")).FeedConfigured);

        var refused = await _client.PostAsJsonAsync("/api/sync", new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("not set up", await DetailAsync(refused));
    }

    [Fact]
    public async Task The_catalog_keeps_the_sources_names_and_says_where_it_stands()
    {
        var products = await GetAsync<List<ProductDto>>("/api/catalog/products");
        var executive = Assert.Single(products, product => product.Id == "citi-aadvantage-executive");
        Assert.Equal("Citi AAdvantage Executive", executive.DisplayName);
        Assert.Contains(executive.AlsoCalled!, name => name.Contains("World Legend", StringComparison.Ordinal));
        Assert.Equal("citi-aadvantage-executive", ProductSearch.Find(products, "citi world executive")[0].Id);

        // Nothing has been fetched yet and, in these tests, nothing will be: the shipped copy is in use.
        var status = await GetAsync<CatalogStatusDto>("/api/catalog/status");
        Assert.Equal((true, false, products.Count), (status.FollowsSource, status.RefreshesItself, status.Products));
        Assert.Null(status.RetrievedAt);
        Assert.Null(status.Problem);
        Assert.Empty(status.Flags);
    }

    [Fact]
    public async Task A_card_added_by_hand_can_be_tracked_and_backfilled_from_a_statement()
    {
        var person = await PostAsync<PersonDto>("/api/people", new AddPersonRequest("Alex"));
        var products = await GetAsync<List<ProductDto>>("/api/catalog/products");
        Assert.Contains(products, product => product.Id == "amex-gold" && product.DisplayName == "Amex Gold");
        Assert.NotEmpty(await GetAsync<List<BenefitDto>>("/api/catalog/products/amex-gold/benefits"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cardId = await PostAsync<Guid>("/api/cards", new AddCardRequest(person.Id, "amex-gold", today.AddYears(-2), null, null, "1234", null));

        var group = Assert.Single(await GetAsync<List<CardPeriodsDto>>("/api/this-period"));
        Assert.Equal((cardId, "Alex", "Amex Gold", 325m), (group.Card.Id, group.Card.PersonName, group.Card.ProductName, group.Card.AnnualFee));
        var dining = Assert.Single(group.Periods, period => period.BenefitId == "amex-gold:dining-credit");
        Assert.Equal((10m, 0m, false), (dining.Amount, dining.Used, dining.Manual));
        var uber = Assert.Single(group.Periods, period => period.BenefitId == "amex-gold:uber-cash");
        Assert.True(uber.Manual);

        // One tap for the benefit that never reaches the statement.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/api/periods/{uber.Id}/use", new UsePeriodRequest(null, null), Ct)).StatusCode);

        // A statement export fills in this month's dining credit. Amex writes credits as negatives.
        var midMonth = new DateOnly(today.Year, today.Month, 1).AddDays(9);
        var csv = $"Date,Description,Amount\n{midMonth:MM/dd/yyyy},AMEX Dining Credit,-10.00\n{midMonth:MM/dd/yyyy},RESTAURANT,48.00\n";
        var upload = await _client.PostAsync($"/api/cards/{cardId}/statements", new StringContent(csv, Encoding.UTF8, "text/csv"), Ct);
        Assert.Equal(new ImportResultDto(2, 2), await upload.Content.ReadFromJsonAsync<ImportResultDto>(Ct));

        var periods = (await GetAsync<List<CardPeriodsDto>>("/api/this-period")).Single().Periods;
        Assert.Equal("Used", periods.Single(period => period.Id == uber.Id).Status);
        if (midMonth <= today)
        {
            Assert.Equal(10m, periods.Single(period => period.Id == dining.Id).Used);
        }

        var value = Assert.Single(await GetAsync<List<CardValueDto>>("/api/card-value"));
        Assert.Equal(325m, value.AnnualFee);
        Assert.True(value.Used >= 10m);

        var expiring = await GetAsync<List<PeriodDto>>("/api/expiring?days=400");
        Assert.DoesNotContain(expiring, period => period.Id == uber.Id);
        Assert.Contains(expiring, period => period.BenefitId == "amex-gold:resy-credit");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/periods/{uber.Id}/manual-usage", Ct)).StatusCode);
        Assert.Contains(await GetAsync<List<PeriodDto>>("/api/expiring?days=400"), period => period.Id == uber.Id);
    }

    [Fact]
    public async Task Bad_requests_come_back_as_problem_details()
    {
        var person = await PostAsync<PersonDto>("/api/people", new AddPersonRequest("Alex"));

        var unknownProduct = await _client.PostAsJsonAsync(
            "/api/cards", new AddCardRequest(person.Id, "no-such-card", new DateOnly(2025, 1, 1), null, null, null, null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknownProduct.StatusCode);
        Assert.Equal("application/problem+json", unknownProduct.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-such-card", await DetailAsync(unknownProduct));

        var missingPeriod = await _client.PostAsJsonAsync($"/api/periods/{Guid.NewGuid()}/use", new UsePeriodRequest(null, null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingPeriod.StatusCode);

        var cardId = await PostAsync<Guid>("/api/cards", new AddCardRequest(person.Id, "amex-gold", new DateOnly(2025, 1, 1), null, null, null, null));
        var notCsv = await _client.PostAsync($"/api/cards/{cardId}/statements", new StringContent("hello"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notCsv.StatusCode);
    }

    [Fact]
    public async Task The_web_app_is_served_for_any_page_address()
    {
        foreach (var path in new[] { "/", "/review" })
        {
            var response = await _client.GetAsync(path, Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<title>Card benefits</title>", await response.Content.ReadAsStringAsync(Ct));
        }
    }

    private async Task<T> GetAsync<T>(string url) => (await _client.GetFromJsonAsync<T>(url, Ct))!;

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct))!["detail"].ToString()!;
}
