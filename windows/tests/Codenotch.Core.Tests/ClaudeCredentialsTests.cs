using Codenotch.Core.Sources.Claude;
using Xunit;

namespace Codenotch.Core.Tests;

public class ClaudeCredentialsTests
{
    [Fact]
    public void ParsesAccessTokenRefreshTokenScopesSubscriptionTypeAndRateLimitTier()
    {
        var creds = ClaudeCredentialsFile.Parse(Fixtures.Text("claude-credentials.json"));
        Assert.Equal("sk-ant-oat01-TESTTOKEN", creds.AccessToken);
        Assert.Equal("sk-ant-ort01-TESTREFRESH", creds.RefreshToken);
        Assert.Equal(new[] { "user:inference", "user:profile" }, creds.Scopes);
        Assert.Equal("max", creds.SubscriptionType);
        Assert.Equal("default_max_20x", creds.RateLimitTier);
    }

    [Fact]
    public void ExpiresAtInEpochMillisecondsBecomesTheExpectedInstant()
    {
        var creds = ClaudeCredentialsFile.Parse(Fixtures.Text("claude-credentials.json"));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788813600), creds.ExpiresAt);
    }

    [Fact]
    public void ExpiresAtSuppliedAsADoubleParsesIdentically()
    {
        var json = """{"claudeAiOauth":{"accessToken":"tok","expiresAt":1788813600000.0}}""";
        var creds = ClaudeCredentialsFile.Parse(json);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788813600), creds.ExpiresAt);
    }

    [Fact]
    public void IsExpiredIsFalseBeforeAndTrueAtOrAfterExpiresAt()
    {
        var creds = ClaudeCredentialsFile.Parse(Fixtures.Text("claude-credentials.json"));
        var expiresAt = creds.ExpiresAt!.Value;
        Assert.False(creds.IsExpired(expiresAt.AddSeconds(-1)));
        Assert.True(creds.IsExpired(expiresAt));
        Assert.True(creds.IsExpired(expiresAt.AddSeconds(1)));
    }

    [Fact]
    public void ACredentialWithNoExpiresAtReportsIsExpiredTrue()
    {
        var json = """{"claudeAiOauth":{"accessToken":"tok"}}""";
        var creds = ClaudeCredentialsFile.Parse(json);
        Assert.True(creds.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void McpOnlyFixtureThrowsWithProblemMcpOAuthOnly()
    {
        var ex = Assert.Throws<ClaudeCredentialException>(() =>
            ClaudeCredentialsFile.Parse(Fixtures.Text("claude-credentials-mcponly.json")));
        Assert.Equal(ClaudeCredentialProblem.McpOAuthOnly, ex.Problem);
    }

    [Fact]
    public void ClaudeAiOauthWithNoAccessTokenThrowsMissingAccessToken()
    {
        var json = """{"claudeAiOauth":{"refreshToken":"rt"}}""";
        var ex = Assert.Throws<ClaudeCredentialException>(() => ClaudeCredentialsFile.Parse(json));
        Assert.Equal(ClaudeCredentialProblem.MissingAccessToken, ex.Problem);
    }

    [Fact]
    public void AFileWithNeitherKeyThrowsMissingOAuth()
    {
        var ex = Assert.Throws<ClaudeCredentialException>(() => ClaudeCredentialsFile.Parse("{}"));
        Assert.Equal(ClaudeCredentialProblem.MissingOAuth, ex.Problem);
    }

    [Fact]
    public void MalformedJsonThrowsDecodeFailed()
    {
        var ex = Assert.Throws<ClaudeCredentialException>(() => ClaudeCredentialsFile.Parse("{not json"));
        Assert.Equal(ClaudeCredentialProblem.DecodeFailed, ex.Problem);
    }

    [Fact]
    public void AMissingFileThrowsNotFound()
    {
        var ex = Assert.Throws<ClaudeCredentialException>(() =>
            ClaudeCredentialsFile.Read(Path.Combine(Path.GetTempPath(), "codenotch-does-not-exist-" + Guid.NewGuid())));
        Assert.Equal(ClaudeCredentialProblem.NotFound, ex.Problem);
    }

    [Fact]
    public void ResolvePathHonoursClaudeConfigDir()
    {
        var env = new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = "/custom/config" };
        var path = ClaudeCredentialsFile.ResolvePath(env, "/home/user");
        Assert.Equal(Path.Combine("/custom/config", ".credentials.json"), path);
    }

    [Fact]
    public void ResolvePathFallsBackToUserProfileDotClaude()
    {
        var env = new Dictionary<string, string?>();
        var path = ClaudeCredentialsFile.ResolvePath(env, "/home/user");
        Assert.Equal(Path.Combine("/home/user", ".claude", ".credentials.json"), path);
    }

    [Fact]
    public void ResolvePathTakesTheFirstEntryOfASemicolonSeparatedClaudeConfigDir()
    {
        var env = new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = "/first;/second" };
        var path = ClaudeCredentialsFile.ResolvePath(env, "/home/user");
        Assert.Equal(Path.Combine("/first", ".credentials.json"), path);
    }
}
