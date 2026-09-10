using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using Xunit;

namespace Codenotch.Core.Tests;

/// End-to-end assertions over the real codexbar serve payload captured in
/// fixtures/live.json (one healthy provider, one errored).
public class LiveFixtureTests
{
    private static IReadOnlyList<ProviderEntry> Entries() =>
        UsageEnvelopeParser.ProvidersFrom(Fixtures.Text("live.json"));

    [Fact]
    public void LiveJsonParsesToTwoEntriesWithNoException()
    {
        var entries = Entries();
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void TheCodexEntrysErrorMessageIsTheExpectedTextAndItRendersAsNoData()
    {
        var codex = Entries().Single(e => e.Provider == "codex");
        var reading = UsageNormalizer.ReadingFrom(codex);

        Assert.Equal("Codex returned invalid data: codex app-server closed stdout", reading.Error);
        Assert.False(reading.HasData);
    }

    [Fact]
    public void TheClaudeEntrysPaceSummaryMatchesTheFixture()
    {
        var claude = Entries().Single(e => e.Provider == "claude");
        var reading = UsageNormalizer.ReadingFrom(claude);

        Assert.Equal("27% in reserve | Expected 39% used | Lasts until reset", reading.PaceSummary);
    }

    [Fact]
    public void WorstOfOverTheWholeFixtureReturnsClaudeTwelveWithWeeklyFiftyOne()
    {
        var readings = UsageNormalizer.ReadingsFrom(Entries());
        var worst = Worst.Of(readings);

        Assert.NotNull(worst);
        Assert.Equal("claude", worst!.Provider.Id);
        Assert.Equal(12, worst.Used);
        Assert.Equal(51, worst.Weekly);
    }

    [Fact]
    public void TheDividerPositionForTheClaudeEntryIsLabelDividerLabelLabel()
    {
        var claude = Entries().Single(e => e.Provider == "claude");
        var windows = UsageNormalizer.WindowsFrom(claude);

        // Mirrors the "Weekly limits" divider rule (C.9): emit it exactly once,
        // immediately before the first window whose Group is Weekly.
        var shape = new List<string>();
        var dividerEmitted = false;
        foreach (var w in windows)
        {
            if (w.Group == WindowGroup.Weekly && !dividerEmitted)
            {
                shape.Add("divider");
                dividerEmitted = true;
            }
            shape.Add("label");
        }

        Assert.Equal(new[] { "label", "divider", "label", "label" }, shape);
    }
}
