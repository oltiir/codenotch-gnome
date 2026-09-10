using System.Globalization;
using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

public class ClockTextTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;
    private static readonly TimeZoneInfo Berlin = TestTime.Berlin;

    [Fact]
    public void TheSameBerlinDayShowsJustTheTime()
    {
        Assert.Equal("22:10", UsageText.ClockText("2026-09-07T20:10:00Z", Now, Berlin));
    }

    [Fact]
    public void AnotherDayNamesTheDay()
    {
        Assert.Equal("Tue 06:00", UsageText.ClockText("2026-09-08T04:00:00Z", Now, Berlin));
    }

    [Fact]
    public void ANullTimestampHasNoClockText()
    {
        Assert.Equal("", UsageText.ClockText(null, Now, Berlin));
    }

    [Fact]
    public void TheDayNameIsInvariantEnglishEvenUnderAGermanCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Tue 06:00", UsageText.ClockText("2026-09-08T04:00:00Z", Now, Berlin));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
