using Codenotch.Core;

namespace Codenotch.Core.Tests;

/// Injectable clock for time-dependent tests. Settable UtcNow/LocalZone so a
/// test can pin an instant and advance it explicitly instead of relying on
/// the wall clock or TimeZoneInfo.Local.
public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
    public TimeZoneInfo LocalZone { get; set; }

    public FakeClock(DateTimeOffset utcNow, TimeZoneInfo? localZone = null)
    {
        UtcNow = utcNow;
        LocalZone = localZone ?? TimeZoneInfo.Utc;
    }

    public void Advance(TimeSpan by) => UtcNow += by;
}
