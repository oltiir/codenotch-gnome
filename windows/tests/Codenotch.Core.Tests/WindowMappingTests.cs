using Codenotch.Core.Model;
using Xunit;

namespace Codenotch.Core.Tests;

public class WindowMappingTests
{
    private static ProviderEntry ClaudeFromLive()
    {
        var list = UsageEnvelopeParser.ProvidersFrom(Fixtures.Text("live.json"));
        return list.Single(e => e.Provider == "claude");
    }

    private static ProviderEntry CodexFromLive()
    {
        var list = UsageEnvelopeParser.ProvidersFrom(Fixtures.Text("live.json"));
        return list.Single(e => e.Provider == "codex");
    }

    [Fact]
    public void LiveJsonsClaudeEntryMapsToExactlyTheExpectedTriples()
    {
        var windows = UsageNormalizer.WindowsFrom(ClaudeFromLive());
        var triples = windows.Select(w => (w.Key, w.Label, w.Used)).ToArray();
        Assert.Equal(
            new (string, string, int)[]
            {
                ("session", "Current session", 12),
                ("weekly", "All models", 51),
                ("claude-weekly-scoped-fable", "Fable", 72),
            },
            triples);
    }

    [Fact]
    public void GroupsForThatEntryAreSessionWeeklyWeekly()
    {
        var windows = UsageNormalizer.WindowsFrom(ClaudeFromLive());
        Assert.Equal(new[] { WindowGroup.Session, WindowGroup.Weekly, WindowGroup.Weekly },
            windows.Select(w => w.Group).ToArray());
    }

    private static ProviderEntry EntryWithExtra(string? title, string? id = "extra-id")
    {
        return new ProviderEntry
        {
            Provider = "claude",
            Usage = new UsageDto
            {
                ExtraRateWindows = new List<NamedRateWindowDto>
                {
                    new()
                    {
                        Id = id,
                        Title = title,
                        Window = new RateWindowDto { UsedPercent = 40 },
                    },
                },
            },
        };
    }

    [Fact]
    public void FableOnlyLosesTheOnlySuffix()
    {
        var windows = UsageNormalizer.WindowsFrom(EntryWithExtra("Fable only"));
        Assert.Equal("Fable", windows.Single().Label);
    }

    [Fact]
    public void OpusOnlyLosesTheSuffixCaseInsensitively()
    {
        var windows = UsageNormalizer.WindowsFrom(EntryWithExtra("Opus Only"));
        Assert.Equal("Opus", windows.Single().Label);
    }

    [Theory]
    [InlineData("Lonely")]
    [InlineData("Only")]
    public void LonelyAndOnlyKeepTheirText(string title)
    {
        var windows = UsageNormalizer.WindowsFrom(EntryWithExtra(title));
        Assert.Equal(title, windows.Single().Label);
    }

    [Fact]
    public void AnUntitledExtraWindowIsLabelledScopedAndKeyedExtraWhenIdIsAbsent()
    {
        var windows = UsageNormalizer.WindowsFrom(EntryWithExtra(null, id: null));
        var w = windows.Single();
        Assert.Equal("extra", w.Key);
        Assert.Equal("Scoped", w.Label);
    }

    [Fact]
    public void AWindowWithNoUsedPercentIsSkippedItsSiblingsSurvive()
    {
        var entry = new ProviderEntry
        {
            Provider = "claude",
            Usage = new UsageDto
            {
                Primary = new RateWindowDto { UsedPercent = null },
                Secondary = new RateWindowDto { UsedPercent = 51, ResetsAt = "2026-09-08T04:00:00Z" },
            },
        };
        var windows = UsageNormalizer.WindowsFrom(entry);
        Assert.Single(windows);
        Assert.Equal("weekly", windows[0].Key);
    }

    [Fact]
    public void ANullTertiaryIsSkipped()
    {
        var entry = new ProviderEntry
        {
            Provider = "claude",
            Usage = new UsageDto { Tertiary = null },
        };
        Assert.Empty(UsageNormalizer.WindowsFrom(entry));
    }

