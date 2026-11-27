using CardBenefits.Application;
using CardBenefits.Application.Balances;
using CardBenefits.Application.Cards;
using CardBenefits.Application.Matching;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Review;
using CardBenefits.Application.Statements;
using CardBenefits.Application.Sync;
using CardBenefits.Application.Usage;
using CardBenefits.Application.Views;
using CardBenefits.Contracts;
using CardBenefits.Infrastructure.Catalog;

namespace CardBenefits.Api;

/// <summary>The HTTP surface. Every route is one call into a use case and nothing more.</summary>
internal static class TrackerApi
{
    private const int LargestStatementBytes = 5 * 1024 * 1024;
    private const int LargestBalanceReportBytes = 1024 * 1024;

    public static void MapTrackerApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(TranslateFailuresAsync);

        // Views
        api.MapGet("/summary", (TrackerQueries queries, ITransactionFeed feed, CancellationToken ct) =>
            queries.GetSummaryAsync(feed.Name, ct));
        api.MapGet("/expiring", (int? days, TrackerQueries queries, CancellationToken ct) =>
            queries.GetExpiringSoonAsync(days, ct));
        api.MapGet("/this-period", (TrackerQueries queries, CancellationToken ct) => queries.GetThisPeriodAsync(ct));
        api.MapGet("/card-value", (TrackerQueries queries, CancellationToken ct) => queries.GetCardValueAsync(ct));

        // Manual usage
        api.MapPost("/periods/{id:guid}/use", async (Guid id, UsePeriodRequest request, UsageService usage, CancellationToken ct) =>
        {
            await usage.MarkUsedAsync(id, request.Amount, request.Note, ct);
            return Results.NoContent();
        });
        api.MapDelete("/periods/{id:guid}/manual-usage", async (Guid id, UsageService usage, CancellationToken ct) =>
        {
            await usage.UndoManualAsync(id, ct);
            return Results.NoContent();
        });

        // Review queue
        api.MapGet("/review", (ReviewService review, CancellationToken ct) => review.GetQueueAsync(ct));
        api.MapPost("/review/{id:guid}/assign", async (Guid id, AssignReviewRequest request, ReviewService review, CancellationToken ct) =>
        {
            await review.AssignAsync(id, request.BenefitId, request.Keyword, ct);
            return Results.NoContent();
        });
        api.MapPost("/review/{id:guid}/dismiss", async (Guid id, DismissReviewRequest request, ReviewService review, CancellationToken ct) =>
        {
            await review.DismissAsync(id, request.IgnoreKeyword, ct);
            return Results.NoContent();
        });
        api.MapPost("/review/{id:guid}/acknowledge", async (Guid id, ReviewService review, CancellationToken ct) =>
        {
            await review.AcknowledgeAsync(id, ct);
            return Results.NoContent();
        });

        // Feed accounts
        api.MapGet("/accounts", (CardService cards, CancellationToken ct) => cards.GetAccountsAsync(ct));
        api.MapGet("/accounts/new", (CardService cards, CancellationToken ct) => cards.GetNewAccountsAsync(ct));
        api.MapPost("/accounts/{id:guid}/confirm", async (Guid id, ConfirmCardRequest request, CardService cards, CancellationToken ct) =>
            Results.Ok((await cards.ConfirmCardAsync(id, request, ct)).Id));
        api.MapPost("/accounts/{id:guid}/map", async (Guid id, MapAccountRequest request, CardService cards, CancellationToken ct) =>
        {
            await cards.MapAsync(id, request.CardId, ct);
            return Results.NoContent();
        });
        api.MapPost("/accounts/dismiss", async (DismissAccountsRequest request, CardService cards, CancellationToken ct) =>
        {
            await cards.DismissAsync(request.Ids, ct);
            return Results.NoContent();
        });
        api.MapPost("/accounts/{id:guid}/reopen", async (Guid id, CardService cards, CancellationToken ct) =>
        {
            await cards.ReopenAsync(id, ct);
            return Results.NoContent();
        });

