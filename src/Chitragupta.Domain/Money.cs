namespace Chitragupta.Domain;

/// <summary>A US dollar amount held as whole cents, so sums never drift.</summary>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    public static Money Zero => new(0);

    public decimal Dollars => Cents / 100m;

    public static Money FromDollars(decimal dollars)
    {
        var cents = dollars * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new ArgumentException($"{dollars} has a fraction of a cent.", nameof(dollars));
        }

        return new Money((long)cents);
    }

    public static Money Min(Money left, Money right) => left <= right ? left : right;

    public static Money Sum(IEnumerable<Money> amounts) => new(amounts.Sum(amount => amount.Cents));

    public static Money operator +(Money left, Money right) => new(left.Cents + right.Cents);

    public static Money operator -(Money left, Money right) => new(left.Cents - right.Cents);

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public static bool operator <=(Money left, Money right) => left.Cents <= right.Cents;

    public static bool operator >=(Money left, Money right) => left.Cents >= right.Cents;

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    public override string ToString()
    {
        var absolute = Math.Abs(Cents);
        return $"{(Cents < 0 ? "-" : "")}${absolute / 100}.{absolute % 100:00}";
    }
}
