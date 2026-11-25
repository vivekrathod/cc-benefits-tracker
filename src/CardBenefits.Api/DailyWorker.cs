using CardBenefits.Application.Matching;
using CardBenefits.Application.Ports;
using CardBenefits.Application.Reminders;
using CardBenefits.Application.Sync;
using CardBenefits.Infrastructure;

namespace CardBenefits.Api;

/// <summary>
/// The background job. It wakes every few minutes and does whatever has come due:
/// the daily sync, rolling the periods forward, reminders, and the nightly backup.
/// Each step decides for itself whether it is due, so a restart never repeats or skips one.
/// </summary>
internal sealed class DailyWorker(
    ITransactionFeed feed,
    SyncService sync,
    Rematcher rematcher,
    ReminderService reminders,
    DatabaseBackup backup,
    IClock clock,
    ILogger<DailyWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private DateOnly? _periodsRolledOn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await StepAsync("sync", SyncAsync, stoppingToken);
            await StepAsync("periods", RollPeriodsAsync, stoppingToken);
            await StepAsync("reminders", token => reminders.RunAsync(token), stoppingToken);
            await StepAsync("backup", token => backup.RunIfDueAsync(token), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
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
