using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Codenotch.Core;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using Codenotch.Core.Sources;

namespace Codenotch.App;

/// <summary>
/// One reading of every provider, plus what the UI needs to draw the offline state.
/// </summary>
public sealed record Snapshot(IReadOnlyList<ProviderReading> Readings, WorstReading? Worst,
                             DateTimeOffset UpdatedAt, string? FatalError);

/// <summary>
/// The poll loop. Owns the timer, the in-flight fetch's cancellation and the last
/// snapshot; everything else in the app just listens to SnapshotChanged.
///
/// Fetches run on the thread pool and the event is raised back on the dispatcher,
/// so the surfaces never touch a background thread. Nothing here is allowed to
/// throw out of a timer tick: a tray app that dies on one bad poll is worse than
/// one showing a stale number.
/// </summary>
public sealed class UsageController : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly IClock _clock;
    private readonly Action<Exception>? _log;

    private IUsageSource _source;
    private CancellationTokenSource? _inFlight;
    private bool _disposed;

    public UsageController(IUsageSource source, IClock clock, int pollSeconds,
                           Action<Exception>? log = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = IntervalFor(pollSeconds),
        };
        _timer.Tick += OnTick;
    }

    public event EventHandler<Snapshot>? SnapshotChanged;

    public Snapshot? Current { get; private set; }

    public void Start()
    {
        _timer.Start();
        // First fetch happens now, not one interval from now: an empty notch for a
        // minute after login looks broken.
        _ = RefreshNowAsync();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Replaces the source graph after a settings change and refetches.</summary>
    public void ReplaceSource(IUsageSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ = RefreshNowAsync();
    }

    public void SetPollSeconds(int pollSeconds)
    {
        _timer.Interval = IntervalFor(pollSeconds);
        if (_timer.IsEnabled)
        {
            // Restart so the new interval starts counting from now.
            _timer.Stop();
            _timer.Start();
        }
    }

    private static TimeSpan IntervalFor(int pollSeconds)
        => TimeSpan.FromSeconds(Math.Clamp(pollSeconds, Settings.MinPollSeconds, Settings.MaxPollSeconds));

    private void OnTick(object? sender, EventArgs e)
    {
        // A throw out of a Tick handler reaches DispatcherUnhandledException; the poll
        // loop is not worth risking that, even though RefreshNowAsync catches its own.
        try
        {
            _ = RefreshNowAsync();
        }
        catch (Exception ex)
        {
            _log?.Invoke(ex);
        }
    }

    /// <summary>
    /// Cancels any in-flight fetch and starts a new one. Never bypasses the 429
    /// gate — that lives in the source and is deliberately not reachable from here.
    /// </summary>
    public async Task RefreshNowAsync()
    {
        if (_disposed)
            return;

        CancellationTokenSource? previous = Interlocked.Exchange(ref _inFlight, null);
        Cancel(previous);

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _inFlight, cts);

        // Restart the timer so a manual refresh resets the clock rather than
        // landing a second fetch a moment later.
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _timer.Start();
        }

        IUsageSource source = _source;
        try
        {
            IReadOnlyList<ProviderEntry> entries =
                await Task.Run(() => source.FetchAsync(cts.Token), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
                return;
            Publish(entries);
        }
        catch (OperationCanceledException)
        {
            // A superseded poll; the newer one will publish.
        }
        catch (Exception ex)
        {
            _log?.Invoke(ex);
            Publish([], ex.Message);
        }
        finally
        {
            Interlocked.CompareExchange(ref _inFlight, null, cts);
            cts.Dispose();
        }
    }

    private void Publish(IReadOnlyList<ProviderEntry> entries, string? fatalError = null)
    {
        IReadOnlyList<ProviderReading> readings = UsageNormalizer.ReadingsFrom(entries);
        WorstReading? worst = Worst.Of(readings);

        string? fatal = fatalError;
        if (fatal is null && readings.Count == 0)
            fatal = "No providers enabled";      // extension.js's wording

        var snapshot = new Snapshot(readings, worst, _clock.UtcNow, fatal);
        Current = snapshot;
        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            // A broken handler must not stop the next poll.
            _log?.Invoke(ex);
        }
    }

    private static void Cancel(CancellationTokenSource? cts)
    {
        if (cts is null)
            return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        Cancel(Interlocked.Exchange(ref _inFlight, null));
    }
}
