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
    /// answered from the automation's memory, without a sign-in.
    /// </summary>
    Task<BalanceCheckAnswer> CheckAsync(string account, TimeSpan maxAge, CancellationToken cancellationToken);
}

/// <summary>An account by the automation's own name for it, and its word for the program the account is with.</summary>
public sealed record CheckerAccount(string Name, string Program);

/// <summary>Either the automation's result, as it words it, or the moment from which it can be asked again.</summary>
public sealed record BalanceCheckAnswer(string? Report, DateTimeOffset? NotBefore)
{
    public static BalanceCheckAnswer Result(string report) => new(report, null);

    public static BalanceCheckAnswer ComeBackAt(DateTimeOffset moment) => new(null, moment);
}

/// <summary>The automation did not answer, or answered something that is not a result.</summary>
public sealed class BalanceCheckerUnavailableException(string message, bool reached = false) : Exception(message)
{
    /// <summary>True when the automation is there but its answer was not usable.</summary>
    public bool Reached { get; } = reached;
}
