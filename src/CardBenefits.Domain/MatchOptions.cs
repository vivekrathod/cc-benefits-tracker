namespace CardBenefits.Domain;

public sealed class MatchOptions
{
    public const int DefaultGraceDays = 5;

    /// <summary>Descriptions containing any of these are skipped outright, for example card payments.</summary>
    public IReadOnlyCollection<string> IgnoreKeywords { get; init; } = [];

    /// <summary>
    /// How many days after a period ends a credit may still count for it, per benefit id.
    /// Benefits not listed use <see cref="DefaultGraceDays"/>.
    /// </summary>
    public Dictionary<string, int> GraceDays { get; init; } = [];

    /// <summary>
    /// A person's decisions, by transaction id: the benefit the transaction belongs to,
    /// or null when it is not a benefit at all. Pins come before every rule.
    /// </summary>
    public Dictionary<Guid, string?> Pins { get; init; } = [];
}
