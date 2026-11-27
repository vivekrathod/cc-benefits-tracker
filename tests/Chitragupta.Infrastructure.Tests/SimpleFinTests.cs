using System.Net;
using System.Text;
using Chitragupta.Application.Feeds;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;
using Chitragupta.Infrastructure.Feeds.SimpleFin;

namespace Chitragupta.Infrastructure.Tests;

public class SimpleFinTests
{
    // Made-up values in the shape the Bridge returns (protocol v2).
    private const string Response = """
        {
          "errlist": [ { "code": "con.auth", "msg": "Connection needs to be re-authenticated.", "conn_id": "CON-2" } ],
          "connections": [
            { "conn_id": "CON-1", "name": "Example Bank", "org_id": "ORG-1", "org_name": "Example Bank NA", "org_url": "https://bank.example", "sfin_url": "https://bridge.example/simplefin" },
            { "conn_id": "CON-2", "name": "Other Bank", "org_id": "ORG-2", "org_name": "Other Bank", "org_url": "https://other.example", "sfin_url": "https://bridge.example/simplefin" }
          ],
          "accounts": [
            {
              "id": "ACT-1", "name": "Travel Card (1234)", "conn_id": "CON-1", "currency": "USD",
              "balance": "-120.50", "available-balance": "0.00", "balance-date": 1795000000, "holdings": [],
              "transactions": [
                { "id": "TRN-1", "posted": 1794571200, "amount": "15.00", "description": "TRAVEL  CREDIT", "payee": "Travel Credit", "memo": "", "transacted_at": 1794398400 },
                { "id": "TRN-2", "posted": 1794628800, "amount": "-42.17", "description": "RESTAURANT", "transacted_at": 1794628800 },
                { "id": "TRN-3", "posted": 0, "amount": "-9.99", "description": "COFFEE", "transacted_at": 1794744000, "pending": true }
              ]
            },
            { "id": "ACT-2", "name": "Checking", "conn_id": "CON-9", "currency": "USD", "balance": "10.00", "balance-date": 1795000000, "transactions": [] }
          ]
        }
        """;

