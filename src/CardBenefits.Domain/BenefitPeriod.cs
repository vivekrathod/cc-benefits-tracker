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
        if (amount < Money.Zero)
        {
            throw new ArgumentException("A period amount cannot be negative.", nameof(amount));
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

    /// <summary>A perk such as a lounge pass has no dollar value: it is simply used or not.</summary>
    public bool HasDollarValue => Amount > Money.Zero;

    public bool IsUsed => HasDollarValue ? Remaining == Money.Zero : _usage.Count > 0;

    public static BenefitPeriod From(PeriodSpec spec, Guid cardId, string benefitId) =>
        new(Guid.NewGuid(), cardId, benefitId, spec.Range, spec.Amount);

    /// <summary>Rebuilds a stored period with the usage it had.</summary>
    public static BenefitPeriod Restore(
        Guid id, Guid cardId, string benefitId, DateRange range, Money amount, IEnumerable<UsageEntry> usage)
    {
        var period = new BenefitPeriod(id, cardId, benefitId, range, amount);
        period._usage.AddRange(usage);
        if (period.Used > amount)
        {
            throw new InvalidOperationException($"Stored usage {period.Used} exceeds the period amount {amount}.");
        }

        return period;
    }

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

    /// <summary>A person marks the period used: everything that remains, or the perk itself.</summary>
    public UsageEntry MarkUsed(string? note = null)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("This period is already used.");
        }

        var entry = new UsageEntry(Remaining, UsageSource.Manual, null, note);
        _usage.Add(entry);
        return entry;
    }

    /// <summary>Removes matched usage ahead of a replay. Manual entries stay.</summary>
    public void ClearAutomaticUsage() => _usage.RemoveAll(entry => entry.Source == UsageSource.Automatic);

    /// <summary>Undoes what a person entered. Matched usage stays.</summary>
    public void ClearManualUsage() => _usage.RemoveAll(entry => entry.Source == UsageSource.Manual);

    public PeriodStatus StatusOn(DateOnly today)
    {
        if (IsUsed)
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
