using Codenotch.Core.Model;
using Codenotch.Core.Pace;
using Xunit;

namespace Codenotch.Core.Tests;

public class PaceTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static RateWindowDto HalfElapsed(double used) => new()
    {
        UsedPercent = used,
        WindowMinutes = 300,
        ResetsAt = Now.AddHours(2.5).ToString("O"),
    };

    [Fact]
    public void AHalfElapsedWindowWithTwentyPercentUsedGivesExpectedFiftyAndDeltaMinusThirty()
    {
        var pace = PaceCalculator.For(HalfElapsed(20), Now, 300);
        Assert.NotNull(pace);
        Assert.Equal(50, pace!.ExpectedUsedPercent);
        Assert.Equal(-30, pace.DeltaPercent);
    }

    [Fact]
    public void DeltaWithinTwoGivesOnTrackAndOnPaceAsTheLeftLabel()
    {
        var pace = PaceCalculator.For(HalfElapsed(50), Now, 300);
        Assert.NotNull(pace);
        Assert.Equal("onTrack", pace!.Stage);
        Assert.StartsWith("On pace", pace.Summary);
    }

    [Fact]
    public void DeltaBetweenTwoAndSixGivesSlightlyAheadOrSlightlyBehindBySign()
    {
        var ahead = PaceCalculator.For(HalfElapsed(54), Now, 300);
        var behind = PaceCalculator.For(HalfElapsed(46), Now, 300);

        Assert.Equal("slightlyAhead", ahead!.Stage);
        Assert.Equal("slightlyBehind", behind!.Stage);
    }

    [Fact]
    public void TheSummaryForTheLiveJsonClaudePrimaryReproducesTheExactFixtureString()
    {
        var fixtureNow = DateTimeOffset.Parse("2026-09-07T17:06:05Z");
        var window = new RateWindowDto
        {
            UsedPercent = 12,
            WindowMinutes = 300,
            ResetsAt = "2026-09-07T20:10:00Z",
        };

        var pace = PaceCalculator.For(window, fixtureNow, 300);

        Assert.NotNull(pace);
        Assert.Equal("27% in reserve | Expected 39% used | Lasts until reset", pace!.Summary);
    }

    [Fact]
    public void ExpectedBelowThreeGivesNull()
    {
        var window = new RateWindowDto
        {
            UsedPercent = 1,
            WindowMinutes = 300,
            ResetsAt = Now.AddMinutes(299).ToString("O"),   // only 1 of 300 minutes elapsed
        };
        Assert.Null(PaceCalculator.For(window, Now, 300));
    }

    [Fact]
    public void AWindowWithNoResetsAtGivesNull()
    {
        var window = new RateWindowDto { UsedPercent = 20, WindowMinutes = 300, ResetsAt = null };
        Assert.Null(PaceCalculator.For(window, Now, 300));
    }

    [Fact]
    public void ANonPositiveWindowMinutesGivesNull()
    {
        var window = new RateWindowDto { UsedPercent = 20, WindowMinutes = 0, ResetsAt = Now.AddHours(1).ToString("O") };
        Assert.Null(PaceCalculator.For(window, Now, 0));
    }

    [Fact]
    public void AResetAlreadyPastGivesNull()
    {
        var window = new RateWindowDto { UsedPercent = 20, WindowMinutes = 300, ResetsAt = Now.AddMinutes(-5).ToString("O") };
        Assert.Null(PaceCalculator.For(window, Now, 300));
    }
}
