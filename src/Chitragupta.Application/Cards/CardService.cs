using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Contracts;
using Chitragupta.Domain;

namespace Chitragupta.Application.Cards;

/// <summary>People, cards, and the queue of feed accounts waiting for a decision.</summary>
public sealed class CardService(ITrackerStore store, ICatalog catalog, Rematcher rematcher, IClock clock)
{
    public async Task<Person> AddPersonAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RequestRejectedException("A person needs a name.");
        }

        var people = await store.GetPeopleAsync(cancellationToken);
        var existing = people.FirstOrDefault(person => person.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var person = new Person(Guid.NewGuid(), name);
        await store.SavePersonAsync(person, cancellationToken);
        return person;
    }

    public async Task<Person> RenamePersonAsync(Guid personId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RequestRejectedException("A person needs a name.");
        }

        if ((await store.GetPeopleAsync(cancellationToken)).All(person => person.Id != personId))
        {
            throw new NotFoundException("The person");
        }

        var renamed = new Person(personId, name);
        await store.SavePersonAsync(renamed, cancellationToken);
        return renamed;
    }

    /// <summary>The accounts nobody has decided on yet, each with what the service could work out.</summary>
    public async Task<IReadOnlyList<NewAccountDto>> GetNewAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await store.GetFeedAccountsAsync(cancellationToken);
        var cards = await store.GetCardsAsync(cancellationToken);
        var linkedLive = accounts
            .Where(account => account.CardId is not null && account.Feed != TrackerSettings.CsvFeedName)
            .Select(account => account.CardId!.Value)
            .ToHashSet();

        var queue = new List<NewAccountDto>();
        foreach (var account in accounts.Where(account => account.Status == FeedAccountStatus.New))
        {
            var transactions = await store.GetTransactionsAsync(account.Id, cancellationToken);
            var product = ProductSuggester.Suggest(account.ConnectionName, account.Name, catalog.Products);
            var feeDate = AnnualFeeDate(transactions);

            // An existing card of the same product is probably this one: either it has no live
            // account yet, or it carries the same last four digits, as after a re-linked connection.
            var sameProduct = product is null
                ? []
                : cards
                    .Where(card => !card.IsClosed && card.CurrentProductId == product.Id)
                    .Where(card => card.LastFour is not null && account.LastFour is not null
                        ? card.LastFour == account.LastFour
                        : !linkedLive.Contains(card.Id))
                    .Select(card => card.Id)
                    .ToArray();

            queue.Add(new NewAccountDto(
                account.Id,
                account.Feed,
                account.ConnectionName,
                account.Name,
                account.LastFour,
                transactions.Count,
                product is not null || ProductSuggester.LooksLikeCard(account.Name) || ReceivesCardPayments(transactions),
                product?.Id,
                sameProduct,
                feeDate?.Month,
                feeDate?.Day));
        }

        return queue
            .OrderByDescending(item => item.LooksLikeCard)
            .ThenBy(item => item.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<FeedAccountDto>> GetAccountsAsync(CancellationToken cancellationToken) =>
        (await store.GetFeedAccountsAsync(cancellationToken))
            .OrderBy(account => account.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
            .Select(account => new FeedAccountDto(
                account.Id, account.Feed, account.ConnectionName, account.Name, account.Status.ToString(), account.CardId))
            .ToArray();

    /// <summary>A new feed account is a card: create it, map it, and match what is already stored.</summary>
    public async Task<Card> ConfirmCardAsync(Guid feedAccountId, ConfirmCardRequest request, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(feedAccountId, cancellationToken);
        if (account.Status == FeedAccountStatus.Mapped)
        {
            throw new RequestRejectedException("This account already belongs to a card.");
        }

        var anniversary = AnniversaryOf(request.AnniversaryMonth, request.AnniversaryDay);
        var card = await CreateCardAsync(
            request.PersonId,
            request.ProductId,
            // Without an opening date the card is treated as open well before anything tracked.
            request.OpenedOn ?? anniversary.In(clock.Today.Year - 2),
            anniversary,
            account.LastFour,
            request.AnnualFee,
            cancellationToken);

        account.MapTo(card.Id);
        await store.SaveFeedAccountAsync(account, cancellationToken);
        await rematcher.RematchCardAsync(card.Id, cancellationToken);
        return card;
    }

    /// <summary>A card with no feed account, for an issuer the Bridge is not connected to.</summary>
    public async Task<Card> AddCardAsync(AddCardRequest request, CancellationToken cancellationToken)
    {
        var anniversary = request.AnniversaryMonth is null || request.AnniversaryDay is null
            ? Anniversary.Of(request.OpenedOn)
            : AnniversaryOf(request.AnniversaryMonth.Value, request.AnniversaryDay.Value);

        var card = await CreateCardAsync(
            request.PersonId, request.ProductId, request.OpenedOn, anniversary, request.LastFour, request.AnnualFee,
            cancellationToken);
        await rematcher.RematchCardAsync(card.Id, cancellationToken);
        return card;
    }

    /// <summary>The account is one more source for a card that exists, as after a re-linked connection.</summary>
    public async Task MapAsync(Guid feedAccountId, Guid cardId, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(feedAccountId, cancellationToken);
        var card = await FindCardAsync(cardId, cancellationToken);

        await LinkAsync(account, card, cancellationToken);
        await rematcher.RematchAllAsync(cancellationToken);
    }

    /// <summary>
    /// Links the new accounts that leave no doubt: exactly one card fits the account, and
    /// no other new account is after that card. The rest wait for a person. Matching is
    /// left to the caller, which replays once for the whole batch.
    /// </summary>
    /// <returns>How many accounts were linked.</returns>
    public async Task<int> LinkCertainAsync(CancellationToken cancellationToken)
    {
        var certain = (await GetNewAccountsAsync(cancellationToken))
            .Where(account => account.SuggestedCardIds.Count == 1)
            .GroupBy(account => account.SuggestedCardIds[0])
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();

        foreach (var waiting in certain)
        {
            await LinkAsync(
                await FindAccountAsync(waiting.Id, cancellationToken),
                await FindCardAsync(waiting.SuggestedCardIds[0], cancellationToken),
                cancellationToken);
        }

        return certain.Length;
    }

    private async Task LinkAsync(FeedAccount account, Card card, CancellationToken cancellationToken)
    {
        account.MapTo(card.Id);
        await store.SaveFeedAccountAsync(account, cancellationToken);

        // A seeded card has no digits yet. With them, a second account of the same product
        // is told apart, and a re-linked connection finds its way back to this card.
        if (card.LastFour is null && account.LastFour is { } lastFour)
        {
            card.RecordLastFour(lastFour);
            await store.SaveCardAsync(card, cancellationToken);
        }
    }

    /// <summary>Not a card. The account stays hidden and its stored transactions are removed.</summary>
    public async Task DismissAsync(IReadOnlyCollection<Guid> feedAccountIds, CancellationToken cancellationToken)
    {
        var rematch = false;
        foreach (var id in feedAccountIds)
        {
            var account = await FindAccountAsync(id, cancellationToken);
            rematch |= account.CardId is not null;
            account.Dismiss();
            await store.SaveFeedAccountAsync(account, cancellationToken);
            await store.DeleteTransactionsAsync(account.Id, cancellationToken);
        }

        if (rematch)
        {
            await rematcher.RematchAllAsync(cancellationToken);
        }
    }

    /// <summary>Undoes a dismissal or a mapping: the account goes back to the queue.</summary>
    public async Task ReopenAsync(Guid feedAccountId, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(feedAccountId, cancellationToken);
        var wasMapped = account.CardId is not null;
        account.Reopen();
        await store.SaveFeedAccountAsync(account, cancellationToken);

        if (wasMapped)
        {
            await rematcher.RematchAllAsync(cancellationToken);
        }
    }

    public async Task ChangeProductAsync(Guid cardId, string productId, DateOnly on, CancellationToken cancellationToken)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        RequireProduct(productId);
        Guard(() => card.ChangeProduct(productId, on));
        await SaveAndRematchAsync(card, cancellationToken);
    }

    public async Task CloseAsync(Guid cardId, DateOnly on, CancellationToken cancellationToken)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        Guard(() => card.Close(on));
        await SaveAndRematchAsync(card, cancellationToken);
    }

    public async Task UpdateAsync(Guid cardId, UpdateCardRequest request, CancellationToken cancellationToken)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        card.CorrectAnniversary(AnniversaryOf(request.AnniversaryMonth, request.AnniversaryDay));
        Guard(() => card.SetAnnualFee(request.AnnualFee is null ? null : Money.FromDollars(request.AnnualFee.Value)));
        await SaveAndRematchAsync(card, cancellationToken);
    }

    /// <summary>Removes a card added by mistake. Its feed accounts go back to the queue.</summary>
    public async Task RemoveAsync(Guid cardId, CancellationToken cancellationToken)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        foreach (var account in (await store.GetFeedAccountsAsync(cancellationToken)).Where(account => account.CardId == card.Id))
        {
            if (account.Feed == TrackerSettings.CsvFeedName)
            {
                // A CSV import only exists for its card.
                account.Dismiss();
                await store.DeleteTransactionsAsync(account.Id, cancellationToken);
            }
            else
            {
                account.Reopen();
            }

            await store.SaveFeedAccountAsync(account, cancellationToken);
        }

        await store.DeleteCardAsync(card.Id, cancellationToken);
    }

    /// <summary>
    /// A card whose feed name is just the cardholder still gives itself away: money comes
    /// in as bill payments, which the catalog already knows how to recognise.
    /// </summary>
    private bool ReceivesCardPayments(IEnumerable<StoredTransaction> transactions) =>
        transactions.Any(transaction => transaction.Amount > Money.Zero
            && catalog.IgnoreKeywords.Any(keyword => transaction.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The day the annual fee last posted, when the stored transactions show one. Issuers
    /// bill the fee on the first statement of the cardmember year, so it is a good first
    /// guess for the anniversary and is offered to the person to confirm.
    /// </summary>
    internal static DateOnly? AnnualFeeDate(IEnumerable<StoredTransaction> transactions) =>
        transactions
            .Where(transaction => transaction is { Pending: false, PostedOn: not null } && transaction.Amount < Money.Zero)
            .Where(transaction =>
                transaction.Description.Contains("ANNUAL", StringComparison.OrdinalIgnoreCase)
                && (transaction.Description.Contains("FEE", StringComparison.OrdinalIgnoreCase)
                    || transaction.Description.Contains("MEMBERSHIP", StringComparison.OrdinalIgnoreCase)))
            .Max(transaction => transaction.PostedOn);

    private async Task<Card> CreateCardAsync(
        Guid personId,
        string productId,
        DateOnly openedOn,
        Anniversary anniversary,
        string? lastFour,
        decimal? annualFee,
        CancellationToken cancellationToken)
    {
        if ((await store.GetPeopleAsync(cancellationToken)).All(person => person.Id != personId))
        {
            throw new NotFoundException("The person");
        }

        RequireProduct(productId);
        var card = Guard(() => new Card(Guid.NewGuid(), personId, productId, openedOn, anniversary, lastFour));
        Guard(() => card.SetAnnualFee(annualFee is null ? null : Money.FromDollars(annualFee.Value)));
        await store.SaveCardAsync(card, cancellationToken);
        return card;
    }

    private async Task SaveAndRematchAsync(Card card, CancellationToken cancellationToken)
    {
        await store.SaveCardAsync(card, cancellationToken);
        await rematcher.RematchCardAsync(card.Id, cancellationToken);
    }

    private void RequireProduct(string productId)
    {
        if (catalog.FindProduct(productId) is null)
        {
            throw new RequestRejectedException($"The catalog has no card product '{productId}'.");
        }
    }

    private async Task<FeedAccount> FindAccountAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindFeedAccountAsync(id, cancellationToken) ?? throw new NotFoundException("The account");

    private async Task<Card> FindCardAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindCardAsync(id, cancellationToken) ?? throw new NotFoundException("The card");

    private static Anniversary AnniversaryOf(int month, int day) => Guard(() => new Anniversary(month, day));

    private static void Guard(Action change) => Guard(() =>
    {
        change();
        return true;
    });

    /// <summary>Turns a rule the domain enforces into a message for the person who broke it.</summary>
    private static T Guard<T>(Func<T> change)
    {
        try
        {
            return change();
        }
        catch (Exception rejected) when (rejected is ArgumentException or InvalidOperationException)
        {
            throw new RequestRejectedException(rejected.Message);
        }
    }
}
