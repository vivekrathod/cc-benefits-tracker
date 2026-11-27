using Chitragupta.Application.Matching;
using Chitragupta.Application.Ports;
using Chitragupta.Domain;

namespace Chitragupta.Application.Usage;

/// <summary>What a person enters by hand: the one-tap "used", a partial amount, and undo.</summary>
public sealed class UsageService(ITrackerStore store, Rematcher rematcher)
{
    /// <param name="amount">Part of the period, or null for everything that is left.</param>
    public async Task MarkUsedAsync(Guid periodId, decimal? amount, string? note, CancellationToken cancellationToken)
    {
        var period = await FindAsync(periodId, cancellationToken);
        try
        {
            if (amount is null)
            {
                period.MarkUsed(note);
            }
            else
            {
                period.Use(Money.FromDollars(amount.Value), UsageSource.Manual, null, note);
            }
        }
        catch (Exception rejected) when (rejected is ArgumentException or InvalidOperationException)
        {
            throw new RequestRejectedException(rejected.Message);
        }

        await SaveAsync(period, cancellationToken);
    }

    public async Task UndoManualAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var period = await FindAsync(periodId, cancellationToken);
        period.ClearManualUsage();
        await SaveAsync(period, cancellationToken);
    }

    private async Task SaveAsync(BenefitPeriod period, CancellationToken cancellationToken)
    {
        await store.SavePeriodsAsync([period], cancellationToken);

        // Manual usage counts against the cap, so matched credits may now land differently.
        await rematcher.RematchCardAsync(period.CardId, cancellationToken);
    }

    private async Task<BenefitPeriod> FindAsync(Guid periodId, CancellationToken cancellationToken) =>
        await store.FindPeriodAsync(periodId, cancellationToken) ?? throw new NotFoundException("The period");
}
