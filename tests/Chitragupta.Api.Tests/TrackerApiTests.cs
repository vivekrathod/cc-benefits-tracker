using System.Net;
using System.Net.Http.Json;
using System.Text;
using Chitragupta.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Chitragupta.Api.Tests;

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
            .UseSetting("Chitragupta:DataDirectory", _folder)
            // The tests work out "today" from the UTC clock, so the service must count its days the same way.
            // Left on its default zone it is a day behind for a few hours every evening, and a card that
            // opens "today" then has its cardmember year end today instead of start today.
            .UseSetting("Chitragupta:TimeZone", "UTC")
            .UseSetting("Chitragupta:Catalog:RefreshDays", "0"));
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
    public async Task Miles_arrive_from_udaan_on_one_address_and_can_be_set_and_removed_by_hand()
    {
        var person = await PostAsync<PersonDto>("/api/people", new AddPersonRequest("Alex"));
        var expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400);

        // The body is what "udaan check" printed, piped through curl untouched.
        var result = $$"""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "ok": true, "status": "ok", "source": "live",
              "checked_at": "{{DateTimeOffset.UtcNow.AddMinutes(-5):yyyy-MM-ddTHH:mm:ss}}+00:00",
              "balance": { "award_miles": 45210, "loyalty_points": 0, "miles_expire": "{{expires:yyyy-MM-dd}}" } }
            """;
        var posted = await _client.PostAsync("/api/balances/udaan", new StringContent(result, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal([new BalanceImportLineDto("alex-aa", "Ok", true, null)], await posted.Content.ReadFromJsonAsync<List<BalanceImportLineDto>>(Ct));

        // curl's own content type, for when the header is left off.
        var again = await _client.PostAsync(
            "/api/balances/udaan", new StringContent(result, Encoding.UTF8, "application/x-www-form-urlencoded"), Ct);
        Assert.Equal([new BalanceImportLineDto("alex-aa", "Ok", false, null)], await again.Content.ReadFromJsonAsync<List<BalanceImportLineDto>>(Ct));

        var byHand = await _client.PostAsJsonAsync("/api/balances", new RecordBalanceRequest(person.Id, "Chase Ultimate Rewards", 80_000, null), Ct);
        Assert.Equal(HttpStatusCode.NoContent, byHand.StatusCode);

        var view = await GetAsync<BalancesDto>("/api/balances");
        Assert.Equal(
            [("American AAdvantage", 45_210L, "Automation", (DateOnly?)expires, (int?)400), ("Chase Ultimate Rewards", 80_000L, "Manual", null, null)],
            view.Balances.Select(balance => (balance.Program, balance.Amount, balance.Source, balance.ExpiresOn, balance.DaysUntilExpiry)));
        Assert.All(view.Balances, balance => Assert.Equal((person.Id, "Alex", false), (balance.PersonId, balance.PersonName, balance.ExpiresSoon)));
        Assert.Equal(("alex-aa", "Ok"), (view.Checks.Single().Account, view.Checks.Single().Status));

        var remove = $"/api/balances?personId={person.Id}&program={Uri.EscapeDataString("Chase Ultimate Rewards")}";
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(remove, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(remove, Ct)).StatusCode);
        Assert.Single((await GetAsync<BalancesDto>("/api/balances")).Balances);

        var notAResult = await _client.PostAsync("/api/balances/udaan", new StringContent("udaan: command not found"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notAResult.StatusCode);
        Assert.Contains("udaan check", await DetailAsync(notAResult));
    }

    [Fact]
    public async Task A_failed_check_arrives_with_what_to_do_and_can_be_forgotten_and_nothing_can_be_asked_without_the_automation()
    {
        await PostAsync<PersonDto>("/api/people", new AddPersonRequest("Alex"));
        var result = $$"""
            { "account": "alex-aa", "program": "aa", "owner": "alex", "udaan_version": "0.3.0", "ok": false, "status": "needs_user",
              "error": "the sign-in form came back; not retrying", "error_code": "login_rejected", "retryable": false,
              "checked_at": "{{DateTimeOffset.UtcNow.AddMinutes(-5):yyyy-MM-ddTHH:mm:ss}}+00:00", "decisions": [], "last_good": null }
            """;
        var posted = await _client.PostAsync("/api/balances/udaan", new StringContent(result, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var view = await GetAsync<BalancesDto>("/api/balances");
        var check = Assert.Single(view.Checks);
        Assert.Equal(("NeedsUser", "login_rejected", 1, false), (check.Status, check.ErrorCode, check.SignInsToday, check.CanCheckNow));
        Assert.Contains("udaan set-login alex-aa", check.Advice);
        Assert.Equal("NeedsUser", Assert.Single(check.Recent).Status);
        Assert.Equal(1, (await GetAsync<SummaryDto>("/api/summary")).BalanceProblems);

        // No automation is connected here, as on an install that only has results pushed in.
        Assert.Null(view.Checker);
        var asked = await _client.PostAsync("/api/balances/checks/alex-aa", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, asked.StatusCode);
        Assert.Contains("No balance automation is connected", await DetailAsync(asked));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/balances/checks/alex-aa", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/balances/checks/alex-aa", Ct)).StatusCode);
        Assert.Equal(0, (await GetAsync<SummaryDto>("/api/summary")).BalanceProblems);
    }

    [Fact]
    public async Task The_web_app_is_served_for_any_page_address()
    {
        foreach (var path in new[] { "/", "/review", "/points" })
        {
            var response = await _client.GetAsync(path, Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<title>Chitragupta</title>", await response.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task Email_alerts_are_set_up_from_the_app_once_a_google_client_is_in_place()
    {
        // A fresh install has no Google client, so there is nothing to start and nothing to try.
        Assert.Equal(new EmailAlertsDto(false, false, null), await GetAsync<EmailAlertsDto>("/api/email"));
        var start = new BeginEmailSignInRequest("http://localhost:8090/");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/email/sign-in", start, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/email/test", null, Ct)).StatusCode);

        var client = Path.Combine(_folder, "google-oauth-client.json");
        await File.WriteAllTextAsync(client, """{ "installed": { "client_id": "made-up-client", "client_secret": "made-up-secret" } }""", Ct);
        using var factory = _factory.WithWebHostBuilder(host => host.UseSetting("Chitragupta:Gmail:ClientFile", client));
        using var withClient = factory.CreateClient();

        Assert.Equal(new EmailAlertsDto(true, false, null), await withClient.GetFromJsonAsync<EmailAlertsDto>("/api/email", Ct));
        var answer = await withClient.PostAsJsonAsync("/api/email/sign-in", start, Ct);
        var ask = (await answer.Content.ReadFromJsonAsync<EmailSignInDto>(Ct))!.Url;
        Assert.StartsWith("https://accounts.google.com/o/oauth2/auth?client_id=made-up-client&redirect_uri=http%3A%2F%2Flocalhost%3A8090%2F&", ask);
        Assert.DoesNotContain("made-up-secret", ask);
        // The address goes out as a browser or any other client can take it: nothing in it left unescaped.
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.send%20openid%20email&", ask);

        // Google sends the browser back to the app's own address, which serves the app; the app hands the code over.
        Assert.Contains("<title>Chitragupta</title>", await withClient.GetStringAsync("/?state=made-up&code=made-up", Ct));
        var finish = await withClient.PostAsJsonAsync("/api/email/sign-in/complete", new CompleteEmailSignInRequest("not-the-state", "made-up"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, finish.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await withClient.DeleteAsync("/api/email", Ct)).StatusCode);
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
