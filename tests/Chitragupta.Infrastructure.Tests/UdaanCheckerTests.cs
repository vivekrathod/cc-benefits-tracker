using System.Net;
using System.Text;
using Chitragupta.Application.Ports;
using Chitragupta.Infrastructure.Balances;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>The conversation with "udaan serve". Every account, token and number here is made up.</summary>
public sealed class UdaanCheckerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private static readonly FixedClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Lists_the_accounts_udaan_has_and_shows_the_token_from_its_file()
    {
        var udaan = new StubHttp((_, _) => Json(HttpStatusCode.OK, """
            { "accounts": [ { "account": "alex-aa", "program": "aa", "owner": "alex" }, { "account": "alex-united", "program": "united", "owner": "alex" } ],
              "programs": { "aa": "American Airlines AAdvantage" } }
            """));

        var accounts = await Checker(udaan).GetAccountsAsync(Ct);

        Assert.Equal([new CheckerAccount("alex-aa", "aa"), new CheckerAccount("alex-united", "united")], accounts);
        var asked = Assert.Single(udaan.Requests);
        Assert.Equal((HttpMethod.Get, "http://host.docker.internal:8791/accounts", "Bearer made-up-token"), (asked.Method, asked.Url.ToString(), asked.Authorization));
    }

    [Fact]
    public async Task A_check_hands_back_udaans_result_as_it_was_given()
    {
        var udaan = new StubHttp((_, _) => Json(HttpStatusCode.OK, """{ "account": "alex-aa", "status": "ok" }"""));

        var answer = await Checker(udaan).CheckAsync("alex-aa", TimeSpan.FromHours(12.5), Ct);

        Assert.Equal(BalanceCheckAnswer.Result("""{ "account": "alex-aa", "status": "ok" }"""), answer);
        var asked = Assert.Single(udaan.Requests);
        Assert.Equal((HttpMethod.Post, "http://host.docker.internal:8791/checks", """{"account":"alex-aa","max_age":45000}"""), (asked.Method, asked.Url.ToString(), asked.Body));
    }

    [Fact]
    public async Task Not_now_becomes_the_moment_to_come_back()
    {
        var udaan = new StubHttp((_, _) =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, """{ "account": "alex-aa", "status": "wait", "attempted": false }""");
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(840));
            return response;
        });

        var answer = await Checker(udaan).CheckAsync("alex-aa", TimeSpan.Zero, Ct);

        Assert.Equal(BalanceCheckAnswer.ComeBackAt(Clock.Now.AddMinutes(14)), answer);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "did not accept the token")]
    [InlineData(HttpStatusCode.InternalServerError, "answered 500")]
    public async Task An_answer_that_is_not_a_result_is_reported_as_reached_but_unusable(HttpStatusCode status, string says)
    {
        var udaan = new StubHttp((_, _) => Json(status, """{ "ok": false, "status": "usage", "error": "..." }"""));

        var refused = await Assert.ThrowsAsync<BalanceCheckerUnavailableException>(() => Checker(udaan).CheckAsync("alex-aa", TimeSpan.Zero, Ct));

        Assert.True(refused.Reached);
        Assert.Contains(says, refused.Message);
        Assert.DoesNotContain("made-up-token", refused.Message);
    }

    [Fact]
    public async Task A_service_that_is_not_there_is_reported_as_not_answering()
    {
        var udaan = new StubHttp((_, _) => throw new HttpRequestException("Connection refused"));

        var down = await Assert.ThrowsAsync<BalanceCheckerUnavailableException>(() => Checker(udaan).GetAccountsAsync(Ct));

        Assert.False(down.Reached);
        Assert.Contains("did not answer", down.Message);
    }

    [Fact]
    public async Task Without_the_token_file_nothing_is_sent()
    {
        var udaan = new StubHttp((_, _) => Json(HttpStatusCode.OK, "{}"));
        var checker = new UdaanChecker(
            udaan.Client(), new UdaanOptions { Url = "http://host.docker.internal:8791", TokenFile = Path.Combine(_folder.Path, "missing") }, Clock);

        var missing = await Assert.ThrowsAsync<BalanceCheckerUnavailableException>(() => checker.GetAccountsAsync(Ct));

        Assert.Contains("udaan token --copy-to", missing.Message);
        Assert.Empty(udaan.Requests);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("http://host.docker.internal:8791", true)]
    public void It_is_connected_only_when_an_address_is_set(string? url, bool connected)
    {
        using var http = new HttpClient();

        Assert.Equal(connected, new UdaanChecker(http, new UdaanOptions { Url = url }, Clock).IsConfigured);
    }

    private UdaanChecker Checker(StubHttp udaan) => new(
        udaan.Client(),
        new UdaanOptions { Url = "http://host.docker.internal:8791/", TokenFile = _folder.Write("udaan-token", "made-up-token\n") },
        Clock);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
