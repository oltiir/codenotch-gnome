using System.Text.Json;
using Codenotch.Core.Model;
using Codenotch.Core.Sources.Claude;
using Xunit;

namespace Codenotch.Core.Tests;

public class ClaudeMappingTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static ClaudeUsageResponse Load(string fixture) =>
        JsonSerializer.Deserialize<ClaudeUsageResponse>(Fixtures.Text(fixture), CodexBarJson.Options)!;

    [Fact]
    public void FiveHourMapsToUsagePrimaryWithUsedPercentAndWindowMinutes()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.Equal(12.4, entry.Usage!.Primary!.UsedPercent);
        Assert.Equal(300, entry.Usage.Primary.WindowMinutes);
    }

    [Fact]
    public void SevenDayMapsToUsageSecondaryWithWindowMinutesTenThousandEighty()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.Equal(51, entry.Usage!.Secondary!.UsedPercent);
        Assert.Equal(10080, entry.Usage.Secondary.WindowMinutes);
    }

    [Fact]
    public void WithNoLimitsSevenDayOpusLandsInTertiaryAndExtraRateWindowsIsEmptyOrNull()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.Equal(72.6, entry.Usage!.Tertiary!.UsedPercent);
        Assert.Equal(10080, entry.Usage.Tertiary.WindowMinutes);
        Assert.True(entry.Usage.ExtraRateWindows is null or { Count: 0 });
    }

    [Fact]
    public void WhenBothSevenDaySonnetAndSevenDayOpusArePresentSonnetWinsTheTertiarySlot()
    {
        var response = Load("claude-oauth-usage.json");
        response.SevenDaySonnet = new ClaudeUsageWindowDto { Utilization = 5, ResetsAt = "2026-09-08T04:00:00Z" };
        var entry = ClaudeUsageMapper.ToEntry(response, Now);
        Assert.Equal(5, entry.Usage!.Tertiary!.UsedPercent);
    }

    [Fact]
    public void OauthAppsAndRoutinesProduceNothingInV1()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        var windows = UsageNormalizer.WindowsFrom(entry);
        Assert.DoesNotContain(windows, w => w.Used == 3);   // seven_day_oauth_apps utilization
    }

    [Fact]
    public void TheResultingEntrysProviderIsClaude()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.Equal("claude", entry.Provider);
    }

    [Fact]
    public void RoundTrippingTheNoLimitsEntryGivesTheExpectedLabels()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        var labels = UsageNormalizer.WindowsFrom(entry).Select(w => w.Label).ToArray();
        Assert.Equal(new[] { "Current session", "All models", "Model" }, labels);
    }

    [Fact]
    public void ResetsAtSurvivesVerbatimIntoResetsAt()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.Equal("2026-09-07T20:10:00.000Z", entry.Usage!.Primary!.ResetsAt);
    }

    [Fact]
    public void AResponseWithEveryWindowNullMapsToAnEntryWhoseWindowsFromIsEmpty()
    {
        var empty = new ClaudeUsageResponse();
        var entry = ClaudeUsageMapper.ToEntry(empty, Now);
        Assert.Empty(UsageNormalizer.WindowsFrom(entry));
    }

    [Fact]
    public void LimitsFixtureProducesExactlyOneExtraWindowAndDropsAllModelsAndTertiary()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage-limits.json"), Now);
        Assert.Null(entry.Usage!.Tertiary);
        var extras = entry.Usage.ExtraRateWindows;
        Assert.NotNull(extras);
        var extra = Assert.Single(extras!);
        Assert.Equal("claude-weekly-scoped-claude-fable-4", extra.Id);
        Assert.Equal("Fable only", extra.Title);
        Assert.Equal(44, extra.Window!.UsedPercent);
    }

    [Fact]
    public void DuplicateLimitsIdsAreDeduplicatedTheInactiveDuplicateDoesNotAddARow()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage-limits.json"), Now);
        Assert.Single(entry.Usage!.ExtraRateWindows!);
    }

    [Fact]
    public void RoundTrippingTheLimitsEntryGivesLabelsProvingTheOnlyStripClosesTheLoop()
    {
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage-limits.json"), Now);
        var labels = UsageNormalizer.WindowsFrom(entry).Select(w => w.Label).ToArray();
        Assert.Equal(new[] { "Current session", "All models", "Fable" }, labels);
    }

    [Fact]
    public void UnknownResponseKeysAreIgnored()
    {
        // claude-oauth-usage.json carries an unrecognised "extra_usage" object; parsing must not throw.
        var entry = ClaudeUsageMapper.ToEntry(Load("claude-oauth-usage.json"), Now);
        Assert.NotNull(entry);
    }

    [Fact]
    public void SlugLowercasesAndDashesNonAlphanumerics()
    {
        Assert.Equal("claude-fable-4", ClaudeUsageMapper.Slug("Claude Fable 4"));
    }
}
