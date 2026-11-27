namespace Chitragupta.Domain;

/// <summary>A recurring benefit of a card product, with every set of terms it has had.</summary>
public sealed class BenefitDefinition
{
    public BenefitDefinition(
        string id,
        string productId,
        string name,
        DetectionMode detection,
        IEnumerable<BenefitTerms> terms,
        DateOnly? lastDay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var ordered = terms.OrderBy(version => version.EffectiveFrom).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("A benefit needs at least one set of terms.", nameof(terms));
        }

        if (ordered.DistinctBy(version => version.EffectiveFrom).Count() != ordered.Length)
        {
            throw new ArgumentException("Two sets of terms start on the same day.", nameof(terms));
        }

        if (ordered.Any(version => version.Amount < Money.Zero))
        {
            throw new ArgumentException("A benefit amount cannot be negative.", nameof(terms));
        }

        if (detection == DetectionMode.Automatic && ordered.Any(version => version.Amount == Money.Zero))
        {
            throw new ArgumentException("A benefit detected from statement credits needs a positive amount.", nameof(terms));
        }

        if (lastDay < ordered[0].EffectiveFrom)
        {
            throw new ArgumentException("A benefit cannot end before it starts.", nameof(lastDay));
        }

        Id = id;
        ProductId = productId;
        Name = name;
        Detection = detection;
        Terms = ordered;
        LastDay = lastDay;
    }

    public string Id { get; }

    public string ProductId { get; }

    public string Name { get; }

    public DetectionMode Detection { get; }

    /// <summary>Terms in date order.</summary>
    public IReadOnlyList<BenefitTerms> Terms { get; }

    /// <summary>The last day the benefit can be used, when it has been discontinued.</summary>
    public DateOnly? LastDay { get; }

    public DateRange Lifetime => new(Terms[0].EffectiveFrom, LastDay ?? DateOnly.MaxValue);
}
