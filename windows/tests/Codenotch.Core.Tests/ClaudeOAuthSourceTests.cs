using System.Net;
using Codenotch.Core;
using Codenotch.Core.Sources;
using Codenotch.Core.Sources.Claude;
using Xunit;

namespace Codenotch.Core.Tests;

public class ClaudeOAuthSourceTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static ClaudeOAuthSource Build(StubHttpMessageHandler handler, string credentialsPath, IClock? clock = null)
    {
        clock ??= new FakeClock(Now);
        var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
        var refresh = new ClaudeRefreshPolicy(runner, clock);
        var gate = new RateLimitGate(clock);
        var version = new ClaudeVersionProbe(runner);
        return new ClaudeOAuthSource(new HttpClient(handler), credentialsPath, refresh, gate, version, clock);
    }

    [Fact]
    public async Task A200YieldsOneClaudeEntryAndTheExpectedRequestHeaders()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Text("claude-oauth-usage.json")),
        });
        var source = Build(handler, Fixtures.Path("claude-credentials.json"));

        var entries = await source.FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("claude", entry.Provider);
        Assert.Equal(12.4, entry.Usage!.Primary!.UsedPercent);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("Bearer sk-ant-oat01-TESTTOKEN", request.Headers["Authorization"]);
        Assert.Contains("oauth-2025-04-20", request.Headers["anthropic-beta"]);
        Assert.StartsWith("claude-code/", request.Headers["User-Agent"][0]);
    }

    [Fact]
    public async Task A401YieldsTheUnauthorizedErrorEntry()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var source = Build(handler, Fixtures.Path("claude-credentials.json"));

        var entry = Assert.Single(await source.FetchAsync());
        Assert.Equal("unauthorized, run `claude` to sign in again", entry.Error!.Message);
    }

    [Fact]
    public async Task A429WithRetryAfterYieldsRateLimitedAndGatesTheNextFetch()
    {
        var rateLimited = new HttpResponseMessage((HttpStatusCode)429);
        rateLimited.Headers.Add("Retry-After", "120");
        var handler = new StubHttpMessageHandler(new[] { rateLimited });
        var clock = new FakeClock(Now);
        var source = Build(handler, Fixtures.Path("claude-credentials.json"), clock);

        var first = Assert.Single(await source.FetchAsync());
        Assert.Equal("rate limited", first.Error!.Message);
        Assert.Single(handler.Requests);

        var second = Assert.Single(await source.FetchAsync());
        Assert.Contains("rate limited", second.Error!.Message);
        Assert.Single(handler.Requests);   // still one: the second fetch never hit the network
    }

    [Fact]
    public async Task AMissingCredentialsFileYieldsNotSignedInAndNoNetworkCall()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var missingPath = Path.Combine(Path.GetTempPath(), "codenotch-missing-" + Guid.NewGuid() + ".json");
        var source = Build(handler, missingPath);

        var entry = Assert.Single(await source.FetchAsync());
        Assert.Equal("not signed in, run `claude` to sign in", entry.Error!.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnExpiredCredentialTriggersExactlyOneClaudeAuthStatusJsonRun()
    {
        var expiredPath = Path.Combine(Path.GetTempPath(), "codenotch-expired-" + Guid.NewGuid() + ".json");
        var expiresAtMs = Now.AddHours(-1).ToUnixTimeMilliseconds();
        var json = "{\"claudeAiOauth\":{\"accessToken\":\"sk-ant-oat01-EXPIRED\",\"expiresAt\":"
            + expiresAtMs + "}}";
        await File.WriteAllTextAsync(expiredPath, json);
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixtures.Text("claude-oauth-usage.json")),
            });
            var clock = new FakeClock(Now);
            var runner = new FakeProcessRunner { Default = new ProcessResult(0, "{}", "", false) };
            var refresh = new ClaudeRefreshPolicy(runner, clock);
            var gate = new RateLimitGate(clock);
            var version = new ClaudeVersionProbe(runner);
            var source = new ClaudeOAuthSource(new HttpClient(handler), expiredPath, refresh, gate, version, clock);

            await source.FetchAsync();

            var invocation = Assert.Single(runner.Invocations);
            Assert.Equal(new[] { "auth", "status", "--json" }, invocation.Arguments);
        }
        finally
        {
            File.Delete(expiredPath);
        }
    }
}
