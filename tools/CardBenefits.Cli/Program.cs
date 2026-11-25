using CardBenefits.Application;
using CardBenefits.Application.Cards;
using CardBenefits.Application.Matching;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Statements;
using CardBenefits.Application.Sync;
using CardBenefits.Application.Views;
using CardBenefits.Cli;
using CardBenefits.Domain;
using CardBenefits.Infrastructure;
using CardBenefits.Infrastructure.Catalog;
using CardBenefits.Infrastructure.Feeds.SimpleFin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The operator's tool. It prints counts and status, never transaction details or secrets.

const string Usage = """
    tracker <command> [--data <folder>]

      status                          What is stored: people, cards, accounts, transactions, queues.
      cards                           The cards with their ids.
      import-cards <seed.json>        Add people and cards from a seed file.
      import-csv <card> <file.csv>    Backfill a card from an issuer export. <card> is an id or the last four digits.
      import-pull <file.json>         Load a saved SimpleFIN response as if it had just been synced.
      link-suggested                  Link new feed accounts to existing cards where exactly one card fits.
      sync                            Pull from the SimpleFIN Bridge now.
      replay                          Rebuild periods and matching from the stored transactions.
      claim <access-url-file>         Exchange a setup token (read from standard input) for the access URL file.
      import-catalog <dataset.json> <catalog.json>
                                      Convert the community benefits dataset into this project's catalog.

    Settings come from environment variables such as Tracker__DataDirectory; --data overrides the data folder.
    """;

var arguments = args.ToList();
string? dataDirectory = null;
var dataFlag = arguments.IndexOf("--data");
if (dataFlag >= 0 && dataFlag + 1 < arguments.Count)
{
    dataDirectory = arguments[dataFlag + 1];
    arguments.RemoveRange(dataFlag, 2);
}

if (arguments.Count == 0)
{
    Console.WriteLine(Usage);
    return 1;
}

