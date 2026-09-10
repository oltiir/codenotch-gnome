namespace Codenotch.Core.Tests;

public sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers);

/// Scripted HttpMessageHandler: respond via a delegate or a queue of
/// pre-built responses. Every request is recorded (method, URL, headers --
/// request headers merged with content headers) before the response is
/// produced, so assertions can inspect what was sent even when the
/// responder throws to simulate a network failure.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
    private readonly Queue<HttpResponseMessage>? _scripted;

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public StubHttpMessageHandler(IEnumerable<HttpResponseMessage> scripted)
    {
        _scripted = new Queue<HttpResponseMessage>(scripted);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var h in request.Headers)
            headers[h.Key] = h.Value.ToList();
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                headers[h.Key] = h.Value.ToList();
        }
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, headers));

        if (_responder is not null)
            return Task.FromResult(_responder(request));
        if (_scripted is not null && _scripted.Count > 0)
            return Task.FromResult(_scripted.Dequeue());

        throw new InvalidOperationException("StubHttpMessageHandler: no scripted response left.");
    }
}
