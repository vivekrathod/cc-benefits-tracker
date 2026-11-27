namespace Chitragupta.Domain;

/// <summary>One use of part or all of a benefit period.</summary>
public sealed record UsageEntry(Money Amount, UsageSource Source, Guid? TransactionId, string? Note);
