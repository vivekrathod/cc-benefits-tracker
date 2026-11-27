using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Contracts;
using Chitragupta.Domain;

namespace Chitragupta.Application.Review;

/// <summary>
/// The queue of credits the matcher could not settle. Every answer is stored as a
/// decision about the transaction, so a replay gives the same result, and an answer
/// can teach a rule so the same question is not asked twice.
/// </summary>
public sealed class ReviewService(ITrackerStore store, ICatalog catalog, Rematcher rematcher)
{
    public async Task<IReadOnlyList<ReviewItemDto>> GetQueueAsync(CancellationToken cancellationToken)
    {
        var people = (await store.GetPeopleAsync(cancellationToken)).ToDictionary(person => person.Id);
        var cards = (await store.GetCardsAsync(cancellationToken)).ToDictionary(card => card.Id);
        var queue = new List<ReviewItemDto>();

        foreach (var item in await store.GetReviewItemsAsync(cancellationToken))
        {
            var transaction = await store.FindTransactionAsync(item.TransactionId, cancellationToken);
            if (transaction is null || !cards.TryGetValue(item.CardId, out var card))
            {
                continue;
            }

            var choices = card.Products
                .Select(assignment => assignment.ProductId)
                .Distinct()
                .SelectMany(catalog.BenefitsOf)
                .Select(benefit => new BenefitChoiceDto(benefit.Id, benefit.Name))
                .ToArray();

            queue.Add(new ReviewItemDto(
                item.Id,
                item.Reason.ToString(),
                card.Id,
                catalog.FindProduct(card.CurrentProductId)?.DisplayName ?? card.CurrentProductId,
                people.GetValueOrDefault(card.PersonId)?.Name ?? "",
                transaction.Id,
                transaction.PostedOn ?? transaction.TransactedOn,
                item.Amount.Dollars,
                transaction.Amount.Dollars,
                transaction.Description,
                item.BenefitId,
                item.BenefitId is null ? null : catalog.FindBenefit(item.BenefitId)?.Name,
                choices));
        }

        return queue
            .OrderByDescending(item => item.PostedOn)
            .ThenBy(item => item.Description, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The credit belongs to this benefit. With a keyword, similar credits are matched from now on.</summary>
    public async Task AssignAsync(Guid reviewItemId, string benefitId, string? keyword, CancellationToken cancellationToken)
    {
        var item = await FindAsync(reviewItemId, cancellationToken);
        var benefit = catalog.FindBenefit(benefitId)
            ?? throw new RequestRejectedException($"The catalog has no benefit '{benefitId}'.");

        await store.SavePinAsync(item.TransactionId, benefit.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // The ceiling keeps a learned keyword from swallowing a large refund from the same merchant.
            var ceiling = benefit.Terms.Max(terms => terms.Amount);
            await store.AddLearnedRuleAsync(
                new MatchRule(benefit.Id, keyword.Trim(), ceiling > Money.Zero ? ceiling : null), cancellationToken);
        }

        await rematcher.RematchAllAsync(cancellationToken);
    }

    /// <summary>The credit is not a benefit. With a keyword, similar descriptions are ignored from now on.</summary>
    public async Task DismissAsync(Guid reviewItemId, string? ignoreKeyword, CancellationToken cancellationToken)
    {
        var item = await FindAsync(reviewItemId, cancellationToken);
        await store.SavePinAsync(item.TransactionId, null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ignoreKeyword))
        {
            await store.AddLearnedIgnoreKeywordAsync(ignoreKeyword.Trim(), cancellationToken);
        }

        await rematcher.RematchAllAsync(cancellationToken);
    }

    /// <summary>
    /// Seen and accepted as it stands, for the flags that are information more than a
    /// question: a credit above the cap, a reversal, a credit outside every period.
    /// </summary>
    public async Task AcknowledgeAsync(Guid reviewItemId, CancellationToken cancellationToken)
    {
        var item = await FindAsync(reviewItemId, cancellationToken);
        await store.AcknowledgeFlagAsync(item.TransactionId, item.Reason, cancellationToken);
        await rematcher.RematchCardAsync(item.CardId, cancellationToken);
    }

    private async Task<ReviewItem> FindAsync(Guid reviewItemId, CancellationToken cancellationToken) =>
        await store.FindReviewItemAsync(reviewItemId, cancellationToken) ?? throw new NotFoundException("The review item");
}
