using Chitragupta.Application.Feeds;

namespace Chitragupta.Application.Ports;

/// <summary>Reads an issuer's CSV export into transactions, with credits to the card positive.</summary>
public interface IStatementParser
{
    /// <summary>Thrown as <see cref="RequestRejectedException"/> when the file cannot be understood.</summary>
    IReadOnlyList<FeedTransaction> Parse(string csv);
}
