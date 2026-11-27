using System.Net;
using System.Net.Mail;
using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure.Notifications;

/// <summary>Reminders by email. The password is read from a file each time, never from configuration.</summary>
internal sealed class SmtpNotifier(SmtpOptions options) : INotifier
{
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        using var message = new MailMessage { From = Sender(options.From), Subject = notification.Subject, Body = notification.Body };
        foreach (var recipient in options.Recipients)
        {
            message.To.Add(recipient);
        }

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.UseTls };
        if (!string.IsNullOrWhiteSpace(options.User))
        {
            var password = string.IsNullOrWhiteSpace(options.PasswordFile) ? "" : File.ReadAllText(options.PasswordFile).Trim();
            client.Credentials = new NetworkCredential(options.User, password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }

    /// <summary>The From address, shown as "Chitragupta" unless the setting already carries a name.</summary>
    internal static MailAddress Sender(string from)
    {
        var address = new MailAddress(from);
        return address.DisplayName.Length > 0 ? address : new MailAddress(address.Address, "Chitragupta");
    }
}
