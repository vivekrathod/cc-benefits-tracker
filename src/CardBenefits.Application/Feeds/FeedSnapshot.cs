using CardBenefits.Domain;

namespace CardBenefits.Application.Feeds;

/// <summary>What a feed returned for one request: accounts with their transactions, and any errors.</summary>
public sealed record FeedSnapshot(IReadOnlyList<FeedAccountData> Accounts, IReadOnlyList<FeedError> Errors);

public sealed record FeedAccountData(
    string ConnectionName, string AccountId, string Name, IReadOnlyList<FeedTransaction> Transactions);

/// <summary>
/// A transaction as a feed reports it. A positive amount is money into the account,
/// which on a card means a payment, refund or statement credit.
/// </summary>
public sealed record FeedTransaction(
    string Id, DateOnly? PostedOn, DateOnly? TransactedOn, Money Amount, string Description, bool Pending, string RawPayload);

/// <summary>A problem the feed reported, tied to a connection when it concerns one institution.</summary>
public sealed record FeedError(string Code, string Message, string? ConnectionName = null);
