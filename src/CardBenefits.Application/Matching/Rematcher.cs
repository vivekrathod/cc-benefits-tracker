using CardBenefits.Application.Ports;
using CardBenefits.Domain;

namespace CardBenefits.Application.Matching;

/// <summary>
/// Brings a card's periods and matched usage up to date from scratch: regenerate the
/// periods, clear what the matcher did last time, and replay every stored transaction.
/// Running it twice changes nothing, so anything that might have affected matching
/// simply calls it.
/// </summary>
public sealed class Rematcher(ITrackerStore store, ICatalog catalog, IClock clock, TrackerSettings settings)
{
    public async Task RematchAllAsync(CancellationToken cancellationToken)
    {
        var inputs = await LoadSharedInputsAsync(cancellationToken);
        foreach (var card in await store.GetCardsAsync(cancellationToken))
        {
            await RematchAsync(card, inputs, cancellationToken);
        }
    }

    public async Task RematchCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var card = await store.FindCardAsync(cardId, cancellationToken) ?? throw new NotFoundException("The card");
        await RematchAsync(card, await LoadSharedInputsAsync(cancellationToken), cancellationToken);
    }

    /// <summary>The days periods are kept for: from the tracking start to a little ahead of today.</summary>
    public DateRange Window()
    {
        var today = clock.Today;
        return new DateRange(settings.TrackingStart ?? today.AddYears(-1), today.AddDays(settings.HorizonDays));
    }

    private async Task RematchAsync(Card card, SharedInputs inputs, CancellationToken cancellationToken)
    {
        var stored = await store.GetPeriodsAsync(card.Id, cancellationToken);
        var (periods, obsolete) = Reconcile(card, stored);

        var sources = new List<(FeedAccount, IReadOnlyList<StoredTransaction>)>();
        foreach (var account in inputs.Accounts.Where(account => account.CardId == card.Id))
        {
            sources.Add((account, await store.GetTransactionsAsync(account.Id, cancellationToken)));
        }

        var transactions = FeedPrecedence.Merge(card.Id, sources, clock.Today);
        var result = Matcher.Match(transactions, periods, inputs.Rules, inputs.Options);

        var byId = periods.ToDictionary(period => period.Id);
        foreach (var allocation in result.Allocations)
        {
            byId[allocation.PeriodId].Use(allocation.Amount, UsageSource.Automatic, allocation.TransactionId);
        }

        var open = result.Review
            .Where(flag => !inputs.Acknowledged.Contains((flag.TransactionId, flag.Reason)))
            .ToArray();

        await store.DeletePeriodsAsync(obsolete, cancellationToken);
        await store.SavePeriodsAsync(periods, cancellationToken);
        await store.ReplaceReviewItemsAsync(card.Id, open, cancellationToken);
    }

    /// <summary>
    /// Lines the stored periods up with what the generator says they should be now.
    /// A period keeps its identity and its manual usage while its benefit and first day
    /// stay the same; matched usage is always dropped, because the replay rebuilds it.
    /// </summary>
    private (IReadOnlyList<BenefitPeriod> Periods, IReadOnlyList<Guid> Obsolete) Reconcile(
        Card card, IReadOnlyList<BenefitPeriod> stored)
    {
        var window = Window();
        var existing = stored.ToDictionary(period => (period.BenefitId, period.Range.Start));
        var periods = new List<BenefitPeriod>();

        var benefits = card.Products
            .Select(assignment => assignment.ProductId)
            .Distinct()
            .SelectMany(catalog.BenefitsOf);

        foreach (var benefit in benefits)
        {
            foreach (var spec in PeriodGenerator.Generate(card, benefit, window))
            {
                if (!existing.Remove((benefit.Id, spec.Range.Start), out var match))
                {
                    periods.Add(BenefitPeriod.From(spec, card.Id, benefit.Id));
                    continue;
                }

                periods.Add(BenefitPeriod.Restore(
                    match.Id, card.Id, benefit.Id, spec.Range, spec.Amount, ManualUsageWithin(match, spec.Amount)));
            }
        }

        return (periods, existing.Values.Select(period => period.Id).ToArray());
    }

    private static IEnumerable<UsageEntry> ManualUsageWithin(BenefitPeriod period, Money amount)
    {
        var total = Money.Zero;
        foreach (var entry in period.Usage.Where(entry => entry.Source == UsageSource.Manual))
        {
            if (total + entry.Amount > amount)
            {
                yield break;
            }

            total += entry.Amount;
            yield return entry;
        }
    }

    private async Task<SharedInputs> LoadSharedInputsAsync(CancellationToken cancellationToken)
    {
        var learnedRules = await store.GetLearnedRulesAsync(cancellationToken);
        var learnedIgnores = await store.GetLearnedIgnoreKeywordsAsync(cancellationToken);
        var pins = await store.GetPinsAsync(cancellationToken);

        var options = new MatchOptions
        {
            IgnoreKeywords = [.. catalog.IgnoreKeywords, .. learnedIgnores],
            GraceDays = new Dictionary<string, int>(catalog.GraceDays),
            Pins = new Dictionary<Guid, string?>(pins),
        };

        return new SharedInputs(
            await store.GetFeedAccountsAsync(cancellationToken),
            // Learned rules come first: the matcher keeps this order between rules that tie, so a
            // rule a person confirmed outranks the unverified catalog rule it was confirmed from.
            [.. learnedRules, .. catalog.Rules],
            options,
            await store.GetAcknowledgedFlagsAsync(cancellationToken));
    }

    private sealed record SharedInputs(
        IReadOnlyList<FeedAccount> Accounts,
        IReadOnlyList<MatchRule> Rules,
        MatchOptions Options,
        IReadOnlySet<(Guid, ReviewReason)> Acknowledged);
}
