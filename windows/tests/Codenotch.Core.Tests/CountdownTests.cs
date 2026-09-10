using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

public class CountdownTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static string At(TimeSpan offset) => UsageText.Countdown(Now.Add(offset).ToString("O"), Now);

    [Fact]
    public void FiftyNineMinutesOutIsFiftyNineMin()
    {
        Assert.Equal("59 min", At(TimeSpan.FromMinutes(59)));
    }

    [Fact]
    public void ThirteenHoursFortyNineMinutesOutIsThirteenHrFortyNineMin()
    {
        Assert.Equal("13 hr 49 min", At(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(49)));
    }

    [Fact]
    public void ExactlyTwoHoursOutIsTwoHr()
    {
        Assert.Equal("2 hr", At(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void TwoDaysThreeHoursOutIsTwoDaysThreeHr()
    {
        Assert.Equal("2 days 3 hr", At(TimeSpan.FromDays(2) + TimeSpan.FromHours(3)));
    }

    [Fact]
    public void OneDayOneMinuteOutIsOneDaySingular()
    {
        Assert.Equal("1 day", At(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ExactlyThreeDaysOutIsThreeDays()
    {
        Assert.Equal("3 days", At(TimeSpan.FromDays(3)));
    }

    [Fact]
    public void FiveMinutesInThePastIsNow()
    {
        Assert.Equal("now", At(TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public void ExactlyNowIsNow()
    {
        Assert.Equal("now", At(TimeSpan.Zero));
    }

    [Fact]
    public void TwentySecondsOutIsFlooredToOneMin()
    {
        Assert.Equal("1 min", At(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void FiftyNineMinutesFortySecondsOutRoundsUpToOneHr()
    {
        Assert.Equal("1 hr", At(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(40)));
    }

    [Fact]
    public void NullAndAnUnparsableStringGiveAnEmptyString()
    {
        Assert.Equal("", UsageText.Countdown(null, Now));
        Assert.Equal("", UsageText.Countdown("not a date", Now));
    }

    [Fact]
    public void AnOffsetTimestampGivesTheSameResultAsItsZEquivalent()
    {
        var withZ = UsageText.Countdown("2026-09-07T22:10:00Z", Now);
        var withOffset = UsageText.Countdown("2026-09-08T00:10:00+02:00", Now);
        Assert.Equal(withZ, withOffset);
    }
}
