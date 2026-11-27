using System.Globalization;

namespace Chitragupta.Web;

internal static class Format
{
    public static string Money(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString(amount == decimal.Truncate(amount) ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);

    public static string Day(DateOnly day) => day.ToString("MMM d", CultureInfo.InvariantCulture);

    public static string Day(DateOnly? day) => day is null ? "" : Day(day.Value);

    public static string LongDay(DateOnly day) => day.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset? instant) =>
        instant is null ? "never" : instant.Value.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);

    public static string DaysLeft(int days) => days switch
    {
        < 0 => "ended",
        0 => "ends today",
        1 => "ends tomorrow",
        _ => $"{days} days left",
    };

    /// <summary>A count of miles or points.</summary>
    public static string Count(long amount) => amount.ToString("#,0", CultureInfo.InvariantCulture);

    public static string Age(int days) => days switch
    {
        <= 0 => "today",
        1 => "yesterday",
        _ => $"{days} days ago",
    };

    public static string UntilExpiry(int days) => days switch
    {
        < 0 => "expired",
        0 => "expire today",
        1 => "expire tomorrow",
        _ => $"expire in {days} days",
    };

    /// <summary>A balance check's status in plain words.</summary>
    public static string CheckStatus(string status) => status switch
    {
        "Ok" => "ok",
        "NeedsUser" => "needs you",
        "Blocked" => "blocked by the site",
        "UnknownOwner" => "unknown owner",
        "UnknownProgram" => "unknown program",
        _ => "failed",
    };

    public static string Month(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);

    /// <summary>What the API said, or a plain line when the server could not be reached at all.</summary>
    public static string Failure(Exception failure) =>
        failure is ApiException ? failure.Message : "The server could not be reached. Check the connection and try again.";
}
