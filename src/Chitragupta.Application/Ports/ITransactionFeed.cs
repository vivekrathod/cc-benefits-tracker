using Chitragupta.Application.Feeds;
using Chitragupta.Domain;

namespace Chitragupta.Application.Ports;

/// <summary>Where transactions come from: the SimpleFIN Bridge, or issuer CSV exports.</summary>
public interface ITransactionFeed
{
    /// <summary>A short stable name stored with every account, for example "simplefin".</summary>
    string Name { get; }

    /// <summary>False while the feed lacks what it needs to run, such as its access URL.</summary>
    bool IsConfigured { get; }

    /// <summary>Thrown as <see cref="FeedUnavailableException"/> when the whole request fails.</summary>
    Task<FeedSnapshot> FetchAsync(DateRange window, CancellationToken cancellationToken);
}

/// <summary>The feed could not be read at all, as opposed to one institution reporting an error.</summary>
public sealed class FeedUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
