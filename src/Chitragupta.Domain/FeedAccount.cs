namespace Chitragupta.Domain;

public enum FeedAccountStatus
{
    /// <summary>Seen in the feed and waiting for a person to say what it is.</summary>
    New,

    /// <summary>Tied to a card.</summary>
    Mapped,

    /// <summary>Not a card (checking, savings, a loan). Hidden from then on.</summary>
    Dismissed,
}

/// <summary>
/// An account as a feed reports it. Feed account ids are not stable: a re-linked bank
/// connection issues new ones, so the card is a separate record and this maps to it.
/// </summary>
public sealed class FeedAccount
{
    public FeedAccount(Guid id, string feed, string connectionName, string accountId, string name, DateTimeOffset firstSeenAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        Id = id;
        Feed = feed;
        ConnectionName = connectionName;
        AccountId = accountId;
        Name = name;
        FirstSeenAt = firstSeenAt;
    }

    public Guid Id { get; }

    /// <summary>Which feed reported the account, for example "simplefin" or "csv".</summary>
    public string Feed { get; }

    /// <summary>The institution or connection name the feed gave.</summary>
    public string ConnectionName { get; private set; }

    /// <summary>The feed's own id for the account.</summary>
    public string AccountId { get; }

    public string Name { get; private set; }

    public DateTimeOffset FirstSeenAt { get; }

    public FeedAccountStatus Status { get; private set; } = FeedAccountStatus.New;

    public Guid? CardId { get; private set; }

    /// <summary>The last four digits when the feed puts them at the end of the name, as in "Card (1234)".</summary>
    public string? LastFour
    {
        get
        {
            var name = Name.TrimEnd();
            if (name.Length >= 6 && name[^1] == ')' && name[^6] == '(' && name[^5..^1].All(char.IsAsciiDigit))
            {
                return name[^5..^1];
            }

            return null;
        }
    }

    public void Rename(string connectionName, string name)
    {
        ConnectionName = connectionName;
        Name = name;
    }

    public void MapTo(Guid cardId)
    {
        CardId = cardId;
        Status = FeedAccountStatus.Mapped;
    }

    public void Dismiss()
    {
        CardId = null;
        Status = FeedAccountStatus.Dismissed;
    }

    /// <summary>Puts a dismissed or mapped account back in the queue.</summary>
    public void Reopen()
    {
        CardId = null;
        Status = FeedAccountStatus.New;
    }

    public static FeedAccount Restore(
        Guid id,
        string feed,
        string connectionName,
        string accountId,
        string name,
        DateTimeOffset firstSeenAt,
        FeedAccountStatus status,
        Guid? cardId) =>
        new(id, feed, connectionName, accountId, name, firstSeenAt) { Status = status, CardId = cardId };
}
