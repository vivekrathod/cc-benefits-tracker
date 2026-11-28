using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure.Balances;

/// <summary>
/// Asks "udaan serve" for checks over HTTP. udaan runs on the desktop, where the programs' sites
/// accept its browser, and answers on that machine only. The token is read from its file each
/// time and is never logged. What comes back is udaan's own result, read by <see cref="UdaanBalanceParser"/>.
/// </summary>
internal sealed class UdaanChecker(HttpClient http, UdaanOptions options, IClock clock) : IBalanceChecker
{
    private static readonly TimeSpan QuickAnswer = TimeSpan.FromSeconds(10);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Url);

    public async Task<IReadOnlyList<CheckerAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        using var quick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        quick.CancelAfter(QuickAnswer);

        using var request = Request(HttpMethod.Get, "accounts");
        using var response = await SendAsync(request, quick.Token, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw Refused(response.StatusCode);
        }

        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return body.RootElement.GetProperty("accounts").EnumerateArray()
                .Select(account => new CheckerAccount(
                    account.GetProperty("account").GetString() ?? throw new JsonException(),
                    account.TryGetProperty("program", out var program) ? program.GetString() ?? "" : ""))
                .ToArray();
        }
        catch (Exception odd) when (odd is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new BalanceCheckerUnavailableException("udaan answered something that is not a list of accounts.", reached: true);
        }
    }

    public async Task<BalanceCheckAnswer> CheckAsync(string account, TimeSpan maxAge, bool asked, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "checks");
        request.Content = JsonContent.Create(new { account, max_age = (long)maxAge.TotalSeconds, asked });

        using var response = await SendAsync(request, cancellationToken, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => BalanceCheckAnswer.Result(body),
            // "Not now": another sign-in is running or ended too recently. udaan says when to come back, and which it is.
            HttpStatusCode.TooManyRequests => BalanceCheckAnswer.ComeBackAt(
                clock.Now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)), WhyWait(body)),
            var other => throw Refused(other),
        };
    }

    private static BalanceWait WhyWait(string body)
    {
        try
        {
            using var said = JsonDocument.Parse(body);
            return said.RootElement.ValueKind == JsonValueKind.Object
                && said.RootElement.TryGetProperty("wait_reason", out var reason)
                && reason.ValueKind == JsonValueKind.String
                    ? reason.GetString() switch
                    {
                        "busy" => BalanceWait.Busy,
                        "gap" => BalanceWait.Spacing,
                        _ => BalanceWait.Unsaid,
                    }
                    : BalanceWait.Unsaid;
        }
        catch (JsonException)
        {
            return BalanceWait.Unsaid;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        string token;
        try
        {
            token = File.ReadAllText(options.TokenFile ?? "").Trim();
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new BalanceCheckerUnavailableException(
                "The token for udaan's service is not in place. Where udaan runs, run \"udaan token --copy-to <the secrets folder>/udaan-token\".");
        }

        var request = new HttpRequestMessage(method, $"{options.Url!.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken limit, CancellationToken caller)
    {
        try
        {
            return await http.SendAsync(request, limit);
        }
        catch (HttpRequestException)
        {
            throw new BalanceCheckerUnavailableException("udaan's service did not answer. Where udaan runs, see that it is running.");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            throw new BalanceCheckerUnavailableException("udaan's service took too long to answer.", reached: true);
        }
    }

    private static BalanceCheckerUnavailableException Refused(HttpStatusCode status) => new(
        status == HttpStatusCode.Unauthorized
            ? "udaan's service did not accept the token. Where udaan runs, run \"udaan token --copy-to <the secrets folder>/udaan-token\" again."
            : $"udaan's service answered {(int)status}.",
        reached: true);
}
