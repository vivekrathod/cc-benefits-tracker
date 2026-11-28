using Chitragupta.Application.Ports;
using Chitragupta.Contracts;

namespace Chitragupta.Application.Email;

/// <summary>Setting up, trying and switching off the mail that reminders and alerts go out by.</summary>
public sealed class EmailAlerts(IEmailSignIn signIn, INotifier notifier)
{
    public EmailAlertsDto Read()
    {
        var state = signIn.Read();
        return new EmailAlertsDto(state.CanSignIn, state.SignedIn, state.Account);
    }

    /// <summary>Where to send the browser for the approval. It returns to the address the app was opened at.</summary>
    public Uri BeginSignIn(string? returnTo) =>
        Uri.TryCreate(returnTo, UriKind.Absolute, out var address) && address.Scheme is "http" or "https"
            ? signIn.Begin(address)
            : throw new RequestRejectedException("The sign-in needs the address this app was opened at.");

    public async Task<EmailAlertsDto> CompleteSignInAsync(string? state, string? code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
        {
            throw new RequestRejectedException("The sign-in came back without its code. Start it again.");
        }

        await signIn.CompleteAsync(state, code, cancellationToken);
        return Read();
    }

    /// <summary>Sends one message the way every reminder goes, so a person sees it arrive.</summary>
    public async Task SendTestAsync(CancellationToken cancellationToken)
    {
        if (!signIn.Read().SignedIn)
        {
            throw new RequestRejectedException("Email alerts are not set up yet.");
        }

        try
        {
            await notifier.SendAsync(
                new Notification(
                    "Chitragupta can send you email",
                    "This is a test. Alerts about balance checks, and reminders about benefits and miles, arrive like this one.\n"),
                cancellationToken);
        }
        catch (InvalidOperationException failure)
        {
            // What a notifier says about a failed send is written for a person and names no secret.
            throw new RequestRejectedException(failure.Message);
        }
    }

    public void Disconnect() => signIn.Forget();
}
