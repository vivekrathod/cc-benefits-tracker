using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure.Notifications;

/// <summary>
/// Reminders through the Gmail API, sent as the account that approved it. The token file is the
/// one "udaan gmail-login --send" writes: it can send and can read nothing. It is read each time,
/// so a new sign-in takes effect without a restart, and nothing from it is ever logged.
/// </summary>
internal sealed class GmailNotifier(HttpClient http, GmailOptions options, IReadOnlyList<string> recipients) : INotifier
{
    private const string SendUrl = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private const string DefaultTokenUrl = "https://oauth2.googleapis.com/token";

    private string? _accessToken;
    private string? _accessTokenFor;
    private DateTimeOffset _accessTokenUntil;

    /// <summary>True once the token file is there. Until then reminders go another way.</summary>
    public bool IsReady => !string.IsNullOrWhiteSpace(options.TokenFile) && File.Exists(options.TokenFile);

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var token = SavedToken.Read(options.TokenFile!);
        var to = recipients.Count > 0 ? recipients : token.Account is null ? [] : [token.Account];
        if (to.Count == 0)
        {
            throw new InvalidOperationException(
                "There is no address to send reminders to. Set REMINDER_TO_1, or sign in again with \"udaan gmail-login --send --email <address>\".");
        }

        var access = await AccessTokenAsync(token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, SendUrl)
        {
            Content = JsonContent.Create(new { raw = Base64Url(Compose(notification, to, token.Account)) }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _accessToken = null;
            throw new InvalidOperationException($"Gmail did not take the reminder ({(int)response.StatusCode}).");
        }
    }

    /// <summary>The message as mail servers read it. Subject and body are encoded, so any character survives.</summary>
    internal static string Compose(Notification notification, IReadOnlyList<string> to, string? from)
    {
        var message = new StringBuilder();
        if (from is not null)
        {
            message.Append("From: Chitragupta <").Append(from).Append(">\r\n");
        }

        message.Append("To: ").AppendJoin(", ", to).Append("\r\n");
        message.Append("Subject: =?UTF-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(notification.Subject))).Append("?=\r\n");
        message.Append("MIME-Version: 1.0\r\n");
        message.Append("Content-Type: text/plain; charset=utf-8\r\n");
        message.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        message.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(notification.Body), Base64FormattingOptions.InsertLineBreaks));
        return message.ToString();
    }

    private async Task<string> AccessTokenAsync(SavedToken token, CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenFor == token.RefreshToken && DateTimeOffset.UtcNow < _accessTokenUntil)
        {
            return _accessToken;
        }

        using var response = await http.PostAsync(
            token.TokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["client_id"] = token.ClientId,
                ["client_secret"] = token.ClientSecret,
            }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google did not renew the Gmail sign-in ({(int)response.StatusCode}). Where udaan runs, run \"udaan gmail-login --send\" again.");
        }

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var seconds = answer.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var given) ? given : 300;
        _accessToken = answer.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google answered without an access token.");
        _accessTokenFor = token.RefreshToken;
        _accessTokenUntil = DateTimeOffset.UtcNow.AddSeconds(seconds - 60);
        return _accessToken;
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The parts of google-auth's "authorized user" file that sending needs.</summary>
    private sealed record SavedToken(string RefreshToken, string ClientId, string ClientSecret, Uri TokenUrl, string? Account)
    {
        public static SavedToken Read(string file)
        {
            try
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(file));
                var root = saved.RootElement;
                string? Text(string name) =>
                    root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
                        ? text
                        : null;

                return new SavedToken(
                    Text("refresh_token") ?? throw new JsonException(),
                    Text("client_id") ?? throw new JsonException(),
                    Text("client_secret") ?? throw new JsonException(),
                    new Uri(Text("token_uri") ?? DefaultTokenUrl),
                    Text("account"));
            }
            catch (Exception unreadable) when (unreadable is JsonException or IOException or UnauthorizedAccessException or UriFormatException)
            {
                // The type says enough. Nothing from the file goes into a message.
                throw new InvalidOperationException($"The Gmail token file could not be read ({unreadable.GetType().Name}).");
            }
        }
    }
}
