namespace CardBenefits.Domain;

/// <summary>Where a balance reading came from.</summary>
public enum BalanceSource
{
    /// <summary>Read from the program's own site by a tool outside the tracker.</summary>
    Automation,

    /// <summary>Typed in by a person.</summary>
    Manual,
}

/// <summary>
/// One reading of a person's miles or points in a loyalty program: how many there were
/// at a moment, and the day they lapse when the program gives one. A balance belongs to
/// a person and a program, not to a card.
/// </summary>
public sealed record ProgramBalance
{
    public ProgramBalance(
        Guid personId, string program, long amount, DateTimeOffset asOf, BalanceSource source, DateOnly? expiresOn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        PersonId = personId;
        Program = program.Trim();
        Amount = amount;
        AsOf = asOf;
        Source = source;
        ExpiresOn = expiresOn;
    }

    public Guid PersonId { get; }

    public string Program { get; }

    public long Amount { get; }

    public DateTimeOffset AsOf { get; }

    public BalanceSource Source { get; }

    public DateOnly? ExpiresOn { get; }

    /// <summary>Days until the balance lapses, or null when nothing is at stake: no date, or nothing to lose.</summary>
    public int? DaysUntilExpiry(DateOnly today) =>
        ExpiresOn is null || Amount == 0 ? null : ExpiresOn.Value.DayNumber - today.DayNumber;

    /// <summary>Program names are compared without regard to case, so a retyped name finds the same balance.</summary>
    public bool IsFor(Guid personId, string program) =>
        PersonId == personId && Program.Equals(program.Trim(), StringComparison.OrdinalIgnoreCase);
}