try
{
    // These two need no database.
    switch (arguments[0])
    {
        case "import-catalog" when arguments.Count == 3:
            await using (var dataset = File.OpenRead(arguments[1]))
            {
                await File.WriteAllTextAsync(arguments[2], DatasetImporter.Convert(dataset));
            }

            Console.WriteLine($"Wrote {arguments[2]}.");
            return 0;

        case "claim" when arguments.Count == 2:
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                var token = await Console.In.ReadToEndAsync();
                await SimpleFinClaim.ClaimAsync(http, token, arguments[1], CancellationToken.None);
            }

            Console.WriteLine("Claimed. The access URL was written to the file and not shown.");
            return 0;
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    if (dataDirectory is not null)
    {
        builder.Configuration["Tracker:DataDirectory"] = dataDirectory;
    }

    builder.Services.AddTracker(builder.Configuration);
    using var host = builder.Build();
    var services = host.Services;
    await services.MigrateTrackerDatabaseAsync();

    var store = services.GetRequiredService<ITrackerStore>();
    var clock = services.GetRequiredService<IClock>();
    var none = CancellationToken.None;

    switch (arguments[0])
    {
        case "status":
            await PrintStatusAsync();
            return 0;

        case "cards":
            foreach (var card in await services.GetRequiredService<TrackerQueries>().GetCardsAsync(none))
            {
                var closed = card.ClosedOn is null ? "" : $", closed {card.ClosedOn:yyyy-MM-dd}";
                Console.WriteLine($"{card.Id}  {card.PersonName}: {card.ProductName} {card.LastFour}{closed} [{card.LinkedAccounts.Count} linked]");
            }

            return 0;

        case "import-cards" when arguments.Count == 2:
        {
            var (added, skipped) = await CardSeed.Read(arguments[1]).ApplyAsync(
                services.GetRequiredService<CardService>(), services.GetRequiredService<TrackerQueries>(), none);
            Console.WriteLine($"Cards added: {added}. Already there: {skipped}.");
            return 0;
        }

        case "import-csv" when arguments.Count == 3:
        {
            var cards = await store.GetCardsAsync(none);
            var matches = cards
                .Where(card => card.Id.ToString() == arguments[1] || (card.LastFour is not null && card.LastFour == arguments[1]))
                .ToArray();
            if (matches.Length != 1)
            {
                Console.Error.WriteLine($"'{arguments[1]}' fits {matches.Length} cards. Use an id from 'tracker cards'.");
                return 1;
            }

            var run = await services.GetRequiredService<StatementImportService>()
                .ImportAsync(matches[0].Id, await File.ReadAllTextAsync(arguments[2]), none);
            Console.WriteLine($"Rows read: {run.TransactionsReceived}. New or changed: {run.TransactionsChanged}.");
            return 0;
        }

        case "import-pull" when arguments.Count == 2:
        {
            var feed = new SavedPullFeed(arguments[1], clock);
            var window = new DateRange(clock.Today.AddDays(-89), clock.Today);
            return Report(await services.GetRequiredService<SyncService>().RunAsync(feed, window, null, none));
        }

        case "sync":
        {
            var feed = services.GetRequiredService<ITransactionFeed>();
            var sync = services.GetRequiredService<SyncService>();
            return Report(await sync.RunAsync(feed, await sync.NextWindowAsync(feed.Name, none), null, none));
        }

        case "link-suggested":
        {
            var cardService = services.GetRequiredService<CardService>();
            var queue = await cardService.GetNewAccountsAsync(none);

            // Only the unambiguous: one candidate card for the account, and no other account wanting that card.
            var certain = queue
                .Where(account => account.SuggestedCardIds.Count == 1)
                .GroupBy(account => account.SuggestedCardIds[0])
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .ToArray();
            foreach (var account in certain)
            {
                await cardService.MapAsync(account.Id, account.SuggestedCardIds[0], none);
            }

            Console.WriteLine($"Linked: {certain.Length}. Still waiting for a decision: {queue.Count - certain.Length}.");
            return 0;
        }

        case "replay":
            await services.GetRequiredService<Rematcher>().RematchAllAsync(none);
            await PrintStatusAsync();
            return 0;

        default:
            Console.WriteLine(Usage);
            return 1;
    }

    int Report(SyncRun run)
    {
        if (!run.Succeeded)
        {
            Console.Error.WriteLine($"Failed: {run.Failure}");
            return 2;
        }

        Console.WriteLine(
            $"Accounts: {run.AccountsSeen}. Transactions received: {run.TransactionsReceived}, new or changed: {run.TransactionsChanged}. " +
            $"Newest returned: {run.NewestReturned:yyyy-MM-dd}. Institutions reporting an error: {run.Errors.Count}.");
        return 0;
    }

    async Task PrintStatusAsync()
    {
        var accounts = await store.GetFeedAccountsAsync(none);
        var periods = await store.GetPeriodsAsync(null, none);
        var review = await store.GetReviewItemsAsync(none);
        var transactions = 0;
        foreach (var account in accounts)
        {
            transactions += (await store.GetTransactionsAsync(account.Id, none)).Count;
        }

        Console.WriteLine($"People: {(await store.GetPeopleAsync(none)).Count}");
        Console.WriteLine($"Cards: {(await store.GetCardsAsync(none)).Count}");
        Console.WriteLine(
            $"Feed accounts: {accounts.Count} (new {accounts.Count(a => a.Status == FeedAccountStatus.New)}, " +
            $"linked {accounts.Count(a => a.Status == FeedAccountStatus.Mapped)}, dismissed {accounts.Count(a => a.Status == FeedAccountStatus.Dismissed)})");
        Console.WriteLine($"Transactions stored: {transactions}");
        Console.WriteLine(
            $"Periods: {periods.Count} (with usage {periods.Count(p => p.Usage.Count > 0)}, " +
            $"matched automatically {periods.Count(p => p.Usage.Any(u => u.Source == UsageSource.Automatic))})");
        Console.WriteLine(
            "Review queue: " + (review.Count == 0
                ? "empty"
                : string.Join(", ", review.GroupBy(item => item.Reason).Select(group => $"{group.Key} {group.Count()}"))));
    }
}
catch (Exception failure) when (failure is RequestRejectedException or NotFoundException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine(failure.Message);
    return 1;
}
