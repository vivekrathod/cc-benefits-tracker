using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure.Notifications;

/// <summary>
/// Picks how a reminder goes out at the moment it is sent: through Gmail once its token file is
/// there, else by SMTP when that is configured, else to the log. So signing in to Gmail takes
/// effect without a restart.
/// </summary>
internal sealed class FirstReadyNotifier(GmailNotifier gmail, SmtpNotifier? smtp, LogNotifier log) : INotifier
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken) =>
        (gmail.IsReady ? gmail : smtp ?? (INotifier)log).SendAsync(notification, cancellationToken);
}
