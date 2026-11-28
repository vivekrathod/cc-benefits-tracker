using System.Net;
using System.Text;
using System.Text.Json;
using Chitragupta.Application.Ports;
using Chitragupta.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chitragupta.Infrastructure.Tests;

/// <summary>Reminders through the Gmail API. The token, the client and the addresses are all made up.</summary>
public sealed class GmailNotifierTests : IDisposable
{
    private const string Token = """
        { "token": "old-access", "refresh_token": "made-up-refresh", "token_uri": "https://oauth2.example/token",
          "client_id": "made-up-client", "client_secret": "made-up-secret", "scopes": ["https://www.googleapis.com/auth/gmail.send"],
          "account": "home@example.com", "expiry": "2026-11-27T00:00:00Z" }
        """;

    private readonly TempFolder _folder = new();
    private readonly StubHttp _google = new((request, _) =>
        request.RequestUri!.Host == "oauth2.example"
            ? Json(HttpStatusCode.OK, """{ "access_token": "fresh-access", "expires_in": 3599 }""")
            : Json(HttpStatusCode.OK, """{ "id": "1" }"""));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task A_reminder_is_sent_as_the_account_with_a_renewed_sign_in()
    {
        var notifier = Notifier(_google, ["alex@example.com", "sam@example.com"]);

        await notifier.SendAsync(new Notification("Balance check needs you: alex-aa", "Needs you:\n- alex-aa: café closed\n"), Ct);

        Assert.Equal(2, _google.Requests.Count);
        var renewal = _google.Requests[0];
        Assert.Equal("https://oauth2.example/token", renewal.Url.ToString());
        Assert.Contains("grant_type=refresh_token", renewal.Body);
        Assert.Contains("refresh_token=made-up-refresh", renewal.Body);

        var send = _google.Requests[1];
        Assert.Equal(("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", "Bearer fresh-access"), (send.Url.ToString(), send.Authorization));
        var message = Decode(send.Body);
        Assert.StartsWith("From: Chitragupta <home@example.com>\r\nTo: alex@example.com, sam@example.com\r\nSubject: =?UTF-8?B?", message);
        Assert.Contains("Content-Type: text/plain; charset=utf-8\r\n", message);
        var body = message[(message.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        Assert.Equal("Needs you:\n- alex-aa: café closed\n", Encoding.UTF8.GetString(Convert.FromBase64String(body)));
    }

    [Fact]
    public async Task With_no_address_set_the_reminder_goes_to_the_account_itself()
    {
        await Notifier(_google, []).SendAsync(new Notification("Hello", "Body"), Ct);

        Assert.Contains("\r\nTo: home@example.com\r\n", Decode(_google.Requests[1].Body));
    }

    [Fact]
    public async Task The_renewed_sign_in_is_used_again_until_it_runs_out()
    {
        var notifier = Notifier(_google, []);

        await notifier.SendAsync(new Notification("One", "Body"), Ct);
        await notifier.SendAsync(new Notification("Two", "Body"), Ct);

        Assert.Equal(1, _google.Requests.Count(request => request.Url.Host == "oauth2.example"));
    }

    [Fact]
    public async Task A_sign_in_google_will_not_renew_says_what_to_do_and_gives_nothing_away()
    {
        var google = new StubHttp((_, _) => Json(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }"""));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Notifier(google, []).SendAsync(new Notification("Hello", "Body"), Ct));

        Assert.Contains("udaan gmail-login --send", failure.Message);
        Assert.DoesNotContain("made-up", failure.Message);
    }

    [Fact]
    public async Task A_token_file_that_cannot_be_read_says_so_without_quoting_it()
    {
        var notifier = new GmailNotifier(
            _google.Client(), new GmailOptions { TokenFile = _folder.Write("broken.json", """{ "refresh_token": "made-up-refresh" """) }, []);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => notifier.SendAsync(new Notification("Hello", "Body"), Ct));

        Assert.DoesNotContain("made-up", failure.Message);
        Assert.Empty(_google.Requests);
    }

    [Fact]
    public async Task Reminders_go_through_gmail_from_the_moment_its_token_file_is_there()
    {
        var file = Path.Combine(_folder.Path, "gmail-send-token.json");
        var gmail = new GmailNotifier(_google.Client(), new GmailOptions { TokenFile = file }, []);
        var notifier = new FirstReadyNotifier(gmail, null, new LogNotifier(NullLogger<LogNotifier>.Instance));

        await notifier.SendAsync(new Notification("Before", "Body"), Ct);
        Assert.Empty(_google.Requests);

        File.WriteAllText(file, Token);
        await notifier.SendAsync(new Notification("After", "Body"), Ct);
        Assert.Equal(2, _google.Requests.Count);
    }

    private GmailNotifier Notifier(StubHttp google, string[] recipients) =>
        new(google.Client(), new GmailOptions { TokenFile = _folder.Write("gmail-send-token.json", Token) }, recipients);

    private static string Decode(string sendBody)
    {
        using var json = JsonDocument.Parse(sendBody);
        var raw = json.RootElement.GetProperty("raw").GetString()!.Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(raw.PadRight(raw.Length + ((4 - (raw.Length % 4)) % 4), '=')));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
