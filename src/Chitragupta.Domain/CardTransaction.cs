namespace Chitragupta.Domain;

/// <summary>
/// A transaction on a card as the matcher sees it. A positive amount is money
/// credited to the card (a statement credit, refund or payment).
/// </summary>
public sealed record CardTransaction(
    Guid Id, Guid CardId, string FeedId, DateOnly PostedOn, Money Amount, string Description, bool Pending);
