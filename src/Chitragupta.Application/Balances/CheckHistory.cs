using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Balances;

/// <summary>One account's attempts, oldest first, and what they add up to.</summary>
internal sealed record CheckHistory(string Account, IReadOnlyList<BalanceCheck> Attempts)
{
    public BalanceCheck Latest => Attempts[^1];

    /// <summary>When the run of failures the account is in began, or null while it is ok.</summary>
    public DateTimeOffset? FailingSince => Latest.NeedsAttention ? Attempts[StartOfRun(Attempts.Count - 1)].CheckedAt : null;

    /// <summary>When the run of failures that ended with the latest success began, or null when there was none.</summary>
    public DateTimeOffset? RecoveredFrom
    {
        get
        {
            var index = Attempts.Count - 1;
            while (index >= 0 && !Attempts[index].NeedsAttention)
            {
                index--;
            }

            return Latest.NeedsAttention || index < 0 ? null : Attempts[StartOfRun(index)].CheckedAt;
        }
    }

    public IEnumerable<BalanceCheck> On(DateOnly day, IClock clock) =>
        Attempts.Where(attempt => clock.ToLocalDate(attempt.CheckedAt) == day);

    /// <summary>How many sign-ins were tried on a day. A refusal is not one.</summary>
    public int SignInsOn(DateOnly day, IClock clock) => On(day, clock).Count(attempt => attempt.SignedIn);

    public static IReadOnlyList<CheckHistory> ByAccount(IEnumerable<BalanceCheck> attempts) =>
        attempts
            .GroupBy(attempt => attempt.Account)
            .Select(account => new CheckHistory(account.Key, account.OrderBy(attempt => attempt.CheckedAt).ToArray()))
            .ToArray();

    /// <summary>A check's status in plain words, as a reminder says it.</summary>
    public static string Words(BalanceCheckStatus status) => status switch
    {
        BalanceCheckStatus.Ok => "ok",
        BalanceCheckStatus.NeedsUser => "needs you",
        BalanceCheckStatus.Blocked => "blocked by the site",
        BalanceCheckStatus.UnknownOwner => "unknown owner",
        BalanceCheckStatus.UnknownProgram => "unknown program",
        _ => "failed",
    };

    private int StartOfRun(int index)
    {
        while (index > 0 && Attempts[index - 1].NeedsAttention)
        {
            index--;
        }

        return index;
    }
}
