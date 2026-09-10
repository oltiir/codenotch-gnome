namespace Codenotch.Core.Tests;

/// The fixed instant and explicit (non-local) timezone every time-dependent
/// test pins itself to, so assertions hold in any CI timezone -- the same
/// discipline waybar/tests/test_presentation.py uses. Europe/Berlin in
/// September is UTC+02:00, so the fixtures' *Z timestamps read as two hours
/// later locally.
public static class TestTime
{
    public static readonly DateTimeOffset Now = new(2026, 9, 7, 20, 6, 5, TimeSpan.Zero);

    public static readonly TimeZoneInfo Berlin =
        TimeZoneInfo.CreateCustomTimeZone("Test/Berlin", TimeSpan.FromHours(2), "Test/Berlin", "Test/Berlin");
}
