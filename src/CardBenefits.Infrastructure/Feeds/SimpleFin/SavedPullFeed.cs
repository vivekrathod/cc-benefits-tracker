using CardBenefits.Application.Feeds;
using CardBenefits.Application.Ports;
using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Feeds.SimpleFin;

/// <summary>
/// A SimpleFIN response saved to a file, replayed as if the Bridge had just sent it.
/// It uses the live feed's name, so its accounts are the same accounts the live sync
/// sees afterwards.
/// </summary>
public sealed class SavedPullFeed(string path, IClock clock) : ITransactionFeed
{
    public string Name => SimpleFinFeed.FeedName;

    public bool IsConfigured => File.Exists(path);

    public async Task<FeedSnapshot> FetchAsync(DateRange window, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FeedUnavailableException("The saved pull file does not exist.");
        }

        try
        {
            await using var file = File.OpenRead(path);
            return SimpleFinParser.Parse(file, clock.ToLocalDate);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new FeedUnavailableException("The saved pull file is not valid JSON.");
        }
    }
}
