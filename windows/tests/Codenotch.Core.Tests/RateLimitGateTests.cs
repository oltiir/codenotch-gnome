using Codenotch.Core.Sources;
using Xunit;

namespace Codenotch.Core.Tests;

public class RateLimitGateTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    [Fact]
    public void AnUnseenKeyIsNotBlocked()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        Assert.False(gate.IsBlocked("key", out _));
    }

    [Fact]
    public void RecordRateLimitWithNullRetryAfterBlocksForFiveMinutes()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        gate.RecordRateLimit("key", null);
        Assert.True(gate.IsBlocked("key", out var until));
        Assert.Equal(Now.AddMinutes(5), until);
    }

    [Fact]
    public void RecordRateLimitWithARetryAfterBlocksUntilThatInstant()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        gate.RecordRateLimit("key", Now.AddSeconds(120));
        Assert.True(gate.IsBlocked("key", out var until));
        Assert.Equal(Now.AddSeconds(120), until);
    }

    [Fact]
    public void ASecondRecordRateLimitWithAnEarlierRetryAfterDoesNotShortenTheBlock()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        gate.RecordRateLimit("key", Now.AddMinutes(10));
        gate.RecordRateLimit("key", Now.AddMinutes(1));
        Assert.True(gate.IsBlocked("key", out var until));
        Assert.Equal(Now.AddMinutes(10), until);
    }

    [Fact]
    public void TheBlockExpiresExactlyAtUntil()
    {
        var clock = new FakeClock(Now);
        var gate = new RateLimitGate(clock);
        gate.RecordRateLimit("key", Now.AddMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(gate.IsBlocked("key", out _));
    }

    [Fact]
    public void RecordSuccessClearsTheBlock()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        gate.RecordRateLimit("key", null);
        gate.RecordSuccess("key");
        Assert.False(gate.IsBlocked("key", out _));
    }

    [Fact]
    public void KeysAreIndependent()
    {
        var gate = new RateLimitGate(new FakeClock(Now));
        gate.RecordRateLimit("token-a", null);
        Assert.True(gate.IsBlocked("token-a", out _));
        Assert.False(gate.IsBlocked("token-b", out _));
    }

    [Fact]
    public void RetryAfterParseDeltaSecondsGivesNowPlusThatManySeconds()
    {
        Assert.Equal(Now.AddSeconds(120), RetryAfter.Parse("120", Now));
    }

    [Fact]
    public void RetryAfterParseAnHttpDateGivesThatInstant()
    {
        var parsed = RetryAfter.Parse("Tue, 08 Sep 2026 04:00:00 GMT", Now);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("NaN")]
    public void RetryAfterParseAbsentOrGarbageIsNull(string? header)
    {
        Assert.Null(RetryAfter.Parse(header, Now));
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e30")]
    public void RetryAfterParseAnAbsurdDeltaIsCappedRatherThanThrowing(string header)
    {
        Assert.Equal(Now.AddDays(1), RetryAfter.Parse(header, Now));
    }

    [Fact]
    public void KeyForTokenIsStableSixtyFourHexCharsAndDiffersPerToken()
    {
        var a1 = RateLimitGate.KeyForToken("token-a");
        var a2 = RateLimitGate.KeyForToken("token-a");
        var b = RateLimitGate.KeyForToken("token-b");

        Assert.Equal(a1, a2);
        Assert.Equal(64, a1.Length);
        Assert.Matches("^[0-9a-f]{64}$", a1);
        Assert.NotEqual(a1, b);
    }
}
