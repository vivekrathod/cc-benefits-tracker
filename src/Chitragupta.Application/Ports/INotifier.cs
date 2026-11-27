namespace Chitragupta.Application.Ports;

/// <summary>Sends a reminder to the household: email, push, or whatever is configured.</summary>
public interface INotifier
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(string Subject, string Body);
