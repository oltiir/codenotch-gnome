using System.Net;
using Codenotch.Core.Sources.Codex;
using Xunit;

namespace Codenotch.Core.Tests;

public class CodexOAuthSourceTests
{
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static CodexOAuthSource Build(StubHttpMessageHandler handler, string authJsonPath) =>
        new(new HttpClient(handler), authJsonPath, new FakeClock(Now));

    [Fact]
    public async Task A200YieldsOneCodexEntryAndTheExpectedRequestHeaders()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Text("codex-wham-usage.json")),
        });
        var source = Build(handler, Fixtures.Path("codex-auth.json"));

        var entries = await source.FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("codex", entry.Provider);

        var request = Assert.Single(handler.Requests);
        Assert.Contains(request.Headers["Authorization"], v => v.StartsWith("Bearer "));
        Assert.Contains("acct_2f9c1d40", request.Headers["ChatGPT-Account-Id"]);
    }

    [Fact]
    public async Task A401YieldsTheUnauthorizedRunCodexLoginEntry()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var source = Build(handler, Fixtures.Path("codex-auth.json"));

        var entry = Assert.Single(await source.FetchAsync());
        Assert.Equal("unauthorized, run `codex login`", entry.Error!.Message);
    }

    [Fact]
    public async Task A500YieldsHttp500()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var source = Build(handler, Fixtures.Path("codex-auth.json"));

        var entry = Assert.Single(await source.FetchAsync());
        Assert.Equal("HTTP 500", entry.Error!.Message);
    }
}
