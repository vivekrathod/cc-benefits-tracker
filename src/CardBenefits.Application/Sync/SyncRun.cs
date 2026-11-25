using CardBenefits.Domain;

namespace CardBenefits.Application.Sync;

/// <summary>The record of one sync: what was asked for, what came back, and what went wrong.</summary>
public sealed record SyncRun(
    Guid Id,
    string Feed,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    DateRange Window,
    int AccountsSeen,
    int TransactionsReceived,
    int TransactionsChanged,
    DateOnly? NewestReturned,
    DateOnly? NewestStored,
    string? Failure,
    IReadOnlyList<SyncError> Errors)
{
    /// <summary>The request itself worked. Individual institutions may still have reported errors.</summary>
    public bool Succeeded => Failure is null;
}

public sealed record SyncError(string Code, string Message, string? ConnectionName);
