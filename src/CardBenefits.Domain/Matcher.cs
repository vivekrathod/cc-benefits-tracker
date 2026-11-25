namespace CardBenefits.Domain;

/// <summary>
/// Allocates statement credits to benefit periods. It reads its inputs and changes
/// nothing, and the same inputs in any order give the same result, so matching can
/// be replayed from scratch at any time.
/// </summary>
public static class Matcher
{
    /// <param name="periods">
    /// The periods of the cards involved. Their manual usage counts against the cap;
    /// their automatic usage is ignored, because this run replaces it.
    /// </param>
    public static MatchResult Match(
        IEnumerable<CardTransaction> transactions,
        IEnumerable<BenefitPeriod> periods,
        IEnumerable<MatchRule> rules,
        MatchOptions options)
    {
        var book = periods
            .OrderBy(period => period.Range.Start)
            .ThenBy(period => period.Id)
            .Select(period => new OpenPeriod(period))
            .ToArray();
        var orderedRules = rules
            .OrderByDescending(rule => rule.Keyword.Length)
            .ThenBy(rule => rule.BenefitId, StringComparer.Ordinal)
            .ToArray();
        var ignored = options.IgnoreKeywords.Select(Normalize).ToArray();

        var allocations = new List<Allocation>();
        var review = new List<ReviewFlag>();

        var posted = transactions
            .Where(transaction => !transaction.Pending)
            .OrderBy(transaction => transaction.PostedOn)
            .ThenBy(transaction => transaction.FeedId, StringComparer.Ordinal)
            .ThenBy(transaction => transaction.Id);

        foreach (var transaction in posted)
        {
            var benefitId = BenefitFor(transaction, book, orderedRules, ignored, options, out var skip, out var confirmed);
            if (skip)
            {
                continue;
            }

            if (benefitId is not null && !confirmed)
            {
                if (transaction.Amount > Money.Zero)
                {
                    review.Add(new ReviewFlag(transaction.Id, ReviewReason.Suggested, transaction.Amount, benefitId));
                }

                continue;
            }

            if (transaction.Amount < Money.Zero)
            {
                if (benefitId is not null)
                {
                    review.Add(new ReviewFlag(transaction.Id, ReviewReason.Reversal, Money.Zero - transaction.Amount, benefitId));
                }

                continue;
            }

            if (benefitId is null)
            {
                review.Add(new ReviewFlag(transaction.Id, ReviewReason.Unmatched, transaction.Amount, null));
                continue;
            }

            var period = PeriodFor(transaction, benefitId, book, options);
            if (period is null)
            {
                review.Add(new ReviewFlag(transaction.Id, ReviewReason.NoPeriod, transaction.Amount, benefitId));
                continue;
            }

            var counted = Money.Min(transaction.Amount, period.Remaining);
            if (counted > Money.Zero)
            {
                period.Remaining -= counted;
                allocations.Add(new Allocation(transaction.Id, period.Id, counted));
            }

            if (counted < transaction.Amount)
            {
                review.Add(new ReviewFlag(transaction.Id, ReviewReason.Excess, transaction.Amount - counted, benefitId));
            }
        }

        return new MatchResult(allocations, review);
    }

    private static string? BenefitFor(
        CardTransaction transaction,
        OpenPeriod[] book,
        MatchRule[] orderedRules,
        string[] ignored,
        MatchOptions options,
        out bool skip,
        out bool confirmed)
    {
        skip = false;
        confirmed = true;
        if (options.Pins.TryGetValue(transaction.Id, out var pinned))
        {
            skip = pinned is null;
            return pinned;
        }

        if (transaction.Amount == Money.Zero)
        {
            skip = true;
            return null;
        }

        var description = Normalize(transaction.Description);
        if (ignored.Any(description.Contains))
        {
            skip = true;
            return null;
        }

        var amount = transaction.Amount < Money.Zero ? Money.Zero - transaction.Amount : transaction.Amount;
        var rule = orderedRules.FirstOrDefault(candidate =>
            description.Contains(Normalize(candidate.Keyword))
            && (candidate.AmountCeiling is null || amount <= candidate.AmountCeiling)
            && book.Any(period => period.CardId == transaction.CardId && period.BenefitId == candidate.BenefitId));

        confirmed = rule is null || rule.Verified;
        return rule?.BenefitId;
    }

    private static OpenPeriod? PeriodFor(CardTransaction transaction, string benefitId, OpenPeriod[] book, MatchOptions options)
    {
        var graceDays = options.GraceDays.GetValueOrDefault(benefitId, MatchOptions.DefaultGraceDays);
        var candidates = book
            .Where(period => period.CardId == transaction.CardId && period.BenefitId == benefitId)
            .ToArray();

        // Credits post days after the purchase, so one landing just after a period ended
        // belongs to that period while it still has value left.
        var justEnded = candidates.LastOrDefault(period =>
            period.Range.End < transaction.PostedOn
            && transaction.PostedOn.DayNumber - period.Range.End.DayNumber <= graceDays);
        if (justEnded is { Remaining.Cents: > 0 })
        {
            return justEnded;
        }

        return candidates.FirstOrDefault(period => period.Range.Contains(transaction.PostedOn));
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>A period's running balance during one matching run.</summary>
    private sealed class OpenPeriod(BenefitPeriod period)
    {
        public Guid Id { get; } = period.Id;

        public Guid CardId { get; } = period.CardId;

        public string BenefitId { get; } = period.BenefitId;

        public DateRange Range { get; } = period.Range;

        public Money Remaining { get; set; } = period.Amount - Money.Sum(
            period.Usage.Where(entry => entry.Source == UsageSource.Manual).Select(entry => entry.Amount));
    }
}
