namespace Chitragupta.Application.Ports;

/// <summary>
/// The automation that signs in to loyalty programs, when it can be asked. The tracker decides
/// when to ask; the automation holds the logins and the rules that keep an account safe, and
/// may answer "not now".
/// </summary>
public interface IBalanceChecker
{
    bool IsConfigured { get; }

    /// <summary>The accounts the automation can check.</summary>
    Task<IReadOnlyList<CheckerAccount>> GetAccountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks for one account's balance. A reading no older than <paramref name="maxAge"/> may be
    /// answered from the automation's memory, without a sign-in. <paramref name="asked"/> says a person
    /// asked and is waiting: the automation then signs in at once, where for a check on a schedule it
    /// may first let some time pass since its last sign-in.
    /// </summary>
    Task<BalanceCheckAnswer> CheckAsync(string account, TimeSpan maxAge, bool asked, CancellationToken cancellationToken);
}

/// <summary>An account by the automation's own name for it, and its word for the program the account is with.</summary>
public sealed record CheckerAccount(string Name, string Program);

/// <summary>Either the automation's result, as it words it, or the moment from which it can be asked again, and why not sooner.</summary>
public sealed record BalanceCheckAnswer(string? Report, DateTimeOffset? NotBefore, BalanceWait Why = BalanceWait.Unsaid)
{
    public static BalanceCheckAnswer Result(string report) => new(report, null);

    public static BalanceCheckAnswer ComeBackAt(DateTimeOffset moment, BalanceWait why = BalanceWait.Unsaid) => new(null, moment, why);
}

/// <summary>Why a check has to wait.</summary>
public enum BalanceWait
{
    /// <summary>The automation asked to wait and did not say why.</summary>
    Unsaid,

    /// <summary>A sign-in the tracker did not ask for is running, and only one runs at a time.</summary>
    Busy,

    /// <summary>The automation lets time pass between sign-ins nobody is waiting for. A check a person asked for does not wait for this.</summary>
    Spacing,

    /// <summary>The automation's last answer could not be used, so whether it signed in is not known.</summary>
    Trouble,
}

/// <summary>The automation did not answer, or answered something that is not a result.</summary>
public sealed class BalanceCheckerUnavailableException(string message, bool reached = false) : Exception(message)
{
    /// <summary>True when the automation is there but its answer was not usable.</summary>
    public bool Reached { get; } = reached;
}
