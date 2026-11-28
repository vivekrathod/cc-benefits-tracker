using Chitragupta.Application.Ports;

namespace Chitragupta.Application.Balances;

/// <summary>
/// What is known right now about the automation that can be asked for checks: whether it
/// answers, what is waiting, what is running. It lives in memory only. Everything worth
/// keeping is in the history of attempts.
/// </summary>
public sealed class BalanceCheckerState
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _asked = new(0);
    private readonly List<string> _requested = [];
    private IReadOnlyList<string> _accounts = [];
    private DateTimeOffset? _unreachableSince;
    private DateTimeOffset? _reachableSince;
    private BalancePause? _pause;
    private string? _problem;
    private string? _running;

    public Snapshot Read()
    {
        lock (_gate)
        {
            return new Snapshot(_accounts, _requested.ToArray(), _running, _unreachableSince, _reachableSince, _pause, _problem);
        }
    }

    internal void Reachable(IReadOnlyList<string> accounts, DateTimeOffset now)
    {
        lock (_gate)
        {
            _accounts = accounts;
            _reachableSince ??= now;
            if (_unreachableSince is not null)
            {
                (_unreachableSince, _problem) = (null, null);
            }
        }
    }

    internal void Unreachable(DateTimeOffset now, string problem)
    {
        lock (_gate)
        {
            _unreachableSince ??= now;
            _reachableSince = null;
            _problem = problem;
            // A check somebody asked for is not kept for whenever the automation returns: by then nobody is waiting for it.
            _requested.Clear();
        }
    }

    /// <summary>Adds an account to the "check now" queue. False when it is already waiting or running.</summary>
    internal bool Request(string account)
    {
        lock (_gate)
        {
            if (_running == account || _requested.Contains(account))
            {
                return false;
            }

            _requested.Add(account);
            _asked.Release();
            return true;
        }
    }

    /// <summary>Waits until somebody asks for a check, or for the given time, whichever comes first.</summary>
    public Task WaitForRequestAsync(TimeSpan atMost, CancellationToken cancellationToken) => _asked.WaitAsync(atMost, cancellationToken);

    internal void Started(string account)
    {
        lock (_gate)
        {
            _running = account;
        }
    }

    /// <summary>The automation said when to come back. The account stays in the queue.</summary>
    internal void Deferred(BalancePause pause)
    {
        lock (_gate)
        {
            (_running, _pause) = (null, pause);
        }
    }

    internal void Finished(string account, string? problem = null, BalancePause? pause = null)
    {
        lock (_gate)
        {
            _running = null;
            _requested.Remove(account);
            _problem = problem;
            _pause = pause ?? _pause;
        }
    }

    internal void Drop(string account)
    {
        lock (_gate)
        {
            _requested.Remove(account);
        }
    }

    public sealed record Snapshot(
        IReadOnlyList<string> Accounts,
        IReadOnlyList<string> Requested,
        string? Running,
        DateTimeOffset? UnreachableSince,
        DateTimeOffset? ReachableSince,
        BalancePause? Pause,
        string? Problem);
}

/// <summary>Nothing is asked of the automation until a moment, for a reason.</summary>
public sealed record BalancePause(DateTimeOffset Until, BalanceWait Why)
{
    /// <summary>
    /// The time the automation lets pass between sign-ins is for checks nobody is waiting for.
    /// Every other pause holds a check a person asked for as well.
    /// </summary>
    public bool HoldsRequests => Why != BalanceWait.Spacing;

    public bool Holds(DateTimeOffset now, bool requested) => Until > now && (HoldsRequests || !requested);
}