    [Fact]
    public void APresentTertiaryIsTertiaryModelWeekly()
    {
        var entry = new ProviderEntry
        {
            Provider = "claude",
            Usage = new UsageDto { Tertiary = new RateWindowDto { UsedPercent = 30 } },
        };
        var w = UsageNormalizer.WindowsFrom(entry).Single();
        Assert.Equal("tertiary", w.Key);
        Assert.Equal("Model", w.Label);
        Assert.Equal(WindowGroup.Weekly, w.Group);
    }

    [Theory]
    [InlineData(11.6, 12)]
    [InlineData(11.5, 12)]
    [InlineData(12.5, 13)]
    [InlineData(-5, 0)]
    [InlineData(140, 100)]
    public void ClampPctRoundsAndClampsWithJsHalfUpRounding(double input, int expected)
    {
        Assert.Equal(expected, UsageNormalizer.ClampPct(input));
    }

    [Fact]
    public void AnErroredEntryHasNoWindowsAndHasDataIsFalse()
    {
        var reading = UsageNormalizer.ReadingFrom(CodexFromLive());
        Assert.Empty(reading.Windows);
        Assert.False(reading.HasData);
    }

    [Fact]
    public void AnEntryWithNoUsageAtAllAndANullEntryBothGiveNoWindows()
    {
        Assert.Empty(UsageNormalizer.WindowsFrom(new ProviderEntry { Provider = "claude", Usage = null }));
        Assert.Empty(UsageNormalizer.WindowsFrom(null));
    }

    [Fact]
    public void ResetsAtIsCarriedThroughVerbatimAsTheRawIsoString()
    {
        var w = UsageNormalizer.WindowsFrom(ClaudeFromLive())[0];
        Assert.Equal("2026-09-07T20:10:00Z", w.ResetsAt);
    }

    [Fact]
    public void AWindowWithNoResetsAtCarriesNull()
    {
        var entry = new ProviderEntry
        {
            Provider = "claude",
            Usage = new UsageDto { Primary = new RateWindowDto { UsedPercent = 5, ResetsAt = null } },
        };
        Assert.Null(UsageNormalizer.WindowsFrom(entry).Single().ResetsAt);
    }

    [Fact]
    public void ProviderIdentityForClaudeCursorCopilotGeminiMatchProviders()
    {
        Assert.Equal(new ProviderIdentity("claude", "Claude", "✱"), ProviderIdentity.For("claude"));
        Assert.Equal(new ProviderIdentity("cursor", "Cursor", "△"), ProviderIdentity.For("cursor"));
        Assert.Equal(new ProviderIdentity("copilot", "Copilot", "⌘"), ProviderIdentity.For("copilot"));
        Assert.Equal(new ProviderIdentity("gemini", "Gemini", "✦"), ProviderIdentity.For("gemini"));
    }

    [Fact]
    public void ProviderIdentityForBedrockIsSyntheticAndForNullIsUnknown()
    {
        Assert.Equal(new ProviderIdentity("bedrock", "Bedrock", "B"), ProviderIdentity.For("bedrock"));
        Assert.Equal(new ProviderIdentity("unknown", "Unknown", "U"), ProviderIdentity.For(null));
    }

    [Fact]
    public void ProviderReadingSessionFallsBackToWindowsZeroWhenNoWindowIsKeyedSession()
    {
        var windows = new[] { new UsageWindow("weekly", WindowGroup.Weekly, "All models", 40, null) };
        var reading = new ProviderReading(ProviderIdentity.Claude, windows, null, null);
        Assert.Equal(windows[0], reading.Session);
    }

    [Fact]
    public void ProviderReadingWeeklyIsNullWhenNoWindowIsKeyedWeekly()
    {
        var windows = new[] { new UsageWindow("claude-weekly-scoped-fable", WindowGroup.Weekly, "Fable", 40, null) };
        var reading = new ProviderReading(ProviderIdentity.Claude, windows, null, null);
        Assert.Null(reading.Weekly);
    }
}
