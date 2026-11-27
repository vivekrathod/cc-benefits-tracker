using CardBenefits.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CardBenefits.Infrastructure.Persistence;

internal sealed class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<PersonRow> People => Set<PersonRow>();

    public DbSet<CardRow> Cards => Set<CardRow>();

    public DbSet<FeedAccountRow> FeedAccounts => Set<FeedAccountRow>();

    public DbSet<TransactionRow> Transactions => Set<TransactionRow>();

    public DbSet<PeriodRow> Periods => Set<PeriodRow>();

    public DbSet<UsageRow> Usage => Set<UsageRow>();

    public DbSet<ReviewItemRow> ReviewItems => Set<ReviewItemRow>();

    public DbSet<PinRow> Pins => Set<PinRow>();

    public DbSet<AcknowledgedFlagRow> AcknowledgedFlags => Set<AcknowledgedFlagRow>();

    public DbSet<LearnedRuleRow> LearnedRules => Set<LearnedRuleRow>();

    public DbSet<IgnoreKeywordRow> IgnoreKeywords => Set<IgnoreKeywordRow>();

    public DbSet<SyncRunRow> SyncRuns => Set<SyncRunRow>();

    public DbSet<ReminderRow> Reminders => Set<ReminderRow>();

    public DbSet<BalanceSnapshotRow> BalanceSnapshots => Set<BalanceSnapshotRow>();

    public DbSet<BalanceCheckRow> BalanceChecks => Set<BalanceCheckRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // SQLite cannot sort or compare a DateTimeOffset, so instants are stored as UTC milliseconds.
        configuration.Properties<DateTimeOffset>().HaveConversion<InstantConverter>();

        // Names, not numbers, so reordering an enum never changes what a stored row means.
        configuration.Properties<FeedAccountStatus>().HaveConversion<string>();
        configuration.Properties<UsageSource>().HaveConversion<string>();
        configuration.Properties<ReviewReason>().HaveConversion<string>();
        configuration.Properties<BalanceSource>().HaveConversion<string>();
        configuration.Properties<BalanceCheckStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PersonRow>().ToTable("People");

        model.Entity<CardRow>(card =>
        {
            card.ToTable("Cards");
            card.HasMany(row => row.Products).WithOne().HasForeignKey(product => product.CardId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CardProductRow>().ToTable("CardProducts");

        model.Entity<FeedAccountRow>(account =>
        {
            account.ToTable("FeedAccounts");
            account.HasIndex(row => new { row.Feed, row.AccountId }).IsUnique();
        });

        model.Entity<TransactionRow>(transaction =>
        {
            transaction.ToTable("Transactions");
            transaction.HasIndex(row => new { row.FeedAccountId, row.FeedId }).IsUnique();
        });

        model.Entity<PeriodRow>(period =>
        {
            period.ToTable("Periods");
            period.HasIndex(row => row.CardId);
            period.HasMany(row => row.Usage).WithOne().HasForeignKey(usage => usage.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<UsageRow>().ToTable("Usage");

        model.Entity<ReviewItemRow>(item =>
        {
            item.ToTable("ReviewItems");
            item.HasIndex(row => row.CardId);
        });

        model.Entity<PinRow>(pin =>
        {
            pin.ToTable("Pins");
            pin.HasKey(row => row.TransactionId);
        });

        model.Entity<AcknowledgedFlagRow>(flag =>
        {
            flag.ToTable("AcknowledgedFlags");
            flag.HasKey(row => new { row.TransactionId, row.Reason });
        });

        model.Entity<LearnedRuleRow>().ToTable("LearnedRules");

        model.Entity<IgnoreKeywordRow>(keyword =>
        {
            keyword.ToTable("IgnoreKeywords");
            keyword.HasKey(row => row.Keyword);
        });

        model.Entity<SyncRunRow>(run =>
        {
            run.ToTable("SyncRuns");
            run.HasIndex(row => row.StartedAt);
            run.HasMany(row => row.Errors).WithOne().HasForeignKey(error => error.SyncRunId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SyncErrorRow>().ToTable("SyncErrors");

        model.Entity<ReminderRow>(reminder =>
        {
            reminder.ToTable("Reminders");
            reminder.HasKey(row => row.Key);
        });

        model.Entity<BalanceSnapshotRow>(balance =>
        {
            balance.ToTable("BalanceSnapshots");
            balance.HasIndex(row => new { row.PersonId, row.Program });
        });

        model.Entity<BalanceCheckRow>(check =>
        {
            check.ToTable("BalanceChecks");
            check.HasKey(row => row.Account);
        });
    }

    private sealed class InstantConverter() : ValueConverter<DateTimeOffset, long>(
        instant => instant.ToUnixTimeMilliseconds(),
        milliseconds => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
}
