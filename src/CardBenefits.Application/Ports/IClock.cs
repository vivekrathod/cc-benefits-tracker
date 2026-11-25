namespace CardBenefits.Application.Ports;

/// <summary>The only source of "now". Dates are in the household's time zone.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    DateOnly Today { get; }

    /// <summary>Converts an instant, such as a feed timestamp, to the household's local date.</summary>
    DateOnly ToLocalDate(DateTimeOffset instant);
}
