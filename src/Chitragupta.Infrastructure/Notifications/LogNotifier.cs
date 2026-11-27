using Chitragupta.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Chitragupta.Infrastructure.Notifications;

/// <summary>Stands in until email is configured: the reminder is written to the log instead of sent.</summary>
internal sealed class LogNotifier(ILogger<LogNotifier> logger) : INotifier
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reminder (email is not configured, so it was not sent): {Subject}", notification.Subject);
        return Task.CompletedTask;
    }
}
