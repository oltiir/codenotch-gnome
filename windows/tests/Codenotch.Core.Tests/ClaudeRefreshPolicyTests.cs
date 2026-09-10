using Codenotch.Core;
using Codenotch.Core.Sources.Claude;
using Xunit;

namespace Codenotch.Core.Tests;

public class ClaudeRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;
    private static readonly string[] StatusArgs = { "auth", "status", "--json" };

    private static ClaudeCredentials Fresh(DateTimeOffset now) =>
        new("access-token", "refresh-token", now.AddHours(1), new[] { "user:inference" }, "max", "default");

    private static ClaudeCredentials Expired(DateTimeOffset now) =>
        new("access-token", "refresh-token", now.AddMinutes(-1), new[] { "user:inference" }, "max", "default");

    [Fact]
    public async Task AnUnexpiredCredentialReturnsNotNeededAndNeverTouchesTheRunner()
    {
        var runner = new FakeProcessRunner();
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);
        var current = Fresh(Now);

        var (outcome, creds) = await policy.EnsureFreshAsync(current, () => current);

        Assert.Equal(RefreshOutcome.NotNeeded, outcome);
        Assert.Equal(current, creds);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task AnExpiredCredentialRunsClaudeWithExactlyAuthStatusJsonOnce()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        await policy.EnsureFreshAsync(Expired(Now), () => Fresh(Now));

        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal("claude", invocation.FileName);
        Assert.Equal(StatusArgs, invocation.Arguments);
    }

    [Fact]
    public async Task AfterTheCliRewritesTheFileReloadReturningAFreshCredentialGivesRefreshed()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);
        var fresh = Fresh(Now);

        var (outcome, creds) = await policy.EnsureFreshAsync(Expired(Now), () => fresh);

        Assert.Equal(RefreshOutcome.Refreshed, outcome);
        Assert.Equal(fresh, creds);
    }

    [Fact]
    public async Task AfterTheCliRunsButReloadStillReturnsExpiredTheOutcomeIsStillExpired()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        var (outcome, _) = await policy.EnsureFreshAsync(Expired(Now), () => Expired(Now));

        Assert.Equal(RefreshOutcome.StillExpired, outcome);
    }

    [Fact]
    public async Task ASecondExpiredFetchOneMinuteLaterIsSkippedByCooldownAndDoesNotRerunTheCli()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        await policy.EnsureFreshAsync(Expired(Now), () => Expired(Now));
        Assert.Single(runner.Invocations);

        clock.Advance(TimeSpan.FromMinutes(1));
        var (outcome, _) = await policy.EnsureFreshAsync(Expired(clock.UtcNow), () => Expired(clock.UtcNow));

        Assert.Equal(RefreshOutcome.SkippedByCooldown, outcome);
        Assert.Single(runner.Invocations);
    }

    [Fact]
    public async Task AThirdExpiredFetchFiveMinutesAndOneSecondLaterReRunsTheCli()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        await policy.EnsureFreshAsync(Expired(Now), () => Expired(Now));
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await policy.EnsureFreshAsync(Expired(clock.UtcNow), () => Expired(clock.UtcNow));

        Assert.Equal(2, runner.Invocations.Count);
    }

    [Fact]
    public async Task AProcessResultNotFoundFromTheRunnerGivesCliUnavailable()
    {
        var runner = new FakeProcessRunner { Default = ProcessResult.NotFound };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        var (outcome, _) = await policy.EnsureFreshAsync(Expired(Now), () => Fresh(Now));

        Assert.Equal(RefreshOutcome.CliUnavailable, outcome);
    }

    [Fact]
    public async Task ATimedOutCliRunStillRecordsTheAttemptSoTheCooldownApplies()
    {
        var runner = new FakeProcessRunner { Default = new ProcessResult(-1, "", "", true) };
        var clock = new FakeClock(Now);
        var policy = new ClaudeRefreshPolicy(runner, clock);

        await policy.EnsureFreshAsync(Expired(Now), () => Expired(Now));
        Assert.Single(runner.Invocations);

        clock.Advance(TimeSpan.FromMinutes(1));
        var (outcome, _) = await policy.EnsureFreshAsync(Expired(clock.UtcNow), () => Expired(clock.UtcNow));

        Assert.Equal(RefreshOutcome.SkippedByCooldown, outcome);
        Assert.Single(runner.Invocations);
    }
}
