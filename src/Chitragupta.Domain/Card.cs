namespace Chitragupta.Domain;

/// <summary>
/// One card account of one person. It keeps its own identity when the feed's account
/// id changes, and through a product change on the same account.
/// </summary>
public sealed class Card
{
    private readonly List<ProductAssignment> _products = [];

    public Card(
        Guid id, Guid personId, string productId, DateOnly openedOn, Anniversary anniversary, string? lastFour = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        if (lastFour is not null)
        {
            RequireFourDigits(lastFour);
        }

        Id = id;
        PersonId = personId;
        OpenedOn = openedOn;
        Anniversary = anniversary;
        LastFour = lastFour;
        _products.Add(new ProductAssignment(productId, openedOn));
    }

    public Guid Id { get; }

    public Guid PersonId { get; }

    public string? LastFour { get; private set; }

    public DateOnly OpenedOn { get; }

    public DateOnly? ClosedOn { get; private set; }

    /// <summary>The start of the cardmember year. It survives a product change.</summary>
    public Anniversary Anniversary { get; private set; }

    /// <summary>What this card is actually charged a year, when it differs from the product's list fee.</summary>
    public Money? AnnualFeeOverride { get; private set; }

    public IReadOnlyList<ProductAssignment> Products => _products;

    public string CurrentProductId => _products[^1].ProductId;

    public bool IsClosed => ClosedOn is not null;

    /// <summary>The digits the feed shows for this card, learned when an account is linked to it.</summary>
    public void RecordLastFour(string lastFour)
    {
        RequireFourDigits(lastFour);
        LastFour = lastFour;
    }

    private static void RequireFourDigits(string lastFour)
    {
        if (lastFour.Length != 4 || !lastFour.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Last four must be exactly four digits.", nameof(lastFour));
        }
    }

    /// <summary>Rebuilds a stored card with its product history.</summary>
    public static Card Restore(
        Guid id,
        Guid personId,
        string? lastFour,
        DateOnly openedOn,
        DateOnly? closedOn,
        Anniversary anniversary,
        Money? annualFeeOverride,
        IEnumerable<ProductAssignment> products)
    {
        var ordered = products.OrderBy(assignment => assignment.From).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("A card needs at least one product.", nameof(products));
        }

        var card = new Card(id, personId, ordered[0].ProductId, openedOn, anniversary, lastFour)
        {
            ClosedOn = closedOn,
            AnnualFeeOverride = annualFeeOverride,
        };
        card._products.AddRange(ordered.Skip(1));
        return card;
    }

    public void CorrectAnniversary(Anniversary anniversary) => Anniversary = anniversary;

    public void SetAnnualFee(Money? annualFee)
    {
        if (annualFee < Money.Zero)
        {
            throw new ArgumentException("An annual fee cannot be negative.", nameof(annualFee));
        }

        AnnualFeeOverride = annualFee;
    }

    /// <summary>The first day of the cardmember year that contains the date.</summary>
    public DateOnly CardmemberYearStart(DateOnly date) =>
        Anniversary.In(date.Year) <= date ? Anniversary.In(date.Year) : Anniversary.In(date.Year - 1);

    public void ChangeProduct(string productId, DateOnly on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        EnsureOpen();
        if (productId == CurrentProductId)
        {
            throw new InvalidOperationException($"The card is already on {productId}.");
        }

        if (on <= _products[^1].From)
        {
            throw new InvalidOperationException("A product change must come after the previous one.");
        }

        _products.Add(new ProductAssignment(productId, on));
    }

    public void Close(DateOnly on)
    {
        EnsureOpen();
        if (on < _products[^1].From)
        {
            throw new InvalidOperationException("A card cannot close before it opened or last changed product.");
        }

        ClosedOn = on;
    }

    /// <summary>The spans of days the card was on a product, in date order.</summary>
    public IReadOnlyList<DateRange> DaysOn(string productId)
    {
        var spans = new List<DateRange>();
        for (var index = 0; index < _products.Count; index++)
        {
            if (_products[index].ProductId != productId)
            {
                continue;
            }

            var lastDay = index + 1 < _products.Count
                ? _products[index + 1].From.AddDays(-1)
                : ClosedOn ?? DateOnly.MaxValue;
            spans.Add(new DateRange(_products[index].From, lastDay));
        }

        return spans;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("The card is closed.");
        }
    }
}
