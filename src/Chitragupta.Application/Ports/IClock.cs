namespace Chitragupta.Application.Ports;

/// <summary>The only source of "now". Dates and times of day are in the household's time zone.</summary>
public interface IClock
{
    /// <summary>The current instant, expressed in the household's time zone.</summary>
    DateTimeOffset Now { get; }

    DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Re-expresses an instant, such as a stored timestamp, in the household's time zone.</summary>
    DateTimeOffset ToLocal(DateTimeOffset instant);

    DateOnly ToLocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);
}
