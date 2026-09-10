using System.Net;
using Codenotch.Core.Sources;
using Xunit;

namespace Codenotch.Core.Tests;

public class EndpointSourceTests
{
    private const string Url = "http://127.0.0.1:8787/usage";

    private static EndpointSource Build(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), Url);

    [Fact]
    public async Task A200ServingLiveJsonYieldsTwoEntriesInServerOrder()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixtures.Text("live.json")),
        });
        var entries = await Build(handler).FetchAsync();

        Assert.Equal(new[] { "codex", "claude" }, entries.Select(e => e.Provider).ToArray());
    }

    [Fact]
    public async Task A500YieldsOneEntryWithErrorMessageHttp500()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var entries = await Build(handler).FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("HTTP 500", entry.Error!.Message);
    }

    [Fact]
    public async Task AnHttpRequestExceptionYieldsOneEntryWhoseMessageStartsWithCantReachTheHost()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var entries = await Build(handler).FetchAsync();

        var entry = Assert.Single(entries);
        Assert.StartsWith("Can't reach 127.0.0.1", entry.Error!.Message);
    }

    [Fact]
    public async Task A200WithANonJsonBodyYieldsOneErrorEntryRatherThanThrowing()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all"),
        });
        var entries = await Build(handler).FetchAsync();

        var entry = Assert.Single(entries);
        Assert.NotNull(entry.Error);
    }

    [Fact]
    public async Task TheRequestCarriesAcceptApplicationJson()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        await Build(handler).FetchAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Contains("application/json", request.Headers["Accept"]);
    }
}
