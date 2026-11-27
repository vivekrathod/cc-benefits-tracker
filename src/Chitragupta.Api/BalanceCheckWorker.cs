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

        var answering = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var run = await runner.RunDueAsync(stoppingToken);

                // Said once each way, so an outage is one line in the log and not one a minute.
                var known = state.Read();
                if (answering != known.UnreachableSince is null)
                {
                    answering = !answering;
                    if (answering)
                    {
                        logger.LogInformation("The balance automation answers again, with {Accounts} accounts.", known.Accounts.Count);
                    }
                    else
                    {
                        logger.LogWarning("The balance automation is not answering: {Problem}", known.Problem);
                    }
                }

                if (run is not null)
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
