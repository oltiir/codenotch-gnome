using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

public class ResetLineTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;
    private static readonly TimeZoneInfo Berlin = TestTime.Berlin;

    [Fact]
    public void TheCountdownAndTheWallClockSitSideBySideSeparatedByTwoSpacesAndAMiddleDot()
    {
        Assert.Equal("Resets in 4 min  ·  22:10", UsageText.ResetLine("2026-09-07T20:10:00Z", Now, Berlin));
    }

    [Fact]
    public void AWindowAtItsResetInstantReadsResetting()
    {
        Assert.Equal("Resetting", UsageText.ResetLine(Now.ToString("O"), Now, Berlin));
    }

    [Fact]
    public void AWindowAlreadyPastReadsResetting()
    {
        Assert.Equal("Resetting", UsageText.ResetLine(Now.AddMinutes(-5).ToString("O"), Now, Berlin));
    }

    [Fact]
    public void ANullTimestampHasNoResetLine()
    {
        Assert.Equal("", UsageText.ResetLine(null, Now, Berlin));
    }

    [Fact]
    public void ATomorrowDatedIsoContainsTheMiddleDotAndTheDddHhMmClock()
    {
        var line = UsageText.ResetLine(Now.AddDays(1).ToString("O"), Now, Berlin);
        Assert.Contains("·", line);
        Assert.Contains("Tue", line);
    }
}
