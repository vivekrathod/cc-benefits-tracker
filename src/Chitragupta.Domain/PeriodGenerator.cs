namespace Chitragupta.Domain;

/// <summary>
/// Computes the periods of one benefit on one card. Pure date logic: the same inputs
/// always give the same periods, whatever window is asked for.
/// </summary>
public static class PeriodGenerator
{
    /// <summary>
    /// The periods of a benefit on a card: only while the card was open, on the benefit's
    /// product, and while the benefit existed.
    /// </summary>
    public static IReadOnlyList<PeriodSpec> Generate(Card card, BenefitDefinition benefit, DateRange window) =>
        card.DaysOn(benefit.ProductId)
            .Select(span => span.Intersect(benefit.Lifetime))
            .OfType<DateRange>()
            .SelectMany(eligibility => Generate(benefit.Terms, card.Anniversary, eligibility, window))
            .ToArray();

    /// <param name="terms">Every set of terms the benefit has had, in any order.</param>
    /// <param name="anniversary">The card's anniversary, used by cardmember-year benefits.</param>
    /// <param name="eligibility">The days the card can use the benefit, for example from opening to closing.</param>
    /// <param name="window">The days of interest. Every period touching it is returned whole.</param>
    public static IReadOnlyList<PeriodSpec> Generate(
        IReadOnlyCollection<BenefitTerms> terms, Anniversary anniversary, DateRange eligibility, DateRange window)
    {
        if (window.IsOpenEnded)
        {
            throw new ArgumentException("The window must have an end.", nameof(window));
        }

        var periods = new List<PeriodSpec>();
        if (terms.Count == 0)
        {
            return periods;
        }

        var ordered = terms.OrderBy(version => version.EffectiveFrom).ToArray();
        var start = Later(eligibility.Start, ordered[0].EffectiveFrom);
        var lastDay = Earlier(eligibility.End, window.End);

        while (start <= lastDay)
        {
            // A period keeps the terms in force on its first day, even if they change before it ends.
            var inForce = ordered.Last(version => version.EffectiveFrom <= start);
            var end = Earlier(EndOfPeriodContaining(start, inForce, anniversary), eligibility.End);
            var range = new DateRange(start, end);

            if (range.Overlaps(window))
            {
                periods.Add(new PeriodSpec(range, inForce.Amount));
            }

            start = end.AddDays(1);
        }

        return periods;
    }

    private static DateOnly EndOfPeriodContaining(DateOnly date, BenefitTerms terms, Anniversary anniversary)
    {
        var months = terms.Cadence.Months();
        return terms.YearBasis switch
        {
            YearBasis.Calendar => EndOfCalendarPeriod(date, months),
            YearBasis.Cardmember => EndOfCardmemberPeriod(date, months, anniversary),
            _ => throw new ArgumentOutOfRangeException(nameof(terms), terms.YearBasis, null),
        };
    }

    private static DateOnly EndOfCalendarPeriod(DateOnly date, int months)
    {
        var periodIndex = (date.Month - 1) / months;
        var nextStart = new DateOnly(date.Year, 1, 1).AddMonths((periodIndex + 1) * months);
        return nextStart.AddDays(-1);
    }

    private static DateOnly EndOfCardmemberPeriod(DateOnly date, int months, Anniversary anniversary)
    {
        // Every boundary is counted from the anniversary itself, so a clamped date
        // (31st to the 28th) never drags the boundaries that follow it.
        var year = anniversary.In(date.Year) <= date ? date.Year : date.Year - 1;
        var monthsIntoYear = months;
        while (anniversary.PlusMonths(year, monthsIntoYear) <= date)
        {
            monthsIntoYear += months;
        }

        return anniversary.PlusMonths(year, monthsIntoYear).AddDays(-1);
    }

    private static DateOnly Later(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Earlier(DateOnly left, DateOnly right) => left < right ? left : right;
}
