using System.Net;
using Chitragupta.Application.Feeds;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Infrastructure.Feeds.SimpleFin;

/// <summary>The live feed: one GET to the SimpleFIN Bridge per sync.</summary>
internal sealed class SimpleFinFeed(HttpClient http, SimpleFinAccess access, IClock clock) : ITransactionFeed
{
    public const string FeedName = "simplefin";

    public string Name => FeedName;

    public bool IsConfigured => access.IsConfigured;

    public async Task<FeedSnapshot> FetchAsync(DateRange window, CancellationToken cancellationToken)
    {
        var (endpoint, credentials) = access.Read();

        // start-date is inclusive and end-date exclusive, both in epoch seconds. The window is
        // in local days, so it is widened to whole UTC days that are sure to cover it.
        var start = Epoch(window.Start);
        var end = Epoch(window.End.AddDays(2));
        var url = $"{endpoint.AbsoluteUri.TrimEnd('/')}/accounts?version=2&pending=1&start-date={start}&end-date={end}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = credentials;

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new FeedUnavailableException(response.StatusCode switch
                {
                    HttpStatusCode.PaymentRequired => "The SimpleFIN Bridge says the subscription needs payment (402).",
                    HttpStatusCode.Forbidden =>
                        "The SimpleFIN Bridge refused the access URL (403). It may have been revoked or disabled; create a new app connection.",
                    _ => $"The SimpleFIN Bridge answered {(int)response.StatusCode}.",
                });
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return SimpleFinParser.Parse(body, clock.ToLocalDate);
        }
        catch (Exception failure) when (failure is HttpRequestException or System.Text.Json.JsonException or IOException
            || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The type says enough. The original message is left out on purpose: it is not
            // worth the chance of it ever carrying part of the request.
            throw new FeedUnavailableException($"The SimpleFIN Bridge could not be read ({failure.GetType().Name}).");
        }
    }

    private static long Epoch(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}
