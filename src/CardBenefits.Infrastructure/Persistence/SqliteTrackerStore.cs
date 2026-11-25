using CardBenefits.Application.Feeds;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Review;
using CardBenefits.Application.Sync;
using CardBenefits.Domain;
using Microsoft.EntityFrameworkCore;

namespace CardBenefits.Infrastructure.Persistence;

/// <summary>
/// The store on SQLite through EF Core. Every call opens its own short-lived context,
/// so the store is safe to share and nothing is tracked between calls.
/// </summary>
internal sealed class SqliteTrackerStore(IDbContextFactory<TrackerDbContext> contexts) : ITrackerStore
{
    // People and cards

    public async Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.People.AsNoTracking().ToListAsync(cancellationToken))
            .Select(row => new Person(row.Id, row.Name))
            .ToArray();
    }

    public async Task SavePersonAsync(Person person, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.People.FindAsync([person.Id], cancellationToken);
        if (row is null)
        {
            db.People.Add(row = new PersonRow { Id = person.Id });
        }

        row.Name = person.Name;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Card>> GetCardsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.Cards.AsNoTracking().Include(card => card.Products).ToListAsync(cancellationToken))
            .Select(ToCard)
            .ToArray();
    }

    public async Task<Card?> FindCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Cards.AsNoTracking().Include(card => card.Products)
            .SingleOrDefaultAsync(card => card.Id == cardId, cancellationToken);
        return row is null ? null : ToCard(row);
    }

    public async Task SaveCardAsync(Card card, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Cards.Include(existing => existing.Products)
            .SingleOrDefaultAsync(existing => existing.Id == card.Id, cancellationToken);
        if (row is null)
        {
            db.Cards.Add(row = new CardRow { Id = card.Id });
        }

        row.PersonId = card.PersonId;
        row.LastFour = card.LastFour;
        row.OpenedOn = card.OpenedOn;
        row.ClosedOn = card.ClosedOn;
        row.AnniversaryMonth = card.Anniversary.Month;
        row.AnniversaryDay = card.Anniversary.Day;
        row.AnnualFeeCents = card.AnnualFeeOverride?.Cents;

        db.RemoveRange(row.Products);
        row.Products = card.Products
            .Select(assignment => new CardProductRow { CardId = card.Id, ProductId = assignment.ProductId, From = assignment.From })
            .ToList();

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCardAsync(Guid cardId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.ReviewItems.Where(item => item.CardId == cardId).ExecuteDeleteAsync(cancellationToken);
        db.Periods.RemoveRange(await db.Periods.Where(period => period.CardId == cardId).ToListAsync(cancellationToken));
        db.Cards.RemoveRange(await db.Cards.Where(card => card.Id == cardId).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Feed accounts

    public async Task<IReadOnlyList<FeedAccount>> GetFeedAccountsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.FeedAccounts.AsNoTracking().ToListAsync(cancellationToken)).Select(ToFeedAccount).ToArray();
    }

    public async Task<FeedAccount?> FindFeedAccountAsync(Guid feedAccountId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.FeedAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == feedAccountId, cancellationToken);
        return row is null ? null : ToFeedAccount(row);
    }

    public async Task SaveFeedAccountAsync(FeedAccount account, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.FeedAccounts.FindAsync([account.Id], cancellationToken);
        if (row is null)
        {
            db.FeedAccounts.Add(row = new FeedAccountRow
            {
                Id = account.Id,
                Feed = account.Feed,
                AccountId = account.AccountId,
                FirstSeenAt = account.FirstSeenAt,
            });
        }

        row.ConnectionName = account.ConnectionName;
        row.Name = account.Name;
        row.Status = account.Status;
        row.CardId = account.CardId;
        await db.SaveChangesAsync(cancellationToken);
    }

    // Transactions

    public async Task<int> UpsertTransactionsAsync(
        Guid feedAccountId, IReadOnlyCollection<FeedTransaction> transactions, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var stored = await db.Transactions
            .Where(row => row.FeedAccountId == feedAccountId)
            .ToDictionaryAsync(row => row.FeedId, cancellationToken);
        var changed = 0;

        // A feed should not repeat an id within an account; if it does, the last one stands.
        foreach (var incoming in transactions.GroupBy(transaction => transaction.Id).Select(group => group.Last()))
        {
            if (!stored.TryGetValue(incoming.Id, out var row))
            {
                row = new TransactionRow { Id = Guid.NewGuid(), FeedAccountId = feedAccountId, FeedId = incoming.Id, FirstSeenAt = seenAt };
                db.Transactions.Add(row);
            }
            else if (row.PostedOn == incoming.PostedOn
                && row.TransactedOn == incoming.TransactedOn
                && row.AmountCents == incoming.Amount.Cents
                && row.Description == incoming.Description
                && row.Pending == incoming.Pending)
            {
                row.RawPayload = incoming.RawPayload;
                continue;
            }

            // Overwrite, never keep the first version: issuers correct amount, sign and text under the same id.
            row.PostedOn = incoming.PostedOn;
            row.TransactedOn = incoming.TransactedOn;
            row.AmountCents = incoming.Amount.Cents;
            row.Description = incoming.Description;
            row.Pending = incoming.Pending;
            row.RawPayload = incoming.RawPayload;
            row.LastChangedAt = seenAt;
            changed++;
        }

        var incomingIds = transactions.Select(transaction => transaction.Id).ToHashSet();
        foreach (var stale in stored.Values.Where(row => row.Pending && !incomingIds.Contains(row.FeedId)))
        {
            db.Transactions.Remove(stale);
            changed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    public async Task DeleteTransactionsAsync(Guid feedAccountId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Transactions.Where(row => row.FeedAccountId == feedAccountId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredTransaction>> GetTransactionsAsync(Guid feedAccountId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.Transactions.AsNoTracking().Where(row => row.FeedAccountId == feedAccountId).ToListAsync(cancellationToken))
            .Select(ToTransaction)
            .ToArray();
    }

    public async Task<StoredTransaction?> FindTransactionAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(transaction => transaction.Id == transactionId, cancellationToken);
        return row is null ? null : ToTransaction(row);
    }

    public async Task<DateOnly?> GetNewestPostedDateAsync(string feed, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Transactions
            .Where(transaction => db.FeedAccounts.Any(account => account.Id == transaction.FeedAccountId && account.Feed == feed))
            .MaxAsync(transaction => transaction.PostedOn, cancellationToken);
    }

    // Periods and usage

    public async Task<IReadOnlyList<BenefitPeriod>> GetPeriodsAsync(Guid? cardId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Periods.AsNoTracking().Include(period => period.Usage)
            .Where(period => cardId == null || period.CardId == cardId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToPeriod).ToArray();
    }

    public async Task<BenefitPeriod?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Periods.AsNoTracking().Include(period => period.Usage)
            .SingleOrDefaultAsync(period => period.Id == periodId, cancellationToken);
        return row is null ? null : ToPeriod(row);
    }

    public async Task SavePeriodsAsync(IReadOnlyCollection<BenefitPeriod> periods, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = periods.Select(period => period.Id).ToArray();
        var stored = await db.Periods.Include(period => period.Usage)
            .Where(period => ids.Contains(period.Id))
            .ToDictionaryAsync(period => period.Id, cancellationToken);

        foreach (var period in periods)
        {
            if (!stored.TryGetValue(period.Id, out var row))
            {
                db.Periods.Add(row = new PeriodRow { Id = period.Id });
            }

            row.CardId = period.CardId;
            row.BenefitId = period.BenefitId;
            row.Start = period.Range.Start;
            row.End = period.Range.End;
            row.AmountCents = period.Amount.Cents;

            db.RemoveRange(row.Usage);
            row.Usage = period.Usage
                .Select((entry, position) => new UsageRow
                {
                    PeriodId = period.Id,
                    Position = position,
                    AmountCents = entry.Amount.Cents,
                    Source = entry.Source,
                    TransactionId = entry.TransactionId,
                    Note = entry.Note,
                })
                .ToList();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePeriodsAsync(IReadOnlyCollection<Guid> periodIds, CancellationToken cancellationToken)
    {
        if (periodIds.Count == 0)
        {
            return;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = periodIds.ToArray();
        db.Periods.RemoveRange(await db.Periods.Where(period => ids.Contains(period.Id)).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Review queue, decisions and learned rules

    public async Task<IReadOnlyList<ReviewItem>> GetReviewItemsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.ReviewItems.AsNoTracking().ToListAsync(cancellationToken)).Select(ToReviewItem).ToArray();
    }

    public async Task<ReviewItem?> FindReviewItemAsync(Guid reviewItemId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.ReviewItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reviewItemId, cancellationToken);
        return row is null ? null : ToReviewItem(row);
    }

    public async Task ReplaceReviewItemsAsync(Guid cardId, IReadOnlyCollection<ReviewFlag> flags, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var stored = await db.ReviewItems.Where(item => item.CardId == cardId).ToListAsync(cancellationToken);

        // A flag that is still there keeps its id, so a link to it survives a replay.
        var remaining = stored.ToList();
        foreach (var flag in flags)
        {
            var same = remaining.FirstOrDefault(row =>
                row.TransactionId == flag.TransactionId && row.Reason == flag.Reason && row.BenefitId == flag.BenefitId);
            if (same is null)
            {
                db.ReviewItems.Add(new ReviewItemRow
                {
                    Id = Guid.NewGuid(),
                    CardId = cardId,
                    TransactionId = flag.TransactionId,
                    Reason = flag.Reason,
                    AmountCents = flag.Amount.Cents,
                    BenefitId = flag.BenefitId,
                });
                continue;
            }

            same.AmountCents = flag.Amount.Cents;
            remaining.Remove(same);
        }

        db.ReviewItems.RemoveRange(remaining);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> GetPinsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Pins.AsNoTracking().ToDictionaryAsync(pin => pin.TransactionId, pin => pin.BenefitId, cancellationToken);
    }

    public async Task SavePinAsync(Guid transactionId, string? benefitId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Pins.FindAsync([transactionId], cancellationToken);
        if (row is null)
        {
            db.Pins.Add(row = new PinRow { TransactionId = transactionId });
        }

        row.BenefitId = benefitId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<(Guid TransactionId, ReviewReason Reason)>> GetAcknowledgedFlagsAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.AcknowledgedFlags.AsNoTracking().ToListAsync(cancellationToken))
            .Select(row => (row.TransactionId, row.Reason))
            .ToHashSet();
    }

    public async Task AcknowledgeFlagAsync(Guid transactionId, ReviewReason reason, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.AcknowledgedFlags.FindAsync([transactionId, reason], cancellationToken) is null)
        {
            db.AcknowledgedFlags.Add(new AcknowledgedFlagRow { TransactionId = transactionId, Reason = reason });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MatchRule>> GetLearnedRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await db.LearnedRules.AsNoTracking().OrderBy(rule => rule.Id).ToListAsync(cancellationToken))
            .Select(row => new MatchRule(
                row.BenefitId, row.Keyword, row.AmountCeilingCents is null ? null : new Money(row.AmountCeilingCents.Value)))
            .ToArray();
    }

    public async Task AddLearnedRuleAsync(MatchRule rule, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.LearnedRules.SingleOrDefaultAsync(
            existing => existing.BenefitId == rule.BenefitId && existing.Keyword == rule.Keyword, cancellationToken);
        if (row is null)
        {
            db.LearnedRules.Add(row = new LearnedRuleRow { BenefitId = rule.BenefitId, Keyword = rule.Keyword });
        }

        row.AmountCeilingCents = rule.AmountCeiling?.Cents;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetLearnedIgnoreKeywordsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.IgnoreKeywords.AsNoTracking().Select(row => row.Keyword).OrderBy(keyword => keyword).ToListAsync(cancellationToken);
    }

    public async Task AddLearnedIgnoreKeywordAsync(string keyword, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.IgnoreKeywords.FindAsync([keyword], cancellationToken) is null)
        {
            db.IgnoreKeywords.Add(new IgnoreKeywordRow { Keyword = keyword });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // Sync log

    public async Task AddSyncRunAsync(SyncRun run, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.SyncRuns.Add(new SyncRunRow
        {
            Id = run.Id,
            Feed = run.Feed,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            WindowStart = run.Window.Start,
            WindowEnd = run.Window.End,
            AccountsSeen = run.AccountsSeen,
            TransactionsReceived = run.TransactionsReceived,
            TransactionsChanged = run.TransactionsChanged,
            NewestReturned = run.NewestReturned,
            NewestStored = run.NewestStored,
            Failure = run.Failure,
            Errors = run.Errors
                .Select(error => new SyncErrorRow { Code = error.Code, Message = error.Message, ConnectionName = error.ConnectionName })
                .ToList(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncRun>> GetRecentSyncRunsAsync(int count, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.SyncRuns.AsNoTracking().Include(run => run.Errors)
            .OrderByDescending(run => run.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SyncRun(
                row.Id,
                row.Feed,
                row.StartedAt,
                row.FinishedAt,
                new DateRange(row.WindowStart, row.WindowEnd),
                row.AccountsSeen,
                row.TransactionsReceived,
                row.TransactionsChanged,
                row.NewestReturned,
                row.NewestStored,
                row.Failure,
                row.Errors.OrderBy(error => error.Id).Select(error => new SyncError(error.Code, error.Message, error.ConnectionName)).ToArray()))
            .ToArray();
    }

    // Reminders

    public async Task<bool> TryRecordReminderAsync(string key, DateTimeOffset sentAt, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await db.Reminders.FindAsync([key], cancellationToken) is not null)
        {
            return false;
        }

        db.Reminders.Add(new ReminderRow { Key = key, SentAt = sentAt });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ForgetReminderAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Reminders.Where(row => row.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task BackupAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.Delete(path);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}", cancellationToken);
    }

    // Mapping

    private static Card ToCard(CardRow row) => Card.Restore(
        row.Id,
        row.PersonId,
        row.LastFour,
        row.OpenedOn,
        row.ClosedOn,
        new Anniversary(row.AnniversaryMonth, row.AnniversaryDay),
        row.AnnualFeeCents is null ? null : new Money(row.AnnualFeeCents.Value),
        row.Products.Select(product => new ProductAssignment(product.ProductId, product.From)));

    private static FeedAccount ToFeedAccount(FeedAccountRow row) => FeedAccount.Restore(
        row.Id, row.Feed, row.ConnectionName, row.AccountId, row.Name, row.FirstSeenAt, row.Status, row.CardId);

    private static StoredTransaction ToTransaction(TransactionRow row) => new(
        row.Id, row.FeedAccountId, row.FeedId, row.PostedOn, row.TransactedOn, new Money(row.AmountCents), row.Description, row.Pending);

    private static BenefitPeriod ToPeriod(PeriodRow row) => BenefitPeriod.Restore(
        row.Id,
        row.CardId,
        row.BenefitId,
        new DateRange(row.Start, row.End),
        new Money(row.AmountCents),
        row.Usage.OrderBy(usage => usage.Position)
            .Select(usage => new UsageEntry(new Money(usage.AmountCents), usage.Source, usage.TransactionId, usage.Note)));

    private static ReviewItem ToReviewItem(ReviewItemRow row) => new(
        row.Id, row.CardId, row.TransactionId, row.Reason, new Money(row.AmountCents), row.BenefitId);
}
