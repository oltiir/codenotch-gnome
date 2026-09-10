using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

public class ToneTests
{
    [Fact]
    public void ToneOfOverTheThresholdBoundariesGivesOkOkWarnWarnCriticalCritical()
    {
        var results = new[] { 0, 69, 70, 89, 90, 100 }.Select(Tone.Of).ToArray();
        Assert.Equal(
            new[] { ToneKind.Ok, ToneKind.Ok, ToneKind.Warn, ToneKind.Warn, ToneKind.Critical, ToneKind.Critical },
            results);
    }

    [Fact]
    public void ToneOfNullIsStale()
    {
        Assert.Equal(ToneKind.Stale, Tone.Of((int?)null));
    }

    [Fact]
    public void ThresholdsAreSeventyAndNinety()
    {
        Assert.Equal(70, Thresholds.WarnAt);
        Assert.Equal(90, Thresholds.CriticalAt);
    }

    [Fact]
    public void ToneColorsHexMatchesTheDocumentedDarkAndLightPairs()
    {
        Assert.Equal("#57E389", ToneColors.Hex(ToneKind.Ok, dark: true));
        Assert.Equal("#F8E45C", ToneColors.Hex(ToneKind.Warn, dark: true));
        Assert.Equal("#FF7B63", ToneColors.Hex(ToneKind.Critical, dark: true));
        Assert.Equal("#9A9996", ToneColors.Hex(ToneKind.Stale, dark: true));
        Assert.Equal("#1A7F37", ToneColors.Hex(ToneKind.Ok, dark: false));
        Assert.Equal("#9A6700", ToneColors.Hex(ToneKind.Warn, dark: false));
        Assert.Equal("#CF222E", ToneColors.Hex(ToneKind.Critical, dark: false));
        Assert.Equal("#6E7781", ToneColors.Hex(ToneKind.Stale, dark: false));
        Assert.Equal("#1FFFFFFF", ToneColors.TrackHex(dark: true));
        Assert.Equal("#1A000000", ToneColors.TrackHex(dark: false));
    }
}
