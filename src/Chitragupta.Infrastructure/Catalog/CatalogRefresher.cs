using Chitragupta.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Chitragupta.Infrastructure.Catalog;

/// <summary>
/// Keeps the catalog in step with the community source: every few days it downloads the
/// dataset, converts it, and takes it into use. Whatever goes wrong on the way, the
/// catalog in use stays as it was and the problem is reported instead.
/// </summary>
public sealed class CatalogRefresher(
    HttpClient http, LiveCatalog catalog, ITrackerStore store, IClock clock, TrackerOptions options, ILogger<CatalogRefresher> logger)
{
    private const int LargestDatasetBytes = 20 * 1024 * 1024;

    // After a failed attempt the source is left alone for a while instead of being asked every few minutes.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(6);

    public bool IsEnabled => catalog.FollowsSource && options.Catalog.RefreshDays > 0;

    /// <summary>Refreshes when the last copy is old enough. True when the catalog changed.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var now = clock.Now;
        var retrieved = catalog.Source.RetrievedAt;
        var due = retrieved is null || now - retrieved >= TimeSpan.FromDays(options.Catalog.RefreshDays);
        var waiting = catalog.FailedAt is { } failed && now - failed < RetryAfter;
        return due && !waiting && await RunAsync(cancellationToken);
    }

    /// <summary>Refreshes now. True when the catalog changed; a failure is recorded in the status, not thrown.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (!catalog.FollowsSource)
        {
            return false;
        }

        var now = clock.Now;
        try
        {
            var previous = catalog.Source;
            var fresh = await DownloadAsync(cancellationToken) with { RetrievedAt = now };

            var before = previous.Products.Count(product => product.GoneFromSource is null);
            if (fresh.Products.Count == 0 || fresh.Products.Count < before / 2)
            {
                throw new InvalidDataException(
                    $"The source lists {fresh.Products.Count} cards where it had {before}. That looks broken, so it was not taken.");
            }

            var cards = await store.GetCardsAsync(cancellationToken);
            var periods = await store.GetPeriodsAsync(null, cancellationToken);
            var kept = CatalogPreserver.Keep(
                previous,
                fresh,
                cards.SelectMany(card => card.Products).Select(assignment => assignment.ProductId).ToHashSet(),
                periods.Where(period => period.Usage.Count > 0).Select(period => period.BenefitId).ToHashSet(),
                clock.Today);

            catalog.Replace(kept, now);

            var changed = (kept with { RetrievedAt = null }).ToJson() != (previous with { RetrievedAt = null }).ToJson();
            logger.LogInformation(
                "Catalog refreshed from the source: {Products} card products, {Outcome}, {Flags} to look at.",
                kept.Products.Count, changed ? "changed" : "unchanged", catalog.Status.Flags.Count);
            return changed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // The dataset is someone else's file on someone else's server: any way it can fail
            // ends here, with the catalog in use untouched.
            catalog.RecordFailure(now, $"{failure.GetType().Name}: {failure.Message}");
            logger.LogWarning("The catalog refresh failed and the catalog in use stays: {Type}: {Message}", failure.GetType().Name, failure.Message);
            return false;
        }
    }

    private async Task<CatalogFile> DownloadAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(options.Catalog.SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var dataset = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (dataset.Length + read > LargestDatasetBytes)
            {
                throw new InvalidDataException("The source file is larger than 20 MB, far beyond what it has ever been.");
            }

            dataset.Write(buffer, 0, read);
        }

        dataset.Position = 0;
        return DatasetImporter.Read(dataset);
    }
}
