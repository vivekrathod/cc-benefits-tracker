using System.Net;
using System.Net.Mail;
using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure.Notifications;

/// <summary>Reminders by email. The password is read from a file each time, never from configuration.</summary>
internal sealed class SmtpNotifier(SmtpOptions options) : INotifier
{
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        using var message = new MailMessage { From = new MailAddress(options.From), Subject = notification.Subject, Body = notification.Body };
        foreach (var recipient in options.To)
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
}
