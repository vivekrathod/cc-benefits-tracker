using CardBenefits.Application.Tests.Support;
using CardBenefits.Contracts;

namespace CardBenefits.Application.Tests;

public class BalanceTests
{
    private const string Aadvantage = "American AAdvantage";

    private static CancellationToken Ct => Tracker.Ct;

    [Fact]
    public async Task A_reported_balance_is_kept_for_the_person_its_owner_names()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 10:00");
        var alex = await tracker.PersonAsync("Alex");

        var lines = await tracker.Balances.ImportAsync(
            Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T14:00:00+00:00", expires: "2028-03-14"), Ct);

        Assert.Equal([new BalanceImportLineDto("alex-aa", "Ok", true, null)], lines);
        var balance = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances);
        Assert.Equal(
            (alex.Id, "Alex", Aadvantage, 45_210L, "Automation", (DateOnly?)DateOnly.Parse("2028-03-14")),
            (balance.PersonId, balance.PersonName, balance.Program, balance.Amount, balance.Source, balance.ExpiresOn));
        // Read two days ago, in the household's own time zone.
        Assert.Equal((2, DateTimeOffset.Parse("2026-11-25T09:00:00-05:00")), (balance.AgeDays, balance.AsOf));
        Assert.Equal((473, false), (balance.DaysUntilExpiry, balance.ExpiresSoon));
    }

    [Fact]
    public async Task The_same_result_sent_twice_is_kept_once()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Alex");
        var result = Udaan.Ok("alex-alaska", "alaska", "alex", 1_900, "2026-11-25T14:00:00+00:00");

        await tracker.Balances.ImportAsync(result, Ct);
        var again = await tracker.Balances.ImportAsync(result, Ct);

        Assert.Equal([new BalanceImportLineDto("alex-alaska", "Ok", false, null)], again);
        Assert.Equal("Alaska Atmos Rewards", Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances).Program);
    }

    [Fact]
    public async Task The_newest_reading_is_the_balance_whatever_order_they_arrive_in()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 10:00");
        await tracker.PersonAsync("Alex");

        await tracker.Balances.ImportAsync(Udaan.Ok("alex-united", "united", "alex", 7_000, "2026-11-26T12:00:00+00:00"), Ct);
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-united", "united", "alex", 7_500, "2026-11-27T12:00:00+00:00"), Ct);
        var late = await tracker.Balances.ImportAsync(Udaan.Ok("alex-united", "united", "alex", 6_000, "2026-11-20T12:00:00+00:00"), Ct);

        Assert.False(late.Single().Recorded);
        var balance = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances);
        Assert.Equal(("United MileagePlus", 7_500L, 0), (balance.Program, balance.Amount, balance.AgeDays));
    }

    [Fact]
    public async Task Several_results_at_once_each_go_to_their_own_person()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Alex");
        await tracker.PersonAsync("Sam Rivera");

        var lines = await tracker.Balances.ImportAsync(
            Udaan.All(
                Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T14:00:00+00:00"),
                Udaan.Ok("sam-aa", "aa", "sam", 310, "2026-11-25T14:20:00+00:00")),
            Ct);

        Assert.All(lines, line => Assert.True(line.Recorded));
        var balances = (await tracker.Queries.GetBalancesAsync(Ct)).Balances;
        Assert.Equal(
            [("Alex", 45_210L), ("Sam Rivera", 310L)],
            balances.Select(balance => (balance.PersonName, balance.Amount)));
    }

    [Fact]
    public async Task A_failed_check_keeps_the_last_balance_and_says_what_is_wrong_until_a_check_works_again()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 10:00");
        await tracker.PersonAsync("Alex");
        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-25T14:00:00+00:00"), Ct);

        var failed = await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", "needs_user", "the sign-in form came back; not retrying", "2026-11-26T14:00:00+00:00"), Ct);

        Assert.Equal([new BalanceImportLineDto("alex-aa", "NeedsUser", false, "the sign-in form came back; not retrying")], failed);
        var view = await tracker.Queries.GetBalancesAsync(Ct);
        Assert.Equal((45_210L, 2), (view.Balances.Single().Amount, view.Balances.Single().AgeDays));
        Assert.Equal(
            [new BalanceCheckDto("alex-aa", "alex", Aadvantage, "NeedsUser", "the sign-in form came back; not retrying", DateTimeOffset.Parse("2026-11-26T09:00:00-05:00"))],
            view.Checks);
        Assert.Equal(1, (await tracker.Queries.GetSummaryAsync(tracker.Feed.Name, Ct)).BalanceProblems);

        await tracker.Balances.ImportAsync(Udaan.Ok("alex-aa", "aa", "alex", 45_900, "2026-11-27T14:00:00+00:00"), Ct);

        view = await tracker.Queries.GetBalancesAsync(Ct);
        Assert.Equal(45_900L, view.Balances.Single().Amount);
        Assert.Equal(("Ok", null), (view.Checks.Single().Status, view.Checks.Single().Problem));
        Assert.Equal(0, (await tracker.Queries.GetSummaryAsync(tracker.Feed.Name, Ct)).BalanceProblems);
    }

    [Fact]
    public async Task A_failed_check_that_carries_the_last_good_reading_still_delivers_it()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Alex");
        var lastGood = Udaan.Ok("alex-aa", "aa", "alex", 45_210, "2026-11-20T14:00:00+00:00", expires: "2028-03-14");

        var lines = await tracker.Balances.ImportAsync(
            Udaan.Failed("alex-aa", "aa", "alex", "blocked", "site challenge", "2026-11-25T14:00:00+00:00", lastGood), Ct);

        Assert.Equal([new BalanceImportLineDto("alex-aa", "Blocked", true, "site challenge")], lines);
        var balance = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances);
        Assert.Equal((45_210L, (DateOnly?)DateOnly.Parse("2028-03-14"), 5), (balance.Amount, balance.ExpiresOn, balance.AgeDays));
    }

    [Fact]
    public async Task A_result_for_an_owner_nobody_goes_by_waits_and_never_makes_a_person()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Alex");

        var lines = await tracker.Balances.ImportAsync(Udaan.Ok("jo-aa", "aa", "jo", 12, "2026-11-25T14:00:00+00:00"), Ct);

        var line = Assert.Single(lines);
        Assert.Equal(("UnknownOwner", false), (line.Status, line.Recorded));
        Assert.Contains("\"jo\"", line.Problem);
        var view = await tracker.Queries.GetBalancesAsync(Ct);
        Assert.Empty(view.Balances);
        Assert.Equal("UnknownOwner", view.Checks.Single().Status);
        Assert.Single(await tracker.Store.GetPeopleAsync(Ct));

        // Once somebody goes by that name, the same result lands.
        await tracker.PersonAsync("Jo");
        Assert.True((await tracker.Balances.ImportAsync(Udaan.Ok("jo-aa", "aa", "jo", 12, "2026-11-25T14:00:00+00:00"), Ct)).Single().Recorded);
        Assert.Equal("Ok", (await tracker.Queries.GetBalancesAsync(Ct)).Checks.Single().Status);
    }

    [Fact]
    public async Task A_first_name_is_enough_only_while_one_person_has_it()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Sam Rivera");
        await tracker.PersonAsync("Sam Okafor");

        var line = (await tracker.Balances.ImportAsync(Udaan.Ok("sam-aa", "aa", "sam", 12, "2026-11-25T14:00:00+00:00"), Ct)).Single();

        Assert.Equal(("UnknownOwner", false), (line.Status, line.Recorded));
    }

    [Fact]
    public async Task A_program_the_tracker_cannot_read_is_reported_and_nothing_is_kept()
    {
        await using var tracker = await Tracker.StartAsync();
        await tracker.PersonAsync("Alex");

        var line = (await tracker.Balances.ImportAsync(Udaan.Ok("alex-delta", "delta", "alex", 12, "2026-11-25T14:00:00+00:00"), Ct)).Single();

        Assert.Equal(("UnknownProgram", false), (line.Status, line.Recorded));
        Assert.Empty((await tracker.Queries.GetBalancesAsync(Ct)).Balances);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "ok": false, "status": "usage", "error": "no account matches" }""")]
    public async Task Text_that_is_not_a_result_is_refused(string body)
    {
        await using var tracker = await Tracker.StartAsync();

        await Assert.ThrowsAsync<RequestRejectedException>(() => tracker.Balances.ImportAsync(body, Ct));
    }

    [Fact]
    public async Task A_balance_typed_in_by_hand_replaces_the_one_before_it_under_the_same_name()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 10:00");
        var alex = await tracker.PersonAsync("Alex");

        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Chase Ultimate Rewards", 80_000, null), Ct);
        tracker.Clock.Advance(TimeSpan.FromDays(3));
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, " chase ultimate rewards ", 92_500, DateOnly.Parse("2027-01-31")), Ct);

        var balance = Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances);
        Assert.Equal(
            ("Chase Ultimate Rewards", 92_500L, "Manual", 0, (DateOnly?)DateOnly.Parse("2027-01-31")),
            (balance.Program, balance.Amount, balance.Source, balance.AgeDays, balance.ExpiresOn));
    }

    [Fact]
    public async Task A_balance_by_hand_needs_a_person_a_program_and_a_sensible_number()
    {
        await using var tracker = await Tracker.StartAsync();
        var alex = await tracker.PersonAsync("Alex");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            tracker.Balances.RecordAsync(new RecordBalanceRequest(Guid.NewGuid(), "Avios", 1, null), Ct));
        await Assert.ThrowsAsync<RequestRejectedException>(() =>
            tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "  ", 1, null), Ct));
        await Assert.ThrowsAsync<RequestRejectedException>(() =>
            tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", -5, null), Ct));
    }

    [Fact]
    public async Task Removing_a_balance_forgets_every_reading_of_it_and_leaves_the_others()
    {
        await using var tracker = await Tracker.StartAsync();
        var alex = await tracker.PersonAsync("Alex");
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 100, null), Ct);
        tracker.Clock.Advance(TimeSpan.FromHours(1));
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 200, null), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Flying Blue", 300, null), Ct);

        await tracker.Balances.RemoveAsync(alex.Id, "avios", Ct);

        Assert.Equal("Flying Blue", Assert.Single((await tracker.Queries.GetBalancesAsync(Ct)).Balances).Program);
        await Assert.ThrowsAsync<NotFoundException>(() => tracker.Balances.RemoveAsync(alex.Id, "Avios", Ct));
    }

    [Fact]
    public async Task Miles_that_lapse_inside_the_expiring_soon_window_are_counted_with_the_benefits()
    {
        await using var tracker = await Tracker.StartAsync("2026-11-27 10:00");
        var alex = await tracker.PersonAsync("Alex");
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Avios", 100, DateOnly.Parse("2026-12-05")), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Flying Blue", 100, DateOnly.Parse("2027-02-01")), Ct);
        await tracker.Balances.RecordAsync(new RecordBalanceRequest(alex.Id, "Empty", 0, DateOnly.Parse("2026-12-01")), Ct);

        var balances = (await tracker.Queries.GetBalancesAsync(Ct)).Balances;

        // Soonest to lapse first within a person, then by name.
        Assert.Equal(
            [("Avios", (int?)8, true), ("Flying Blue", 66, true), ("Empty", null, false)],
            balances.Select(balance => (balance.Program, balance.DaysUntilExpiry, balance.ExpiresSoon)));
        Assert.Equal(1, (await tracker.Queries.GetSummaryAsync(tracker.Feed.Name, Ct)).ExpiringSoon);
    }
}
