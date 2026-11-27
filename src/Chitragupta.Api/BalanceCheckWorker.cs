using Chitragupta.Application.Balances;

namespace Chitragupta.Api;

/// <summary>
/// Asks the balance automation for whatever check has come due: every minute, and at once when
/// somebody presses "Check now". The automation itself keeps sign-ins much further apart than
/// that and says "not now" in between.
/// With no automation connected this does nothing, and results can still be pushed in.
/// </summary>
internal sealed class BalanceCheckWorker(
    BalanceCheckRunner runner, BalanceCheckerState state, ILogger<BalanceCheckWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!runner.IsConfigured)
        {
            logger.LogInformation("No balance automation is connected (Udaan:Url is empty). Balances arrive pushed or typed in.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await runner.RunDueAsync(stoppingToken) is { } run)
                {
                    // A program and a status. Whose account it was stays out of the log.
                    logger.LogInformation(
                        "Balance check ({Kind}) of {Program}: {Status}{Code}, reading {Reading}.",
                        run.Kind, run.Program, run.Status, run.ErrorCode is null ? "" : $" ({run.ErrorCode})", run.Recorded ? "kept" : "unchanged");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure)
            {
                logger.LogError("The balance check step failed: {Type}: {Message}", failure.GetType().Name, failure.Message);
            }

            try
            {
                await state.WaitForRequestAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
