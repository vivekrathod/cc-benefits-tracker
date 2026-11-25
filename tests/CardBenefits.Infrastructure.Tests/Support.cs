using CardBenefits.Application.Ports;

namespace CardBenefits.Infrastructure.Tests;

internal sealed class FixedClock : IClock
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public DateTimeOffset Now => ToLocal(new DateTimeOffset(2026, 11, 25, 15, 0, 0, TimeSpan.Zero));

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, NewYork);

    public DateOnly ToLocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);
}

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("tracker-tests-").FullName;

    public string Write(string name, string content)
    {
        var file = System.IO.Path.Combine(Path, name);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
