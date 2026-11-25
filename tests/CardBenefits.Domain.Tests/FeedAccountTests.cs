namespace CardBenefits.Domain.Tests;

public class FeedAccountTests
{
    private static FeedAccount Account(string name = "Chase Sapphire Preferred (1234)") =>
        new(Guid.NewGuid(), "simplefin", "Chase Bank", "ACT-1", name, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Starts_in_the_new_accounts_queue()
    {
        Assert.Equal(FeedAccountStatus.New, Account().Status);
    }

    [Fact]
    public void Mapping_ties_it_to_a_card()
    {
        var account = Account();
        var cardId = Guid.NewGuid();

        account.MapTo(cardId);

        Assert.Equal(FeedAccountStatus.Mapped, account.Status);
        Assert.Equal(cardId, account.CardId);
    }

    [Fact]
    public void Dismissing_drops_any_card_and_reopening_returns_it_to_the_queue()
    {
        var account = Account();
        account.MapTo(Guid.NewGuid());

        account.Dismiss();
        Assert.Equal(FeedAccountStatus.Dismissed, account.Status);
        Assert.Null(account.CardId);

        account.Reopen();
        Assert.Equal(FeedAccountStatus.New, account.Status);
    }

    [Theory]
    [InlineData("Chase Sapphire Preferred (1234)", "1234")]
    [InlineData("Summit Visa Infinite- 5678 (5678)", "5678")]
    [InlineData("Family HSA", null)]
    [InlineData("529 Plan (5J7O)", null)]
    [InlineData("USDC Wallet (07943552-1ac0)", null)]
    public void Reads_the_last_four_digits_from_the_end_of_the_name(string name, string? expected)
    {
        Assert.Equal(expected, Account(name).LastFour);
    }
}
