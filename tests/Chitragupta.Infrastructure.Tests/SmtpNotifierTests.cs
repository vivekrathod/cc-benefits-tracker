using Chitragupta.Infrastructure.Notifications;

namespace Chitragupta.Infrastructure.Tests;

public sealed class SmtpNotifierTests
{
    [Fact]
    public void Reminders_arrive_from_Chitragupta_when_the_address_has_no_name()
    {
        var sender = SmtpNotifier.Sender("reminders@example.com");

        Assert.Equal("Chitragupta", sender.DisplayName);
        Assert.Equal("reminders@example.com", sender.Address);
    }

    [Fact]
    public void A_name_given_with_the_address_is_kept()
    {
        var sender = SmtpNotifier.Sender("Home <reminders@example.com>");

        Assert.Equal("Home", sender.DisplayName);
        Assert.Equal("reminders@example.com", sender.Address);
    }
}
