using System.Diagnostics;
using Codenotch.Core.Model;
using Codenotch.Core.Sources;
using Xunit;

namespace Codenotch.Core.Tests;

/// A scripted IUsageSource for CompositeSource tests: an optional delay, a
/// canned entry list, or an exception to throw instead.
file sealed class FakeUsageSource : IUsageSource
{
    private readonly IReadOnlyList<ProviderEntry> _entries;
    private readonly Exception? _throws;
    private readonly TimeSpan _delay;
    private readonly bool _available;

    public bool WasQueried { get; private set; }

    public FakeUsageSource(string id, IReadOnlyList<ProviderEntry>? entries = null,
        Exception? throws = null, TimeSpan delay = default, bool available = true)
    {
        Id = id;
        _entries = entries ?? Array.Empty<ProviderEntry>();
        _throws = throws;
        _delay = delay;
        _available = available;
    }

    public string Id { get; }
    public bool IsAvailable() => _available;

    public async Task<IReadOnlyList<ProviderEntry>> FetchAsync(CancellationToken ct = default)
    {
        WasQueried = true;
        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);
        ct.ThrowIfCancellationRequested();
        if (_throws is not null)
            throw _throws;
        return _entries;
    }
}

public class CompositeSourceTests
{
    private static ProviderEntry Entry(string provider) => new() { Provider = provider };

    [Fact]
    public async Task TwoAvailableSourcesAreBothQueriedAndConcatenatedInDeclaredOrder()
    {
        var slow = new FakeUsageSource("slow", new[] { Entry("slow") }, delay: TimeSpan.FromMilliseconds(80));
        var fast = new FakeUsageSource("fast", new[] { Entry("fast") });
        var composite = new CompositeSource(new IUsageSource[] { slow, fast });

        var entries = await composite.FetchAsync();

        Assert.Equal(new[] { "slow", "fast" }, entries.Select(e => e.Provider).ToArray());
    }

    [Fact]
    public async Task ASourceWhoseIsAvailableIsFalseIsNotQueried()
    {
        var unavailable = new FakeUsageSource("off", available: false);
        var composite = new CompositeSource(new IUsageSource[] { unavailable });

        await composite.FetchAsync();

        Assert.False(unavailable.WasQueried);
    }

    [Fact]
    public async Task AThrowingSourceContributesExactlyOneErrorEntry()
    {
        var throwing = new FakeUsageSource("boom", throws: new InvalidOperationException("kaboom"));
        var composite = new CompositeSource(new IUsageSource[] { throwing });

        var entries = await composite.FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("boom", entry.Provider);
        Assert.Equal("kaboom", entry.Error!.Message);
    }

    [Fact]
    public async Task ASourceReturningAnEmptyListContributesOneNoDataErrorEntry()
    {
        var empty = new FakeUsageSource("empty", Array.Empty<ProviderEntry>());
        var composite = new CompositeSource(new IUsageSource[] { empty });

        var entries = await composite.FetchAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("empty", entry.Provider);
        Assert.Equal("no data", entry.Error!.Message);
    }

    [Fact]
    public async Task ThereIsExactlyOneEntryPerQueriedSourcesProvider()
    {
        var a = new FakeUsageSource("a", new[] { Entry("a") });
        var b = new FakeUsageSource("b", new[] { Entry("b") });
        var composite = new CompositeSource(new IUsageSource[] { a, b });

        var entries = await composite.FetchAsync();

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task CancellingThePropagatesOperationCanceledException()
    {
        var slow = new FakeUsageSource("slow", delay: TimeSpan.FromSeconds(5));
        var composite = new CompositeSource(new IUsageSource[] { slow });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => composite.FetchAsync(cts.Token));
    }

    [Fact]
    public async Task SourcesRunConcurrently()
    {
        var a = new FakeUsageSource("a", new[] { Entry("a") }, delay: TimeSpan.FromMilliseconds(100));
        var b = new FakeUsageSource("b", new[] { Entry("b") }, delay: TimeSpan.FromMilliseconds(100));
        var composite = new CompositeSource(new IUsageSource[] { a, b });

        var sw = Stopwatch.StartNew();
        await composite.FetchAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200, $"expected concurrent fetch, took {sw.ElapsedMilliseconds} ms");
    }
}
