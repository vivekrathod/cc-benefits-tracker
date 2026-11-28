using Chitragupta.Application.Email;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Tests.Support;
using Chitragupta.Contracts;

namespace Chitragupta.Application.Tests;

public sealed class EmailAlertsTests
{
    private readonly FakeSignIn _signIn = new();
    private readonly RecordingNotifier _notifier = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EmailAlerts Email => new(_signIn, _notifier);

    [Fact]
    public async Task A_test_message_goes_out_the_way_every_reminder_does()
    {
        _signIn.State = new EmailSignInState(true, true, "home@example.com");

        await Email.SendTestAsync(Ct);

        Assert.Equal("Chitragupta can send you email", Assert.Single(_notifier.Sent).Subject);
        Assert.Equal(new EmailAlertsDto(true, true, "home@example.com"), Email.Read());
    }

    [Fact]
    public async Task There_is_no_test_message_before_anybody_approved_sending()
    {
        await Assert.ThrowsAsync<RequestRejectedException>(() => Email.SendTestAsync(Ct));

        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task A_test_message_that_fails_says_why()
    {
        _signIn.State = new EmailSignInState(true, true, null);
        _notifier.Broken = true;

        var failure = await Assert.ThrowsAsync<RequestRejectedException>(() => Email.SendTestAsync(Ct));

        Assert.Equal("The mail server is down.", failure.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("manage")]
    [InlineData("file:///etc/hosts")]
    public void A_sign_in_starts_only_from_an_address_a_browser_can_come_back_to(string? returnTo)
    {
        Assert.Throws<RequestRejectedException>(() => Email.BeginSignIn(returnTo));
        Assert.Null(_signIn.ReturnTo);

        Assert.Equal("https://accounts.example/ask", Email.BeginSignIn("http://localhost:8090/").ToString());
        Assert.Equal(new Uri("http://localhost:8090/"), _signIn.ReturnTo);
    }

    [Fact]
    public async Task Coming_back_without_a_code_completes_nothing()
    {
        await Assert.ThrowsAsync<RequestRejectedException>(() => Email.CompleteSignInAsync("state", " ", Ct));
        Assert.Null(_signIn.Completed);

        await Email.CompleteSignInAsync("state", "code", Ct);
        Assert.Equal(("state", "code"), _signIn.Completed);
    }

    private sealed class FakeSignIn : IEmailSignIn
    {
        public EmailSignInState State { get; set; } = new(true, false, null);

        public Uri? ReturnTo { get; private set; }

        public (string State, string Code)? Completed { get; private set; }

        public EmailSignInState Read() => State;

        public Uri Begin(Uri returnTo)
        {
            ReturnTo = returnTo;
            return new Uri("https://accounts.example/ask");
        }

        public Task CompleteAsync(string state, string code, CancellationToken cancellationToken)
        {
            Completed = (state, code);
            return Task.CompletedTask;
        }

        public void Forget() => State = State with { SignedIn = false, Account = null };
    }
}
