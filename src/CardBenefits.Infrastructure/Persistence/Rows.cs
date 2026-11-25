using CardBenefits.Domain;

namespace CardBenefits.Infrastructure.Persistence;

// The tables. Rows are plain data: the store maps them to and from the domain,
// so the domain never learns how it is stored. Money is whole cents.

internal sealed class PersonRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class CardRow
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public string? LastFour { get; set; }

    public DateOnly OpenedOn { get; set; }

    public DateOnly? ClosedOn { get; set; }

    public int AnniversaryMonth { get; set; }

    public int AnniversaryDay { get; set; }

    public long? AnnualFeeCents { get; set; }

    public List<CardProductRow> Products { get; set; } = [];
}

internal sealed class CardProductRow
{
    public int Id { get; set; }

    public Guid CardId { get; set; }

    public string ProductId { get; set; } = "";

    public DateOnly From { get; set; }
}

internal sealed class FeedAccountRow
{
    public Guid Id { get; set; }

    public string Feed { get; set; } = "";

    public string ConnectionName { get; set; } = "";

    public string AccountId { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset FirstSeenAt { get; set; }

    public FeedAccountStatus Status { get; set; }

    public Guid? CardId { get; set; }
}

internal sealed class TransactionRow
{
    public Guid Id { get; set; }

    public Guid FeedAccountId { get; set; }

    public string FeedId { get; set; } = "";

    public DateOnly? PostedOn { get; set; }

    public DateOnly? TransactedOn { get; set; }

    public long AmountCents { get; set; }

    public string Description { get; set; } = "";

    public bool Pending { get; set; }

    public string RawPayload { get; set; } = "";

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastChangedAt { get; set; }
}

internal sealed class PeriodRow
{
    public Guid Id { get; set; }

    public Guid CardId { get; set; }

    public string BenefitId { get; set; } = "";

    public DateOnly Start { get; set; }

    public DateOnly End { get; set; }

    public long AmountCents { get; set; }

    public List<UsageRow> Usage { get; set; } = [];
}

internal sealed class UsageRow
{
    public int Id { get; set; }

    public Guid PeriodId { get; set; }

    public int Position { get; set; }

    public long AmountCents { get; set; }

    public UsageSource Source { get; set; }

    public Guid? TransactionId { get; set; }

    public string? Note { get; set; }
}

internal sealed class ReviewItemRow
{
    public Guid Id { get; set; }

    public Guid CardId { get; set; }

    public Guid TransactionId { get; set; }

    public ReviewReason Reason { get; set; }

    public long AmountCents { get; set; }

    public string? BenefitId { get; set; }
}

internal sealed class PinRow
{
    public Guid TransactionId { get; set; }

    public string? BenefitId { get; set; }
}

internal sealed class AcknowledgedFlagRow
{
    public Guid TransactionId { get; set; }

    public ReviewReason Reason { get; set; }
}

internal sealed class LearnedRuleRow
{
    public int Id { get; set; }

    public string BenefitId { get; set; } = "";

    public string Keyword { get; set; } = "";

    public long? AmountCeilingCents { get; set; }
}

internal sealed class IgnoreKeywordRow
{
    public string Keyword { get; set; } = "";
}

internal sealed class SyncRunRow
{
    public Guid Id { get; set; }

    public string Feed { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public DateOnly WindowStart { get; set; }

    public DateOnly WindowEnd { get; set; }

    public int AccountsSeen { get; set; }

    public int TransactionsReceived { get; set; }

    public int TransactionsChanged { get; set; }

    public DateOnly? NewestReturned { get; set; }

    public DateOnly? NewestStored { get; set; }

    public string? Failure { get; set; }

    public List<SyncErrorRow> Errors { get; set; } = [];
}

internal sealed class SyncErrorRow
{
    public int Id { get; set; }

    public Guid SyncRunId { get; set; }

    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    public string? ConnectionName { get; set; }
}

internal sealed class ReminderRow
{
    public string Key { get; set; } = "";

    public DateTimeOffset SentAt { get; set; }
}

/// <summary>
/// Reserved for phase 2 (points). A balance is a dated snapshot with where it came
/// from: automation, manual or estimate. Nothing writes to it in v1.
/// </summary>
internal sealed class BalanceSnapshotRow
{
    public int Id { get; set; }

    public Guid PersonId { get; set; }

    public string Program { get; set; } = "";

    public long Balance { get; set; }

    public DateTimeOffset AsOf { get; set; }

    public string Source { get; set; } = "";
}
