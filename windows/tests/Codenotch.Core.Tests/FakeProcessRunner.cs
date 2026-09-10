using Codenotch.Core;

namespace Codenotch.Core.Tests;

public sealed record ProcessInvocation(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

/// Scripted IProcessRunner: queue a result per (fileName, joined args) key,
/// or fall back to Default when nothing was scripted for that key. Every
/// call is recorded in Invocations regardless of whether it was scripted.
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, Queue<ProcessResult>> _scripted = new();

    public List<ProcessInvocation> Invocations { get; } = new();

    public ProcessResult Default { get; set; } = ProcessResult.NotFound;

    public void Script(string fileName, IReadOnlyList<string> arguments, ProcessResult result)
    {
        var key = Key(fileName, arguments);
        if (!_scripted.TryGetValue(key, out var queue))
        {
            queue = new Queue<ProcessResult>();
            _scripted[key] = queue;
        }
        queue.Enqueue(result);
    }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken ct = default)
    {
        Invocations.Add(new ProcessInvocation(fileName, arguments, timeout));

        var key = Key(fileName, arguments);
        if (_scripted.TryGetValue(key, out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Dequeue());

        return Task.FromResult(Default);
    }

    private static string Key(string fileName, IReadOnlyList<string> arguments) =>
        fileName + "|" + string.Join(' ', arguments);
}
