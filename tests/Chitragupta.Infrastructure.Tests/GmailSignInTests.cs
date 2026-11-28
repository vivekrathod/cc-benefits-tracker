using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Chitragupta.Application;
using Chitragupta.Application.Ports;
using Chitragupta.Infrastructure.Notifications;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>Google's consent for sending mail. The client, the codes and the addresses are all made up.</summary>
public sealed class GmailSignInTests : IDisposable
{
    private const string DesktopClient = """
        { "installed": { "client_id": "made-up-client", "client_secret": "made-up-secret",
          "auth_uri": "https://accounts.example/auth", "token_uri": "https://oauth2.example/token" } }
        """;

    private static readonly Uri Home = new("http://localhost:8090/");

    private readonly TempFolder _folder = new();
    private readonly StubHttp _google = new((request, _) =>
        request.RequestUri!.Host == "oauth2.example"
            ? Json(HttpStatusCode.OK, $$"""{ "access_token": "fresh-access", "expires_in": 3599, "refresh_token": "made-up-refresh", "id_token": "{{IdToken("home@example.com")}}" }""")
            : Json(HttpStatusCode.OK, """{ "id": "1" }"""));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string TokenFile => Path.Combine(_folder.Path, "data", "gmail-token.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Without_a_google_client_there_is_nothing_to_set_up()
    {
        var signIn = SignIn(client: null);

        Assert.Equal(new EmailSignInState(false, false, null), signIn.Read());
        Assert.Throws<RequestRejectedException>(() => signIn.Begin(Home));
    }

    [Fact]
    public void The_browser_is_sent_to_google_to_approve_sending_and_nothing_that_reads_mail()
    {
        var signIn = SignIn();

        var ask = signIn.Begin(new Uri("http://localhost:8090/?tab=1#top"));

        var query = HttpUtility.ParseQueryString(ask.Query);
        Assert.Equal("https://accounts.example/auth", ask.GetLeftPart(UriPartial.Path));
        Assert.Equal(("made-up-client", "http://localhost:8090/", "code", "offline", "S256"),
            (query["client_id"], query["redirect_uri"], query["response_type"], query["access_type"], query["code_challenge_method"]));
        Assert.Equal(["https://www.googleapis.com/auth/gmail.send", "openid", "email"], query["scope"]!.Split(' '));
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.DoesNotContain("made-up-secret", ask.ToString());
        Assert.Equal(new EmailSignInState(true, false, null), signIn.Read());
    }

    [Fact]
    public async Task Coming_back_with_the_code_keeps_an_approval_that_reminders_are_sent_with()
    {
        var signIn = SignIn();
        var state = HttpUtility.ParseQueryString(signIn.Begin(Home).Query)["state"]!;

        await signIn.CompleteAsync(state, "made-up-code", Ct);

        var exchange = Assert.Single(_google.Requests);
        Assert.Equal("https://oauth2.example/token", exchange.Url.ToString());
        Assert.Contains("grant_type=authorization_code", exchange.Body);
        Assert.Contains("code=made-up-code", exchange.Body);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8090%2F", exchange.Body);
        Assert.Contains("code_verifier=", exchange.Body);
        Assert.Equal(new EmailSignInState(true, true, "home@example.com"), signIn.Read());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(TokenFile));
        }

        // The file is what the notifier reads: a reminder now goes out as that account, to that account.
        var notifier = new GmailNotifier(_google.Client(), new GmailOptions { TokenFile = TokenFile }, []);
        Assert.True(notifier.IsReady);
        await notifier.SendAsync(new Notification("Hello", "Body"), Ct);
        Assert.Contains("refresh_token=made-up-refresh", _google.Requests[1].Body);
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", _google.Requests[2].Url.ToString());
    }

    [Fact]
    public async Task A_code_for_a_sign_in_that_was_not_started_here_is_refused_and_ends_the_one_that_was()
    {
        var signIn = SignIn();
        var state = HttpUtility.ParseQueryString(signIn.Begin(Home).Query)["state"]!;

        await Assert.ThrowsAsync<RequestRejectedException>(() => signIn.CompleteAsync("somebody-elses", "made-up-code", Ct));
        await Assert.ThrowsAsync<RequestRejectedException>(() => signIn.CompleteAsync(state, "made-up-code", Ct));

        Assert.Empty(_google.Requests);
        Assert.False(signIn.Read().SignedIn);
    }

    [Fact]
    public async Task A_sign_in_google_refuses_keeps_nothing_and_gives_nothing_away()
    {
        var google = new StubHttp((_, _) => Json(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }"""));
        var signIn = SignIn(google: google);
        var state = HttpUtility.ParseQueryString(signIn.Begin(Home).Query)["state"]!;

        var failure = await Assert.ThrowsAsync<RequestRejectedException>(() => signIn.CompleteAsync(state, "made-up-code", Ct));

        Assert.DoesNotContain("made-up", failure.Message);
        Assert.False(signIn.Read().SignedIn);
    }

    [Fact]
    public void A_desktop_client_is_only_started_from_the_computer_google_will_send_it_back_to()
    {
        var failure = Assert.Throws<RequestRejectedException>(() => SignIn().Begin(new Uri("http://home-server:8090/")));

        Assert.Contains("http://localhost", failure.Message);
    }

    [Fact]
    public void A_web_client_may_come_back_to_any_address_it_was_registered_with()
    {
        var signIn = SignIn("""{ "web": { "client_id": "made-up-client", "client_secret": "made-up-secret" } }""");

        var ask = signIn.Begin(new Uri("https://home.example/"));

        Assert.Equal("https://accounts.google.com/o/oauth2/auth", ask.GetLeftPart(UriPartial.Path));
        Assert.Equal("https://home.example/", HttpUtility.ParseQueryString(ask.Query)["redirect_uri"]);
    }

    [Fact]
    public async Task Forgetting_the_approval_stops_mail_going_out_this_way()
    {
        var signIn = SignIn();
        await signIn.CompleteAsync(HttpUtility.ParseQueryString(signIn.Begin(Home).Query)["state"]!, "made-up-code", Ct);

        signIn.Forget();
        signIn.Forget();

        Assert.Equal(new EmailSignInState(true, false, null), signIn.Read());
        Assert.False(new GmailNotifier(_google.Client(), new GmailOptions { TokenFile = TokenFile }, []).IsReady);
    }

    private GmailSignIn SignIn(string? client = DesktopClient, StubHttp? google = null) => new(
        (google ?? _google).Client(),
        new GmailOptions { ClientFile = client is null ? null : _folder.Write("google-oauth-client.json", client), TokenFile = TokenFile });

    private static string IdToken(string email) =>
        $"e30.{Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email }))).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.c2ln";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
