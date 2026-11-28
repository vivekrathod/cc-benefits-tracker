using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chitragupta.Application;
using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure.Notifications;

/// <summary>
/// Google's consent for sending mail, and nothing else: the token it ends in can send as the
/// account and cannot read a message. The token is kept in the data folder, in the shape
/// <see cref="GmailNotifier"/> reads. Nothing from the client or the token is ever logged or
/// put in a message.
/// </summary>
internal sealed class GmailSignIn(HttpClient http, GmailOptions options) : IEmailSignIn
{
    private const string SendScope = "https://www.googleapis.com/auth/gmail.send";
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private Pending? _pending;

    public EmailSignInState Read()
    {
        var signedIn = File.Exists(options.TokenFile);
        return new EmailSignInState(GoogleClient.TryRead(options.ClientFile) is not null, signedIn, signedIn ? SavedAccount() : null);
    }

    public Uri Begin(Uri returnTo)
    {
        var client = GoogleClient.TryRead(options.ClientFile)
            ?? throw new RequestRejectedException("No Google client is set up for the sign-in. The README says where its file goes.");
        if (client.Installed && !returnTo.IsLoopback)
        {
            throw new RequestRejectedException(
                "Set this up from the computer that runs Chitragupta, with the app opened as http://localhost and its port. Google sends this kind of sign-in back only to that address.");
        }

        // One approval at a time: starting again forgets the one before.
        var pending = new Pending(Random(16), Random(32), returnTo.GetLeftPart(UriPartial.Path), DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _pending = pending;
        }

        var query = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = pending.ReturnTo,
            ["response_type"] = "code",
            // The address comes with the approval, so reminders can default to it. No scope here reads mail.
            ["scope"] = $"{SendScope} openid email",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = pending.State,
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(pending.Verifier))),
            ["code_challenge_method"] = "S256",
        };
        return new UriBuilder(client.AuthUrl) { Query = string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")) }.Uri;
    }

    public async Task CompleteAsync(string state, string code, CancellationToken cancellationToken)
    {
        Pending? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is null
            || DateTimeOffset.UtcNow - pending.StartedAt > Patience
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pending.State), Encoding.UTF8.GetBytes(state)))
        {
            throw new RequestRejectedException("That sign-in was not started here, or was started too long ago. Start it again.");
        }

        var client = GoogleClient.TryRead(options.ClientFile)
            ?? throw new RequestRejectedException("The Google client's file is gone. Put it back and start again.");
        using var response = await http.PostAsync(
            client.TokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = pending.ReturnTo,
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["code_verifier"] = pending.Verifier,
            }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new RequestRejectedException($"Google did not finish the sign-in ({(int)response.StatusCode}). Start it again.");
        }

        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var refresh = Text(answer.RootElement, "refresh_token")
            ?? throw new RequestRejectedException("Google approved the sign-in without a way to renew it. Start it again.");

        Save(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["refresh_token"] = refresh,
            ["token_uri"] = client.TokenUrl.ToString(),
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["scopes"] = new[] { SendScope },
            ["account"] = EmailIn(Text(answer.RootElement, "id_token")),
        }));
    }

    public void Forget()
    {
        if (!string.IsNullOrWhiteSpace(options.TokenFile))
        {
            File.Delete(options.TokenFile);
        }
    }

    private void Save(string token)
    {
        var file = options.TokenFile ?? throw new RequestRejectedException("There is no place set up to keep the sign-in.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, token);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private string? SavedAccount()
    {
        try
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(options.TokenFile!));
            return Text(saved.RootElement, "account");
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The address inside the identity Google hands over with the token. It came straight from Google, so it is read, not verified.</summary>
    private static string? EmailIn(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var claims = parts[1].Replace('-', '+').Replace('_', '/');
            using var identity = JsonDocument.Parse(Convert.FromBase64String(claims.PadRight(claims.Length + ((4 - (claims.Length % 4)) % 4), '=')));
            return Text(identity.RootElement, "email");
        }
        catch (Exception unreadable) when (unreadable is JsonException or FormatException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static string Random(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Pending(string State, string Verifier, string ReturnTo, DateTimeOffset StartedAt);

    /// <summary>
    /// The OAuth client as Google's console downloads it: an object named "installed" (a desktop
    /// app, which Google only sends back to this computer) or "web".
    /// </summary>
    private sealed record GoogleClient(string ClientId, string ClientSecret, Uri AuthUrl, Uri TokenUrl, bool Installed)
    {
        public static GoogleClient? TryRead(string? file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                return null;
            }

            try
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(file));
                var installed = saved.RootElement.TryGetProperty("installed", out var desktop);
                var client = installed ? desktop : saved.RootElement.TryGetProperty("web", out var web) ? web : saved.RootElement;
                return Text(client, "client_id") is { } id && Text(client, "client_secret") is { } secret
                    ? new GoogleClient(
                        id,
                        secret,
                        new Uri(Text(client, "auth_uri") ?? "https://accounts.google.com/o/oauth2/auth"),
                        new Uri(Text(client, "token_uri") ?? "https://oauth2.googleapis.com/token"),
                        installed)
                    : null;
            }
            catch (Exception unreadable) when (unreadable is JsonException or IOException or UnauthorizedAccessException or UriFormatException)
            {
                return null;
            }
        }
    }
}
