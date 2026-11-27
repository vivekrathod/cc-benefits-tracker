using Chitragupta.Application.Ports;

namespace Chitragupta.Infrastructure;

internal sealed class SystemClock(TimeZoneInfo zone) : IClock
{
    public DateTimeOffset Now => ToLocal(DateTimeOffset.UtcNow);

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, zone);
}
