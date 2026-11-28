using Chitragupta.Application.Feeds;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Tests.Support;

internal sealed class FakeClock : IClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-5);

    public DateTimeOffset Now { get; private set; } = At("2026-11-25 10:00");

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset instant) => instant.ToOffset(Offset);

    public void Set(string localTime) => Now = At(localTime);

    public void Advance(TimeSpan by) => Now += by;

    private static DateTimeOffset At(string localTime) => new(DateTime.Parse(localTime), Offset);
}

/// <summary>A feed whose next answer the test sets up: accounts, errors, or an outage.</summary>
internal sealed class FakeFeed : ITransactionFeed
{
    private readonly Dictionary<string, FeedAccountData> _accounts = [];

    public string Name => "simplefin";

    public bool IsConfigured { get; set; } = true;

    public List<FeedError> Errors { get; } = [];

    public string? Outage { get; set; }

    public List<DateRange> Requests { get; } = [];

    public FakeFeed Account(string id, string name, string connection = "Chase", params FeedTransaction[] transactions)
    {
        _accounts[id] = new FeedAccountData(connection, id, name, transactions);
        return this;
    }

    public void Remove(string id) => _accounts.Remove(id);

    public Task<FeedSnapshot> FetchAsync(DateRange window, CancellationToken cancellationToken)
    {
        Requests.Add(window);
        return Outage is null
            ? Task.FromResult(new FeedSnapshot(_accounts.Values.ToArray(), Errors.ToArray()))
            : throw new FeedUnavailableException(Outage);
    }

    public static FeedTransaction Posted(string id, string postedOn, decimal amount, string description) =>
        new(id, DateOnly.Parse(postedOn), DateOnly.Parse(postedOn), Money.FromDollars(amount), description, false, "{}");

    public static FeedTransaction Pending(string id, string transactedOn, decimal amount, string description) =>
        new(id, null, DateOnly.Parse(transactedOn), Money.FromDollars(amount), description, true, "{}");
}

/// <summary>The balance automation as a test sets it up: which accounts it has, and what it answers when asked.</summary>
internal sealed class FakeChecker : IBalanceChecker
{
    public bool IsConfigured { get; set; }

    public List<string> Accounts { get; } = [];

    /// <summary>Set to make the automation not answer at all.</summary>
    public string? Outage { get; set; }

    public List<(string Account, TimeSpan MaxAge)> Asked { get; } = [];

    /// <summary>The accounts asked about on a person's behalf.</summary>
    public List<string> AskedFor { get; } = [];

    public Func<string, BalanceCheckAnswer> Answer { get; set; } = _ => throw new InvalidOperationException("The test set up no answer.");

    /// <summary>Accounts are named "owner-program", as udaan names them.</summary>
    public Task<IReadOnlyList<CheckerAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
        Outage is null
            ? Task.FromResult<IReadOnlyList<CheckerAccount>>(Accounts.Select(name => new CheckerAccount(name, name.Split('-')[^1])).ToArray())
            : throw new BalanceCheckerUnavailableException(Outage);

    public Task<BalanceCheckAnswer> CheckAsync(string account, TimeSpan maxAge, bool asked, CancellationToken cancellationToken)
    {
        Asked.Add((account, maxAge));
        if (asked)
        {
            AskedFor.Add(account);
        }

        return Task.FromResult(Answer(account));
    }
}

internal sealed class RecordingNotifier : INotifier
{
    public List<Notification> Sent { get; } = [];

    public bool Broken { get; set; }

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (Broken)
        {
            throw new InvalidOperationException("The mail server is down.");
        }

        Sent.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>A small catalog with one of each kind of benefit the tracker handles.</summary>
internal sealed class TestCatalog : ICatalog
{
    public const string Gold = "gold";
    public const string Dining = "gold:dining";
    public const string Resy = "gold:resy";
    public const string Uber = "gold:uber";
    public const string Hotel = "gold:hotel";
    public const string Lounge = "gold:lounge";
    public const string Preferred = "sapphire-preferred";
    public const string Reserve = "sapphire-reserve";
    public const string Travel = "sapphire-reserve:travel";
    public const string Plain = "plain";

    private static readonly DateOnly Always = new(2000, 1, 1);

    private readonly BenefitDefinition[] _benefits =
    [
        Benefit(Dining, Gold, "Dining credit", DetectionMode.Automatic, 10, Cadence.Monthly, YearBasis.Calendar),
        Benefit(Resy, Gold, "Resy credit", DetectionMode.Automatic, 50, Cadence.Semiannual, YearBasis.Calendar),
        Benefit(Uber, Gold, "Uber Cash", DetectionMode.Manual, 10, Cadence.Monthly, YearBasis.Calendar),
        Benefit(Hotel, Gold, "Hotel credit", DetectionMode.Automatic, 100, Cadence.Annual, YearBasis.Cardmember),
        Benefit(Lounge, Gold, "Lounge passes", DetectionMode.Manual, 0, Cadence.Annual, YearBasis.Cardmember),
        Benefit(Travel, Reserve, "Travel credit", DetectionMode.Automatic, 300, Cadence.Annual, YearBasis.Cardmember),
    ];

    public IReadOnlyList<CardProduct> Products { get; } =
    [
        new(Gold, "Amex", "Gold", Money.FromDollars(325), PointsProgram: "Membership Rewards"),
        new(Preferred, "Chase", "Sapphire Preferred", Money.FromDollars(95)),
        new(Reserve, "Chase", "Sapphire Reserve", Money.FromDollars(795)),
        new(Plain, "Chase", "Freedom Flex", Money.Zero),
    ];

    public IReadOnlyList<MatchRule> Rules { get; } =
    [
        new(Dining, "DINING CREDIT", Money.FromDollars(10)),
        new(Resy, "RESY", Money.FromDollars(50)),
        new(Hotel, "hotel", Money.FromDollars(100), Verified: false),
        new(Travel, "TRAVEL CREDIT", Money.FromDollars(300)),
    ];

    public IReadOnlyList<string> IgnoreKeywords { get; } = ["PAYMENT THANK YOU"];

    public IReadOnlyDictionary<string, int> GraceDays { get; } = new Dictionary<string, int>();

    public CardProduct? FindProduct(string productId) => Products.FirstOrDefault(product => product.Id == productId);

    public IReadOnlyList<BenefitDefinition> BenefitsOf(string productId) =>
        _benefits.Where(benefit => benefit.ProductId == productId).ToArray();

    public BenefitDefinition? FindBenefit(string benefitId) => _benefits.FirstOrDefault(benefit => benefit.Id == benefitId);

    public IReadOnlyList<string> AliasesOf(string productId) => productId == Reserve ? ["CSR"] : [];

    public string? NotesOf(string benefitId) => null;

    private static BenefitDefinition Benefit(
        string id, string productId, string name, DetectionMode detection, decimal amount, Cadence cadence, YearBasis basis) =>
        new(id, productId, name, detection, [new BenefitTerms(Always, Money.FromDollars(amount), cadence, basis)]);
}
