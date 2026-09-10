namespace Codenotch.Core;

/// <summary>
/// "What time is it" as a dependency. Every countdown, cooldown and pace
/// computation in this library takes its `now` from here so the tests can pin
/// an instant, the way `waybar/tests/test_presentation.py` pins one.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo LocalZone { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo LocalZone => TimeZoneInfo.Local;
}
