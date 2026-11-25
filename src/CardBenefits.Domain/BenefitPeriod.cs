namespace CardBenefits.Domain;

/// <summary>
/// One period of one benefit on one card. The amount used is always the sum of the
/// usage entries; it is never stored, so it cannot drift.
/// </summary>
public sealed class BenefitPeriod
{
    private readonly List<UsageEntry> _usage = [];

    public BenefitPeriod(Guid id, Guid cardId, string benefitId, DateRange range, Money amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benefitId);
        if (amount <= Money.Zero)
        {
            throw new ArgumentException("A period amount must be positive.", nameof(amount));
        }

        Id = id;
        CardId = cardId;
        BenefitId = benefitId;
        Range = range;
        Amount = amount;
    }

    public Guid Id { get; }

    public Guid CardId { get; }

    public string BenefitId { get; }

    public DateRange Range { get; }

    public Money Amount { get; }

    public IReadOnlyList<UsageEntry> Usage => _usage;

    public Money Used => Money.Sum(_usage.Select(entry => entry.Amount));

    public Money Remaining => Amount - Used;

    public static BenefitPeriod From(PeriodSpec spec, Guid cardId, string benefitId) =>
        new(Guid.NewGuid(), cardId, benefitId, spec.Range, spec.Amount);

    public UsageEntry Use(Money amount, UsageSource source, Guid? transactionId = null, string? note = null)
    {
        if (amount <= Money.Zero)
        {
            throw new ArgumentException("Usage must be a positive amount.", nameof(amount));
        }

        if (source == UsageSource.Automatic && transactionId is null)
        {
            throw new ArgumentException("Automatic usage must name its transaction.", nameof(transactionId));
        }

        if (amount > Remaining)
        {
            throw new InvalidOperationException($"{amount} is more than the {Remaining} left in this period.");
        }

        var entry = new UsageEntry(amount, source, transactionId, note);
        _usage.Add(entry);
        return entry;
    }

    /// <summary>Removes matched usage ahead of a replay. Manual entries stay.</summary>
    public void ClearAutomaticUsage() => _usage.RemoveAll(entry => entry.Source == UsageSource.Automatic);

    public PeriodStatus StatusOn(DateOnly today)
    {
        if (Remaining == Money.Zero)
        {
            return PeriodStatus.Used;
        }

        if (today < Range.Start)
        {
            return PeriodStatus.Upcoming;
        }

        if (today > Range.End)
        {
            return PeriodStatus.Expired;
        }

        return Used > Money.Zero ? PeriodStatus.PartlyUsed : PeriodStatus.Open;
    }
}