    private static readonly FixedClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_response_becomes_accounts_named_by_connection_with_credits_positive()
    {
        var snapshot = SimpleFinParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Response)), Clock.ToLocalDate);

        Assert.Equal(2, snapshot.Accounts.Count);
        var card = snapshot.Accounts[0];
        Assert.Equal(("Example Bank", "ACT-1", "Travel Card (1234)"), (card.ConnectionName, card.AccountId, card.Name));
        Assert.Equal("CON-9", snapshot.Accounts[1].ConnectionName);

        var credit = card.Transactions[0];
        Assert.Equal("TRN-1", credit.Id);
        Assert.Equal(Money.FromDollars(15), credit.Amount);
        Assert.Equal("TRAVEL  CREDIT", credit.Description);
        Assert.Equal(DateOnly.Parse("2026-11-13"), credit.PostedOn);
        Assert.Equal(DateOnly.Parse("2026-11-11"), credit.TransactedOn);
        Assert.False(credit.Pending);
        Assert.Contains("\"TRN-1\"", credit.RawPayload);

        Assert.Equal(Money.FromDollars(-42.17m), card.Transactions[1].Amount);
    }

    [Fact]
    public void A_pending_transaction_has_no_posted_date()
    {
        var snapshot = SimpleFinParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Response)), Clock.ToLocalDate);

        var pending = snapshot.Accounts[0].Transactions[2];
        Assert.True(pending.Pending);
        Assert.Null(pending.PostedOn);
        Assert.Equal(DateOnly.Parse("2026-11-15"), pending.TransactedOn);
    }

    [Fact]
    public void An_error_is_tied_to_the_institution_it_concerns()
    {
        var snapshot = SimpleFinParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Response)), Clock.ToLocalDate);

        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("con.auth", "Connection needs to be re-authenticated.", "Other Bank"), (error.Code, error.Message, error.ConnectionName));
    }

    [Fact]
    public void A_timestamp_late_in_the_evening_lands_on_the_local_day()
    {
        // 03:30 UTC on the 14th is 22:30 on the 13th in New York.
        const string late = """{ "accounts": [ { "id": "A", "name": "Card", "transactions": [ { "id": "T", "posted": 1794627000, "amount": "1.00", "description": "X" } ] } ] }""";

        var snapshot = SimpleFinParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(late)), Clock.ToLocalDate);

        Assert.Equal(DateOnly.Parse("2026-11-13"), snapshot.Accounts[0].Transactions[0].PostedOn);
    }

    [Fact]
    public async Task The_request_asks_for_the_window_and_keeps_the_credentials_out_of_the_url()
    {
        using var folder = new TempFolder();
        var file = folder.Write("access-url", "https://user%40name:s3cr3t-value@bridge.example/simplefin\n");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) });
        var feed = new SimpleFinFeed(new HttpClient(handler), new SimpleFinAccess(file), Clock);

        var snapshot = await feed.FetchAsync(new DateRange(DateOnly.Parse("2026-11-11"), DateOnly.Parse("2026-11-25")), Ct);

        Assert.Equal(2, snapshot.Accounts.Count);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://bridge.example/simplefin/accounts?version=2&pending=1&start-date=1794355200&end-date=1795737600",
            request.Uri);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user@name:s3cr3t-value")), request.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.PaymentRequired, "402")]
    [InlineData(HttpStatusCode.BadGateway, "502")]
    public async Task A_refusal_is_reported_without_any_part_of_the_access_url(HttpStatusCode status, string expected)
    {
        using var folder = new TempFolder();
        var file = folder.Write("access-url", "https://someuser:s3cr3t-value@bridge.example/simplefin");
        var feed = new SimpleFinFeed(new HttpClient(new StubHandler(_ => new HttpResponseMessage(status))), new SimpleFinAccess(file), Clock);

        var failure = await Assert.ThrowsAsync<FeedUnavailableException>(
            () => feed.FetchAsync(new DateRange(DateOnly.Parse("2026-11-11"), DateOnly.Parse("2026-11-25")), Ct));

        Assert.Contains(expected, failure.Message);
        AssertNoSecret(failure);
    }

    [Fact]
    public async Task A_network_failure_is_reported_without_the_original_message()
    {
        using var folder = new TempFolder();
        var file = folder.Write("access-url", "https://someuser:s3cr3t-value@bridge.example/simplefin");
        var handler = new StubHandler(_ => throw new HttpRequestException("could not reach someuser:s3cr3t-value@bridge.example"));
        var feed = new SimpleFinFeed(new HttpClient(handler), new SimpleFinAccess(file), Clock);

        var failure = await Assert.ThrowsAsync<FeedUnavailableException>(
            () => feed.FetchAsync(new DateRange(DateOnly.Parse("2026-11-11"), DateOnly.Parse("2026-11-25")), Ct));

        AssertNoSecret(failure);
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData("http://someuser:s3cr3t-value@bridge.example/simplefin")]
    [InlineData("https://bridge.example/simplefin")]
    [InlineData("s3cr3t-value")]
    public void A_malformed_access_url_is_rejected_without_being_echoed(string content)
    {
        var failure = Assert.Throws<FeedUnavailableException>(() => SimpleFinAccess.Split(content));

        AssertNoSecret(failure);
        Assert.DoesNotContain("bridge.example", failure.Message);
    }

    [Fact]
    public void The_feed_is_not_configured_until_the_file_exists()
    {
        using var folder = new TempFolder();

        Assert.False(new SimpleFinAccess(null).IsConfigured);
        Assert.False(new SimpleFinAccess(Path.Combine(folder.Path, "missing")).IsConfigured);
        Assert.True(new SimpleFinAccess(folder.Write("access-url", "x")).IsConfigured);
        Assert.Throws<FeedUnavailableException>(() => new SimpleFinAccess(null).Read());
    }

    [Fact]
    public async Task Claiming_a_setup_token_writes_the_access_url_to_a_private_file_once()
    {
        using var folder = new TempFolder();
        var target = Path.Combine(folder.Path, "secrets", "access-url");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("https://someuser:s3cr3t-value@bridge.example/simplefin"),
        });
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("https://bridge.example/simplefin/claim/abc"));

        await SimpleFinClaim.ClaimAsync(new HttpClient(handler), token, target, Ct);

        Assert.Equal("https://bridge.example/simplefin/claim/abc", Assert.Single(handler.Requests).Uri);
        Assert.Equal("https://someuser:s3cr3t-value@bridge.example/simplefin\n", await File.ReadAllTextAsync(target, Ct));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
        }

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => SimpleFinClaim.ClaimAsync(new HttpClient(handler), token, target, Ct));
        AssertNoSecret(again);
    }

    [Fact]
    public async Task A_spent_or_malformed_setup_token_is_refused()
    {
        using var folder = new TempFolder();
        var target = Path.Combine(folder.Path, "access-url");
        var spent = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("https://bridge.example/simplefin/claim/abc"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SimpleFinClaim.ClaimAsync(new HttpClient(spent), token, target, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SimpleFinClaim.ClaimAsync(new HttpClient(spent), "not a token", target, Ct));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task A_saved_response_replays_under_the_live_feed_name()
    {
        using var folder = new TempFolder();
        var feed = new SavedPullFeed(folder.Write("pull.json", Response), Clock);

        var snapshot = await feed.FetchAsync(new DateRange(DateOnly.Parse("2026-11-11"), DateOnly.Parse("2026-11-25")), Ct);

        Assert.Equal("simplefin", feed.Name);
        Assert.Equal(3, snapshot.Accounts[0].Transactions.Count);
        await Assert.ThrowsAsync<FeedUnavailableException>(() => new SavedPullFeed(Path.Combine(folder.Path, "nope.json"), Clock)
            .FetchAsync(new DateRange(DateOnly.Parse("2026-11-11"), DateOnly.Parse("2026-11-25")), Ct));
    }

    private static void AssertNoSecret(Exception failure)
    {
        Assert.DoesNotContain("s3cr3t-value", failure.ToString());
        Assert.DoesNotContain("someuser", failure.ToString());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Uri, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            return Task.FromResult(respond(request));
        }
    }

    [Theory]
    // The Bridge has been seen naming a connection "MX-MBR-9" on accounts and "MBR-9" on its error.
    [InlineData("MX-MBR-9", "MBR-9")]
    [InlineData("MBR-9", "MX-MBR-9")]
    public void An_error_is_joined_to_its_institution_when_the_id_differs_only_by_a_prefix(string connectionId, string errorId)
    {
        var snapshot = Parse($$"""
            {
              "errlist": [ { "code": "con.auth", "msg": "Needs to be re-authenticated.", "conn_id": "{{errorId}}" } ],
              "connections": [
                { "conn_id": "{{connectionId}}", "name": "Example Bank" },
                { "conn_id": "MX-MBR-19", "name": "Other Bank" }
              ],
              "accounts": []
            }
            """);

        Assert.Equal("Example Bank", Assert.Single(snapshot.Errors).ConnectionName);
    }

    [Fact]
    public void An_error_that_could_belong_to_two_institutions_keeps_its_own_id()
    {
        var snapshot = Parse("""
            {
              "errlist": [ { "code": "con.auth", "msg": "Needs to be re-authenticated.", "conn_id": "MBR-9" } ],
              "connections": [
                { "conn_id": "MX-MBR-9", "name": "Example Bank" },
                { "conn_id": "FI-MBR-9", "name": "Other Bank" }
              ],
              "accounts": []
            }
            """);

        Assert.Equal("MBR-9", Assert.Single(snapshot.Errors).ConnectionName);
    }

    private static FeedSnapshot Parse(string json) =>
        SimpleFinParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)), Clock.ToLocalDate);
}
