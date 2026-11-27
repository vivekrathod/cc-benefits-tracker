using Chitragupta.Application.Balances;
using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Application.Reminders;
using Chitragupta.Application.Sync;
using Chitragupta.Infrastructure;
using Chitragupta.Infrastructure.Catalog;

namespace Chitragupta.Api;

/// <summary>
/// The background job. It wakes every few minutes and does whatever has come due:
/// the catalog refresh, the daily sync, rolling the periods forward, reminders, trimming old history, and the nightly backup.
/// Each step decides for itself whether it is due, so a restart never repeats or skips one.
/// </summary>
internal sealed class DailyWorker(
    CatalogRefresher catalog,
    ITransactionFeed feed,
    SyncService sync,
    Rematcher rematcher,
    ReminderService reminders,
    BalanceService balances,
    DatabaseBackup backup,
    IClock clock,
    ILogger<DailyWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private DateOnly? _periodsRolledOn;
    private DateOnly? _historyTrimmedOn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await StepAsync("catalog", RefreshCatalogAsync, stoppingToken);
            await StepAsync("sync", SyncAsync, stoppingToken);
            await StepAsync("periods", RollPeriodsAsync, stoppingToken);
            await StepAsync("reminders", token => reminders.RunAsync(token), stoppingToken);
            await StepAsync("history", TrimHistoryAsync, stoppingToken);
            await StepAsync("backup", token => backup.RunIfDueAsync(token), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>A catalog that changed can change any period, so everything is replayed against it.</summary>
    private async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        if (await catalog.RunIfDueAsync(cancellationToken))
        {
            await rematcher.RematchAllAsync(cancellationToken);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (!feed.IsConfigured)
        {
            return;
        }

        var run = await sync.RunDailyIfDueAsync(feed, cancellationToken);
        if (run is null)
        {
            return;
        }

        // Counts and status only. Descriptions and amounts stay out of the log.
        if (run.Succeeded)
        {
            logger.LogInformation(
                "Daily sync: {Accounts} accounts, {Received} transactions received, {Changed} new or changed, {Errors} institution errors.",
                run.AccountsSeen, run.TransactionsReceived, run.TransactionsChanged, run.Errors.Count);
        }
        else
        {
            logger.LogWarning("Daily sync failed: {Failure}", run.Failure);
        }
    }

    /// <summary>New periods start as days pass, with or without a sync, so the replay also runs once a day.</summary>
    private async Task RollPeriodsAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        if (_periodsRolledOn == today)
        {
            return;
        }

        await rematcher.RematchAllAsync(cancellationToken);
        _periodsRolledOn = today;
    }

    private async Task TrimHistoryAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        if (_historyTrimmedOn == today)
        {
            return;
        }

        var removed = await balances.TrimHistoryAsync(cancellationToken);
        _historyTrimmedOn = today;
        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} balance check attempts older than the history that is kept.", removed);
        }
    }

    private async Task StepAsync(string name, Func<CancellationToken, Task> step, CancellationToken stoppingToken)
    {
        try
        {
            await step(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            // One step failing must not stop the others or the next tick. Only the type and
            // message are logged: no stack of request details.
            logger.LogError("The {Step} step failed: {Type}: {Message}", name, failure.GetType().Name, failure.Message);
        }
    }
}
