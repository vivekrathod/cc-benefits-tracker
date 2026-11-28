namespace Chitragupta.Application.Ports;

/// <summary>
/// The one-time approval that lets the service send mail as somebody. A person gives it in a
/// browser, at the mail provider, and is sent back with a code that completes it.
/// </summary>
public interface IEmailSignIn
{
    EmailSignInState Read();

    /// <summary>Starts an approval. The answer is where to send the browser; it comes back to <paramref name="returnTo"/>.</summary>
    Uri Begin(Uri returnTo);

    /// <summary>Finishes the approval that <see cref="Begin"/> started, with what the browser came back with.</summary>
    Task CompleteAsync(string state, string code, CancellationToken cancellationToken);

    /// <summary>Throws the approval away. Mail goes another way, or nowhere, until a new one.</summary>
    void Forget();
}

/// <param name="CanSignIn">Whether everything an approval needs is set up.</param>
/// <param name="SignedIn">Whether an approval is held.</param>
/// <param name="Account">Whose mailbox sends, when the approval says.</param>
public sealed record EmailSignInState(bool CanSignIn, bool SignedIn, string? Account);
