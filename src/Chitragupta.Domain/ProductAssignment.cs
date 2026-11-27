namespace Chitragupta.Domain;

/// <summary>The card product an account has been on since a date.</summary>
public sealed record ProductAssignment(string ProductId, DateOnly From);
