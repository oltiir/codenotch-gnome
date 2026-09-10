using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

public class WorstTests
{
    private static ProviderReading Reading(ProviderIdentity id, int session, int? weekly, string? error = null)
    {
        var windows = new List<UsageWindow> { new("session", WindowGroup.Session, "Current session", session, null) };
        if (weekly is not null)
            windows.Add(new UsageWindow("weekly", WindowGroup.Weekly, "All models", weekly.Value, null));
        return new ProviderReading(id, error is null ? windows : new List<UsageWindow>(), error, null);
    }

    [Fact]
    public void AcrossLiveJsonTheWorstIsClaudeTwelveWithWeeklyFiftyOne()
    {
        var entries = UsageEnvelopeParser.ProvidersFrom(Fixtures.Text("live.json"));
        var readings = UsageNormalizer.ReadingsFrom(entries);
        var worst = Worst.Of(readings);
        Assert.NotNull(worst);
        Assert.Equal("claude", worst!.Provider.Id);
        Assert.Equal(12, worst.Used);
        Assert.Equal(51, worst.Weekly);
    }

    [Fact]
    public void AcrossTwoHealthyProvidersTheHigherSessionPercentWins()
    {
        var readings = new[]
        {
            Reading(ProviderIdentity.Claude, 40, 50),
            Reading(ProviderIdentity.Codex, 60, 20),
        };
        var worst = Worst.Of(readings);
        Assert.Equal("codex", worst!.Provider.Id);
        Assert.Equal(60, worst.Used);
    }

    [Fact]
    public void ATieKeepsTheEarlierProviderInTheIterationOrder()
    {
        var readings = new[]
        {
            Reading(ProviderIdentity.Claude, 50, 10),
            Reading(ProviderIdentity.Codex, 50, 20),
        };
        var worst = Worst.Of(readings);
        Assert.Equal("claude", worst!.Provider.Id);
    }

    [Fact]
    public void ProvidersWithHasDataFalseAreSkipped()
    {
        var readings = new[]
        {
            Reading(ProviderIdentity.Claude, 90, null, error: "no data"),
            Reading(ProviderIdentity.Codex, 30, 10),
        };
        var worst = Worst.Of(readings);
        Assert.Equal("codex", worst!.Provider.Id);
    }

    [Fact]
    public void AllErroredInputGivesNull()
    {
        var readings = new[]
        {
            Reading(ProviderIdentity.Claude, 90, null, error: "no data"),
            Reading(ProviderIdentity.Codex, 30, null, error: "no data"),
        };
        Assert.Null(Worst.Of(readings));
    }

    [Fact]
    public void WorstReadingToneFollowsTheSessionPercent()
    {
        var readings = new[] { Reading(ProviderIdentity.Claude, 95, 10) };
        var worst = Worst.Of(readings);
        Assert.Equal(ToneKind.Critical, worst!.Tone);
    }
}
