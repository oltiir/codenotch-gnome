using System.Text.Json;
using Codenotch.Core.Model;
using Codenotch.Core.Sources.Codex;
using Xunit;

namespace Codenotch.Core.Tests;

public class CodexMappingTests
{
    private static CodexUsageResponse Load(string fixture) =>
        JsonSerializer.Deserialize<CodexUsageResponse>(Fixtures.Text(fixture), CodexBarJson.Options)!;

    [Fact]
    public void PrimaryWindowMapsToUsagePrimaryWithUsedPercentTwentyOne()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        Assert.Equal(21, entry.Usage!.Primary!.UsedPercent);
    }

    [Fact]
    public void ResetAtOneOfTheEpochsBecomesTheExpectedIsoInstant()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        Assert.Equal("2026-09-07T20:10:00Z", entry.Usage!.Primary!.ResetsAt);
    }

    [Fact]
    public void ResetAtTheOtherEpochBecomesTheExpectedIsoInstant()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        Assert.Equal("2026-09-08T04:00:00Z", entry.Usage!.Secondary!.ResetsAt);
    }

    [Fact]
    public void LimitWindowSecondsConvertsToWindowMinutes()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        Assert.Equal(300, entry.Usage!.Primary!.WindowMinutes);
        Assert.Equal(10080, entry.Usage.Secondary!.WindowMinutes);
    }

    [Fact]
    public void AdditionalRateLimitsProduceNothingInV1()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        var windows = UsageNormalizer.WindowsFrom(entry);
        Assert.DoesNotContain(windows, w => w.Used == 4);   // the additional_rate_limits entry's used_percent
    }

    [Fact]
    public void TheEntrysProviderIsCodex()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        Assert.Equal("codex", entry.Provider);
    }

    [Fact]
    public void RoundTrippingThroughWindowsFromGivesCurrentSessionAndAllModelsWithTheExpectedUsed()
    {
        var entry = CodexUsageMapper.ToEntry(Load("codex-wham-usage.json"));
        var windows = UsageNormalizer.WindowsFrom(entry);
        Assert.Equal(new[] { "Current session", "All models" }, windows.Select(w => w.Label).ToArray());
        Assert.Equal(new[] { 21, 12 }, windows.Select(w => w.Used).ToArray());
    }

    [Fact]
    public void AMissingRateLimitMapsToAnEntryWithNoWindows()
    {
        var response = new CodexUsageResponse { AccountId = "acct" };
        var entry = CodexUsageMapper.ToEntry(response);
        Assert.Empty(UsageNormalizer.WindowsFrom(entry));
    }

    [Fact]
    public void UsedPercentSuppliedAsADecimalClampsDownstream()
    {
        var response = new CodexUsageResponse
        {
            RateLimit = new CodexRateLimitDto
            {
                PrimaryWindow = new CodexWindowDto { UsedPercent = 21.4, ResetAt = 1788811800, LimitWindowSeconds = 18000 },
            },
        };
        var entry = CodexUsageMapper.ToEntry(response);
        var window = UsageNormalizer.WindowsFrom(entry).Single();
        Assert.Equal(21, window.Used);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(null)]
    public void ResetAtZeroOrAbsentGivesResetsAtNull(long? resetAt)
    {
        var response = new CodexUsageResponse
        {
            RateLimit = new CodexRateLimitDto
            {
                PrimaryWindow = new CodexWindowDto { UsedPercent = 5, ResetAt = resetAt, LimitWindowSeconds = 18000 },
            },
        };
        var entry = CodexUsageMapper.ToEntry(response);
        Assert.Null(entry.Usage!.Primary!.ResetsAt);
    }
}