        // People, cards and the catalog
        api.MapGet("/people", (TrackerQueries queries, CancellationToken ct) => queries.GetPeopleAsync(ct));
        api.MapPost("/people", async (AddPersonRequest request, CardService cards, CancellationToken ct) =>
        {
            var person = await cards.AddPersonAsync(request.Name, ct);
            return Results.Ok(new PersonDto(person.Id, person.Name));
        });
        api.MapPut("/people/{id:guid}", async (Guid id, AddPersonRequest request, CardService cards, CancellationToken ct) =>
        {
            var person = await cards.RenamePersonAsync(id, request.Name, ct);
            return Results.Ok(new PersonDto(person.Id, person.Name));
        });
        api.MapGet("/cards", (TrackerQueries queries, CancellationToken ct) => queries.GetCardsAsync(ct));
        api.MapPost("/cards", async (AddCardRequest request, CardService cards, CancellationToken ct) =>
            Results.Ok((await cards.AddCardAsync(request, ct)).Id));
        api.MapPut("/cards/{id:guid}", async (Guid id, UpdateCardRequest request, CardService cards, CancellationToken ct) =>
        {
            await cards.UpdateAsync(id, request, ct);
            return Results.NoContent();
        });
        api.MapPost("/cards/{id:guid}/product", async (Guid id, ChangeProductRequest request, CardService cards, CancellationToken ct) =>
        {
            await cards.ChangeProductAsync(id, request.ProductId, request.On, ct);
            return Results.NoContent();
        });
        api.MapPost("/cards/{id:guid}/close", async (Guid id, CloseCardRequest request, CardService cards, CancellationToken ct) =>
        {
            await cards.CloseAsync(id, request.On, ct);
            return Results.NoContent();
        });
        api.MapDelete("/cards/{id:guid}", async (Guid id, CardService cards, CancellationToken ct) =>
        {
            await cards.RemoveAsync(id, ct);
            return Results.NoContent();
        });
        api.MapPost("/cards/{id:guid}/statements", async (Guid id, HttpRequest request, StatementImportService statements, CancellationToken ct) =>
        {
            // The body is the CSV export itself, as text.
            if (request.ContentLength > LargestStatementBytes)
            {
                throw new RequestRejectedException("The file is larger than 5 MB.");
            }

            using var reader = new StreamReader(request.Body);
            var run = await statements.ImportAsync(id, await reader.ReadToEndAsync(ct), ct);
            return Results.Ok(new ImportResultDto(run.TransactionsReceived, run.TransactionsChanged));
        });
        api.MapGet("/catalog/products", (TrackerQueries queries) => queries.GetProducts());
        api.MapGet("/catalog/products/{id}/benefits", (string id, TrackerQueries queries) => queries.GetBenefits(id));
        api.MapGet("/catalog/status", (LiveCatalog catalog, CatalogRefresher refresher) => ToDto(catalog.Status, refresher));
        api.MapPost("/catalog/refresh", async (LiveCatalog catalog, CatalogRefresher refresher, Rematcher rematcher, CancellationToken ct) =>
        {
            if (!catalog.FollowsSource)
            {
                throw new RequestRejectedException("A fixed catalog file is configured, so there is no source to refresh from.");
            }

            if (await refresher.RunAsync(ct))
            {
                await rematcher.RematchAllAsync(ct);
            }

            return ToDto(catalog.Status, refresher);
        });

        // Miles and points
        api.MapGet("/balances", (TrackerQueries queries, CancellationToken ct) => queries.GetBalancesAsync(ct));
        api.MapPost("/balances", async (RecordBalanceRequest request, BalanceService balances, CancellationToken ct) =>
        {
            await balances.RecordAsync(request, ct);
            return Results.NoContent();
        });
        api.MapDelete("/balances", async (Guid personId, string program, BalanceService balances, CancellationToken ct) =>
        {
            await balances.RemoveAsync(personId, program, ct);
            return Results.NoContent();
        });
        api.MapPost("/balances/udaan", async (HttpRequest request, BalanceService balances, CancellationToken ct) =>
        {
            // The body is what "udaan check" printed, as it is. One address serves every account:
            // each result says whose it is.
            if (request.ContentLength > LargestBalanceReportBytes)
            {
                throw new RequestRejectedException("The result is larger than 1 MB.");
            }

            using var reader = new StreamReader(request.Body);
            return Results.Ok(await balances.ImportAsync(await reader.ReadToEndAsync(ct), ct));
        });

        // Sync
        api.MapGet("/sync", async (TrackerQueries queries, SyncService sync, ITransactionFeed feed, CancellationToken ct) =>
            await queries.GetSyncHealthAsync(
                feed.Name, feed.IsConfigured, await sync.NextManualSyncAsync(feed.Name, ct), ct));
        api.MapPost("/sync", async (SyncService sync, ITransactionFeed feed, TrackerQueries queries, CancellationToken ct) =>
        {
            if (!feed.IsConfigured)
            {
                throw new RequestRejectedException("The SimpleFIN access URL file is not set up yet, so there is nothing to sync from.");
            }

            var (run, retryAfter) = await sync.RunNowAsync(feed, ct);
            return new SyncNowResponse(run is not null, retryAfter, run is null ? null : queries.ToDto(run));
        });
        api.MapPost("/replay", async (Rematcher rematcher, CancellationToken ct) =>
        {
            await rematcher.RematchAllAsync(ct);
            return Results.NoContent();
        });
    }

    private static CatalogStatusDto ToDto(CatalogStatus status, CatalogRefresher refresher) => new(
        status.FollowsSource,
        refresher.IsEnabled,
        status.Source,
        status.Products,
        status.RetrievedAt,
        status.CheckedAt,
        status.Problem,
        status.Flags);

    /// <summary>Turns the two failures a use case can signal into the status codes a client expects.</summary>
    private static async ValueTask<object?> TranslateFailuresAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (NotFoundException missing)
        {
            return Results.Problem(missing.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (RequestRejectedException rejected)
        {
            return Results.Problem(rejected.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
