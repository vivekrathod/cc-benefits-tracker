using Chitragupta.Application.Balances;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Sync;
using Chitragupta.Contracts;
using Chitragupta.Domain;

namespace Chitragupta.Application.Views;

/// <summary>The read side: every screen is one of these queries.</summary>
public sealed class TrackerQueries(
    ITrackerStore store, ICatalog catalog, IClock clock, TrackerSettings settings, IBalanceChecker checker, BalanceCheckerState checkerState)
{
    private const int RecentAttempts = 14;

    public async Task<IReadOnlyList<PersonDto>> GetPeopleAsync(CancellationToken cancellationToken) =>
        (await store.GetPeopleAsync(cancellationToken))
            .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .Select(person => new PersonDto(person.Id, person.Name))
            .ToArray();

    public IReadOnlyList<ProductDto> GetProducts() =>
        catalog.Products
            .OrderBy(product => product.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(product => new ProductDto(
                product.Id, product.DisplayName, product.AnnualFee.Dollars, product.IsBusiness, product.PointsProgram,
                catalog.AliasesOf(product.Id) is { Count: > 0 } aliases ? aliases : null))
            .ToArray();

    public IReadOnlyList<BenefitDto> GetBenefits(string productId) =>
        catalog.BenefitsOf(productId)
            .Select(benefit =>
            {
                var terms = benefit.Terms.Last(candidate => candidate.EffectiveFrom <= clock.Today || candidate == benefit.Terms[0]);
                return new BenefitDto(
                    benefit.Id,
                    benefit.Name,
                    benefit.Detection == DetectionMode.Manual,
                    terms.Amount.Dollars,
                    terms.Cadence.ToString(),
                    terms.YearBasis.ToString(),
                    catalog.NotesOf(benefit.Id));
            })
            .ToArray();

    public async Task<IReadOnlyList<CardDto>> GetCardsAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).Cards.Values
            .OrderBy(card => card.PersonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => card.ClosedOn is not null)
            .ThenBy(card => card.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Periods with value left that end within the given days, soonest first, both people together.</summary>
    public async Task<IReadOnlyList<PeriodDto>> GetExpiringSoonAsync(int? days, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var until = today.AddDays(days ?? settings.ExpiringSoonDays);
        var snapshot = await LoadAsync(cancellationToken);

        return snapshot.Periods
            .Where(period => period.Range.Contains(today) && period.Range.End <= until && !period.IsUsed)
            .Select(snapshot.ToDto)
            .OfType<PeriodDto>()
            .OrderBy(period => period.End)
            .ThenByDescending(period => period.Remaining)
            .ThenBy(period => period.BenefitName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Per open card, each benefit's current period with used and remaining.</summary>
    public async Task<IReadOnlyList<CardPeriodsDto>> GetThisPeriodAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var snapshot = await LoadAsync(cancellationToken);

        return snapshot.Cards.Values
            .Where(card => card.ClosedOn is null || card.ClosedOn >= today)
            .Select(card => new CardPeriodsDto(
                card,
                snapshot.Periods
                    .Where(period => period.CardId == card.Id && period.Range.Contains(today))
                    .Select(snapshot.ToDto)
                    .OfType<PeriodDto>()
                    .OrderBy(period => period.End)
                    .ThenBy(period => period.BenefitName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(group => group.Card.PersonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Card.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Value returned against the annual fee, per card, for the current cardmember year.
    /// A period counts in the cardmember year it ends in, so a calendar-year benefit is
    /// counted once however the two kinds of year overlap.
    /// </summary>
    public async Task<IReadOnlyList<CardValueDto>> GetCardValueAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var snapshot = await LoadAsync(cancellationToken);
        var values = new List<CardValueDto>();

        foreach (var card in snapshot.Domain.Values.Where(card => !card.IsClosed))
        {
            var start = card.CardmemberYearStart(today);
            var year = new DateRange(start, card.Anniversary.PlusMonths(start.Year, 12).AddDays(-1));
            var periods = snapshot.Periods
                .Where(period => period.CardId == card.Id && period.HasDollarValue && year.Contains(period.Range.End))
                .ToArray();

            var used = Money.Sum(periods.Select(period => period.Used));
            var dto = snapshot.Cards[card.Id];
            values.Add(new CardValueDto(
                dto,
                year.Start,
                year.End,
                used.Dollars,
                Money.Sum(periods.Select(period => period.Amount)).Dollars,
                dto.AnnualFee,
                used.Dollars - dto.AnnualFee));
        }

        return values
            .OrderBy(value => value.Card.PersonName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(value => value.AnnualFee)
            .ThenBy(value => value.Card.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Last successful sync per institution, with the latest error each one reported.</summary>
    public async Task<SyncHealthDto> GetSyncHealthAsync(
        string liveFeedName, bool feedConfigured, DateTimeOffset? nextManualSyncAt, CancellationToken cancellationToken)
    {
        var runs = await store.GetRecentSyncRunsAsync(60, cancellationToken);
        var accounts = await store.GetFeedAccountsAsync(cancellationToken);
        var liveRuns = runs.Where(run => run.Feed == liveFeedName).ToArray();

        var connections = accounts
            .Where(account => account.Feed == liveFeedName && account.Status != FeedAccountStatus.Dismissed)
            .GroupBy(account => account.ConnectionName)
            .Select(group => (Name: group.Key, Cards: group.Count(account => account.Status == FeedAccountStatus.Mapped)))
            .ToDictionary(connection => connection.Name, connection => connection.Cards);

        // An institution in trouble may have no account left in the feed, but it still shows up in the errors.
        var latestErrors = liveRuns.FirstOrDefault(run => run.Succeeded)?.Errors ?? [];
        foreach (var name in latestErrors.Select(error => error.ConnectionName).OfType<string>())
        {
            connections.TryAdd(name, 0);
        }

        var health = connections
            .Select(connection =>
            {
                var lastOk = liveRuns.FirstOrDefault(run =>
                    run.Succeeded && run.Errors.All(error => error.ConnectionName != connection.Key));
                var lastBad = liveRuns
                    .Select(run => (Run: run, Error: run.Errors.FirstOrDefault(error => error.ConnectionName == connection.Key)))
                    .FirstOrDefault(pair => pair.Error is not null);
                var stillFailing = lastBad.Run is not null && (lastOk is null || lastBad.Run.StartedAt > lastOk.StartedAt);

                return new ConnectionHealthDto(
                    connection.Key,
                    lastOk is null ? null : clock.ToLocal(lastOk.StartedAt),
                    stillFailing ? lastBad.Error!.Message : null,
                    stillFailing ? clock.ToLocal(lastBad.Run!.StartedAt) : null,
                    connection.Value);
            })
            .OrderBy(connection => connection.LastError is null)
            .ThenBy(connection => connection.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var last = liveRuns.FirstOrDefault();
        return new SyncHealthDto(
            feedConfigured,
            last is null ? null : clock.ToLocal(last.StartedAt),
            last?.Failure,
            nextManualSyncAt,
            health,
            runs.Take(20).Select(ToDto).ToArray());
    }

    /// <summary>
    /// Every person's miles and points as last read, soonest to lapse first within a person,
    /// with how the automatic checks are doing: the ones that need a look come first.
    /// </summary>
    public async Task<BalancesDto> GetBalancesAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = clock.Today;
        var people = await store.GetPeopleAsync(cancellationToken);
        var names = people.ToDictionary(person => person.Id, person => person.Name);
        var warnFrom = settings.BalanceNudgeDaysBefore.DefaultIfEmpty(0).Max();
        var asked = checker.IsConfigured ? checkerState.Read() : null;

        var histories = CheckHistory.ByAccount(
            await store.GetBalanceAttemptsAsync(now.AddDays(-settings.BalanceHistoryDays), cancellationToken));
        var known = histories.Select(history => history.Account).ToHashSet();
        // An account quiet for longer than the history is kept still has its newest attempt.
        var all = histories
            .Concat((await store.GetBalanceChecksAsync(cancellationToken))
                .Where(check => !known.Contains(check.Account))
                .Select(check => new CheckHistory(check.Account, [check])))
            .ToArray();

        var checks = all
            .Select(history =>
            {
                var latest = history.Latest;
                var signIns = history.SignInsOn(today, clock);
                var waiting = asked is not null && (asked.Running == latest.Account || asked.Requested.Contains(latest.Account));
                var (waitingFor, waitingUntil) = asked is null ? default : Wait(latest.Account, asked, now);
                return new BalanceCheckDto(
                    latest.Account, latest.Owner, latest.Program, latest.Status.ToString(), latest.Problem, clock.ToLocal(latest.CheckedAt))
                {
                    ErrorCode = latest.ErrorCode,
                    Advice = latest.Advice,
                    FailingSince = history.FailingSince is { } since ? clock.ToLocal(since) : null,
                    Overdue = IsOverdue(latest, now),
                    RetriesItself = latest is { NeedsAttention: true, Retryable: true } || latest.Status == BalanceCheckStatus.Blocked,
                    SignInsToday = signIns,
                    Queued = waiting,
                    WaitingFor = waitingFor,
                    WaitingUntil = waitingUntil,
                    CanCheckNow = asked is { UnreachableSince: null }
                        && asked.Accounts.Contains(latest.Account)
                        && !waiting
                        && signIns < settings.BalanceSignInsPerDay,
                    Recent = history.Attempts
                        .TakeLast(RecentAttempts)
                        .Select(attempt => new BalanceAttemptDto(
                            clock.ToLocal(attempt.CheckedAt), attempt.Status.ToString(), attempt.SignedIn, attempt.ErrorCode))
                        .ToArray(),
                };
            })
            .OrderBy(check => check.Status == "Ok" && !check.Overdue)
            .ThenBy(check => check.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var balances = (await store.GetLatestBalancesAsync(cancellationToken))
            .Select(balance =>
            {
                var days = balance.DaysUntilExpiry(today);
                var age = today.DayNumber - clock.ToLocalDate(balance.AsOf).DayNumber;
                return new BalanceDto(
                    balance.PersonId,
                    names.GetValueOrDefault(balance.PersonId) ?? "",
                    balance.Program,
                    balance.Amount,
                    clock.ToLocal(balance.AsOf),
                    age,
                    balance.Source.ToString(),
                    balance.ExpiresOn,
                    days,
                    days <= warnFrom,
                    balance.Source == BalanceSource.Automation && age >= settings.BalanceStaleAfterDays
                        ? WhyStale(balance, all, people)
                        : null);
            })
            .OrderBy(balance => balance.PersonName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(balance => balance.DaysUntilExpiry ?? int.MaxValue)
            .ThenBy(balance => balance.Program, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BalancesDto(
            balances,
            checks,
            asked is null
                ? null
                : new BalanceCheckerDto(
                    asked.UnreachableSince is { } down ? clock.ToLocal(down) : null,
                    asked.Problem,
                    asked.Running,
                    settings.BalanceCheckAt,
                    settings.BalanceSignInsPerDay));
    }

    /// <summary>What a check a person asked for is waiting for, in the order the runner looks at it.</summary>
    private (string? For, DateTimeOffset? Until) Wait(string account, BalanceCheckerState.Snapshot asked, DateTimeOffset now)
    {
        if (asked.Running == account || !asked.Requested.Contains(account))
        {
            return default;
        }

        if (asked.Running is { } other)
        {
            return ($"The check of {other} is running, and only one sign-in runs at a time.", null);
        }

        if (asked.Pause is { } pause && pause.Holds(now, requested: true))
        {
            var why = pause.Why switch
            {
                BalanceWait.Busy => "A sign-in started outside the tracker is running, and only one runs at a time.",
                BalanceWait.Trouble => "The automation's last answer could not be used, so nothing is asked of it for a quarter of an hour.",
                _ => "The automation asked to wait.",
            };
            return (why, clock.ToLocal(pause.Until));
        }

        return asked.Requested[0] == account ? default : ("Another check you asked for goes first.", null);
    }

    private bool IsOverdue(BalanceCheck latest, DateTimeOffset now) =>
        now - latest.CheckedAt >= TimeSpan.FromHours(settings.BalanceOverdueAfterHours);

    /// <summary>An old reading is explained by the check that should have renewed it: the one for the same person and program.</summary>
    private static string WhyStale(ProgramBalance balance, IEnumerable<CheckHistory> histories, IReadOnlyList<Person> people)
    {
        var mine = histories
            .Select(history => history.Latest)
            .Where(check => Owners.Find(people, check.Owner) is { } owner && balance.IsFor(owner.Id, check.Program))
            .ToArray();
        return mine.FirstOrDefault(check => check.NeedsAttention) is { } failing
            ? failing.Status.ToString()
            : mine.Length > 0 ? "Overdue" : "NotChecked";
    }

    public async Task<SummaryDto> GetSummaryAsync(string liveFeedName, CancellationToken cancellationToken)
    {
        var health = await GetSyncHealthAsync(liveFeedName, true, null, cancellationToken);
        var accounts = await store.GetFeedAccountsAsync(cancellationToken);
        var today = clock.Today;

        // Miles count with the benefits once they are inside the same window.
        var lapsing = (await store.GetLatestBalancesAsync(cancellationToken))
            .Count(balance => balance.DaysUntilExpiry(today) is { } days && days >= 0 && days <= settings.ExpiringSoonDays);

        // One count for everything on the Points screen that wants a look: a check that is failing or has
        // gone quiet, a reading nothing renews any more, and an automation that does not answer.
        var points = await GetBalancesAsync(cancellationToken);
        var balanceProblems = points.Checks.Count(check => check.Status != "Ok" || check.Overdue)
            + points.Balances.Count(balance => balance.Stale == "NotChecked")
            + (points.Checker?.UnreachableSince is null ? 0 : 1);

        return new SummaryDto(
            (await GetExpiringSoonAsync(null, cancellationToken)).Count + lapsing,
            (await store.GetReviewItemsAsync(cancellationToken)).Count,
            accounts.Count(account => account.Status == FeedAccountStatus.New),
            health.Connections.Count(connection => connection.LastError is not null) + (health.LastFailure is null ? 0 : 1),
            balanceProblems);
    }

    public SyncRunDto ToDto(SyncRun run) => new(
        clock.ToLocal(run.StartedAt),
        run.Feed,
        run.Window.Start,
        run.Window.End,
        run.AccountsSeen,
        run.TransactionsReceived,
        run.TransactionsChanged,
        run.NewestReturned,
        run.NewestStored,
        run.Failure,
        run.Errors
            .Select(error => error.ConnectionName is null ? error.Message : $"{error.ConnectionName}: {error.Message}")
            .ToArray());

    private async Task<Snapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var people = (await store.GetPeopleAsync(cancellationToken)).ToDictionary(person => person.Id);
        var accounts = await store.GetFeedAccountsAsync(cancellationToken);
        var domain = (await store.GetCardsAsync(cancellationToken)).ToDictionary(card => card.Id);

        var periods = await store.GetPeriodsAsync(null, cancellationToken);
        var withOpenPeriods = periods.Where(period => period.Range.End >= today).Select(period => period.CardId).ToHashSet();

        var cards = domain.Values.ToDictionary(card => card.Id, card =>
        {
            var product = catalog.FindProduct(card.CurrentProductId);
            var yearStart = card.CardmemberYearStart(today);
            return new CardDto(
                card.Id,
                card.PersonId,
                people.GetValueOrDefault(card.PersonId)?.Name ?? "",
                card.CurrentProductId,
                product?.DisplayName ?? card.CurrentProductId,
                card.LastFour,
                card.OpenedOn,
                card.ClosedOn,
                card.Anniversary.Month,
                card.Anniversary.Day,
                (card.AnnualFeeOverride ?? product?.AnnualFee ?? Money.Zero).Dollars,
                card.Anniversary.PlusMonths(yearStart.Year, 12),
                product?.PointsProgram,
                accounts
                    .Where(account => account.CardId == card.Id)
                    .Select(account => account.Feed == TrackerSettings.CsvFeedName ? "CSV import" : $"{account.ConnectionName}: {account.Name}")
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                // Something recurring to track: a benefit its product still has, or a period still running
                // from a product it had before.
                withOpenPeriods.Contains(card.Id)
                    || catalog.BenefitsOf(card.CurrentProductId).Any(benefit => benefit.LastDay is null || benefit.LastDay >= today));
        });

        return new Snapshot(domain, cards, periods, catalog, today);
    }

    private sealed record Snapshot(
        Dictionary<Guid, Card> Domain,
        Dictionary<Guid, CardDto> Cards,
        IReadOnlyList<BenefitPeriod> Periods,
        ICatalog Catalog,
        DateOnly Today)
    {
        public PeriodDto? ToDto(BenefitPeriod period)
        {
            var benefit = Catalog.FindBenefit(period.BenefitId);
            if (benefit is null || !Cards.TryGetValue(period.CardId, out var card))
            {
                return null;
            }

            return new PeriodDto(
                period.Id,
                card.Id,
                card.ProductName,
                card.PersonName,
                benefit.Id,
                benefit.Name,
                Catalog.NotesOf(benefit.Id),
                benefit.Detection == DetectionMode.Manual,
                period.Range.Start,
                period.Range.End,
                period.Amount.Dollars,
                period.Used.Dollars,
                period.Remaining.Dollars,
                period.StatusOn(Today).ToString(),
                period.Range.End.DayNumber - Today.DayNumber,
                period.HasDollarValue,
                period.Usage.Any(entry => entry.Source == UsageSource.Manual));
        }
    }
}
