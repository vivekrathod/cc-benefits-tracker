using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Balances;

/// <summary>
/// Decides when each account is checked and asks the automation, one account at a time.
/// It keeps nothing of its own: what is due is worked out from the history of attempts, so a
/// restart never repeats or skips a check. Three things make a check due, in this order:
/// a person asked, the daily time has passed with nothing heard today, or today's failure
/// is of a kind that may clear by itself and its one retry has waited long enough.
/// Two accounts of one program are kept as far apart in the day's order as the list allows.
/// A check a person asked for starts at once, unless a sign-in is running. The time the automation
/// lets pass between sign-ins holds back only the checks nobody is waiting for.
/// </summary>
public sealed class BalanceCheckRunner(
    IBalanceChecker checker,
    BalanceService balances,
    ITrackerStore store,
    BalanceCheckerState state,
    IClock clock,
    TrackerSettings settings)
{
    private static readonly TimeSpan AfterTrouble = TimeSpan.FromMinutes(15);

    /// <summary>For "check now", a reading this young is new enough, so one just taken by hand is not taken again.</summary>
    private static readonly TimeSpan NewEnough = TimeSpan.FromHours(1);

    public bool IsConfigured => checker.IsConfigured;

    /// <summary>Puts an account at the front of the line: "check now". The daily limit on sign-ins still holds.</summary>
    public async Task RequestAsync(string account, CancellationToken cancellationToken)
    {
        if (!checker.IsConfigured)
        {
            throw new RequestRejectedException("No balance automation is connected, so there is nothing to ask.");
        }

        var known = state.Read();
        if (known.UnreachableSince is not null)
        {
            throw new RequestRejectedException("The balance automation is not answering, so the check cannot run now.");
        }

        if (!known.Accounts.Contains(account))
        {
            throw new NotFoundException("The account");
        }

        if (await SignInsTodayAsync(account, cancellationToken) >= settings.BalanceSignInsPerDay)
        {
            throw new RequestRejectedException(
                $"{settings.BalanceSignInsPerDay} sign-ins have already been tried for {account} today. "
                + "That is the daily limit, which keeps the account clear of lockouts.");
        }

        state.Request(account);
    }

    /// <summary>Does the one check that is due, if any. Safe to call as often as the host likes.</summary>
    public async Task<BalanceCheckRun?> RunDueAsync(CancellationToken cancellationToken)
    {
        if (!checker.IsConfigured)
        {
            return null;
        }

        IReadOnlyList<CheckerAccount> accounts;
        try
        {
            accounts = await checker.GetAccountsAsync(cancellationToken);
        }
        catch (BalanceCheckerUnavailableException down)
        {
            state.Unreachable(clock.Now, down.Message);
            return null;
        }

        var now = clock.Now;
        state.Reachable(accounts.Select(account => account.Name).ToArray(), now);

        var known = state.Read();
        var unasked = known.Pause?.Holds(now, requested: false) != true;
        if (known.Pause?.Holds(now, requested: true) == true
            || await NextAsync(accounts, known.Requested, unasked, now, cancellationToken) is not (var account, var kind, var maxAge))
        {
            return null;
        }

        state.Started(account);
        try
        {
            var answer = await checker.CheckAsync(account, maxAge, asked: kind == BalanceCheckKind.Requested, cancellationToken);
            if (answer.Report is null)
            {
                state.Deferred(new BalancePause(answer.NotBefore ?? now + AfterTrouble, answer.Why));
                return null;
            }

            var line = (await balances.ImportAsync(answer.Report, cancellationToken)).FirstOrDefault(line => line.Account == account);
            state.Finished(account);

            var latest = (await store.GetBalanceChecksAsync(cancellationToken)).FirstOrDefault(check => check.Account == account);
            return new BalanceCheckRun(kind, latest?.Program ?? "", line?.Status ?? "Unknown", latest?.ErrorCode, line?.Recorded ?? false);
        }
        catch (Exception failure) when (failure is BalanceCheckerUnavailableException or RequestRejectedException)
        {
            // Whether a sign-in happened is not known, so nothing is asked for a while. A person's request is dropped
            // and not kept for later: a sign-in nobody is waiting for any more would only be a surprise.
            state.Finished(account, failure.Message, new BalancePause(now + AfterTrouble, BalanceWait.Trouble));
            return null;
        }
        catch
        {
            state.Finished(account);
            throw;
        }
    }

    /// <summary>
    /// The account to ask about, why, and how old a reading may be to do without a sign-in: anything
    /// read today for the day's check, anything read since the failure for a retry.
    /// With <paramref name="unasked"/> false, only what a person asked for is looked at.
    /// </summary>
    private async Task<(string Account, BalanceCheckKind Kind, TimeSpan MaxAge)?> NextAsync(
        IReadOnlyList<CheckerAccount> accounts, IReadOnlyList<string> requested, bool unasked, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var attempts = await store.GetBalanceAttemptsAsync(now - now.TimeOfDay, cancellationToken);
        var histories = CheckHistory.ByAccount(attempts).ToDictionary(history => history.Account);
        int SignIns(string account) => histories.TryGetValue(account, out var history) ? history.SignInsOn(today, clock) : 0;

        foreach (var account in requested)
        {
            if (accounts.Any(known => known.Name == account) && SignIns(account) < settings.BalanceSignInsPerDay)
            {
                return (account, BalanceCheckKind.Requested, NewEnough);
            }

            state.Drop(account);
        }

        if (!unasked)
        {
            return null;
        }

        if (TimeOnly.FromTimeSpan(now.TimeOfDay) >= settings.BalanceCheckAt
            && Spread(accounts.Where(account => !histories.ContainsKey(account.Name)).ToArray(), accounts, attempts) is { } unheard)
        {
            return (unheard.Name, BalanceCheckKind.Scheduled, now.TimeOfDay);
        }

        foreach (var account in accounts)
        {
            if (histories.TryGetValue(account.Name, out var history)
                && history.Latest is { NeedsAttention: true, Retryable: true, SignedIn: true } failed
                && SignIns(account.Name) < settings.BalanceSignInsPerDay
                && now - failed.CheckedAt >= TimeSpan.FromHours(settings.BalanceRetryAfterHours))
            {
                // Just short of the failure, so only a reading taken after it will do.
                return (account.Name, BalanceCheckKind.Retry, now - failed.CheckedAt - TimeSpan.FromSeconds(1));
            }
        }

        return null;
    }

    /// <summary>
    /// Which of the accounts still to check goes next. A program's site should not see two sign-ins in
    /// a row, so the program checked last waits when another can go, and the program with the most
    /// accounts left goes first: that is what leaves room to deal the rest apart.
    /// </summary>
    private static CheckerAccount? Spread(
        IReadOnlyList<CheckerAccount> due, IReadOnlyList<CheckerAccount> accounts, IReadOnlyList<BalanceCheck> attemptsToday)
    {
        var last = attemptsToday.Count == 0
            ? null
            : accounts.FirstOrDefault(account => account.Name == attemptsToday[^1].Account)?.Program;
        return due
            .OrderBy(account => account.Program == last)
            .ThenByDescending(account => due.Count(other => other.Program == account.Program))
            .FirstOrDefault();
    }

    private async Task<int> SignInsTodayAsync(string account, CancellationToken cancellationToken)
    {
        var now = clock.Now;
        return (await store.GetBalanceAttemptsAsync(now - now.TimeOfDay, cancellationToken))
            .Count(attempt => attempt.Account == account && attempt.SignedIn);
    }
}

public enum BalanceCheckKind
{
    Scheduled,
    Retry,
    Requested,
}

/// <summary>What one run of the checker did, for the log: a program and a status, never whose account it was.</summary>
public sealed record BalanceCheckRun(BalanceCheckKind Kind, string Program, string Status, string? ErrorCode, bool Recorded);
