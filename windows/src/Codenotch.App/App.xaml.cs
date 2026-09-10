using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Codenotch.Core;
using Codenotch.Core.Presentation;
using Codenotch.Core.Sources;
using Codenotch.Core.Sources.Claude;
using Codenotch.Core.Sources.Codex;
using Codenotch.App.Theme;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Codenotch.App;

/// <summary>
/// The whole lifecycle: one instance, one HttpClient, one poll loop, three
/// surfaces. Nothing here is allowed to throw far enough to show a crash dialog —
/// a tray app that pops a stack trace at login is worse than one that quietly logs
/// and keeps its last reading on screen.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Codenotch.SingleInstance.v1";

    private static Mutex? _singleInstance;
    private static bool _ownsMutex;

    private HttpClient? _http;
    private UsageController? _controller;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private NotchWindow? _notch;
    private SettingsWindow? _settingsWindow;
    private ThresholdNotifier _notifier = new();
    private Settings _settings = Settings.Default;
    private RateLimitGate? _gate;
    private ClaudeVersionProbe? _versionProbe;
    private ClaudeRefreshPolicy? _refreshPolicy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF hosting WinForms: the ComCtl32 v6 activation context has to be turned on
        // before the first WinForms handle exists, or the tray menu and the balloon tips
        // render in the pre-XP style. The DPI mode is deliberately NOT set here — the
        // manifest already declares PerMonitorV2 and SetHighDpiMode would fight it.
        // SetCompatibleTextRenderingDefault is deliberately not called: false is already
        // the default and it throws once any WinForms handle exists.
        WinForms.Application.EnableVisualStyles();

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Already running: the tray icon the user is looking for is the other one.
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }
        _ownsMutex = true;

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log(ex);
        };

        _settings = SettingsStore.Load(SettingsStore.DefaultPath);
        ToneBrushes.Apply(ToneBrushes.ReadSystemIsDark());

        _http = BuildHttpClient();
        var runner = new ProcessRunner();
        _gate = new RateLimitGate(SystemClock.Instance);
        _versionProbe = new ClaudeVersionProbe(runner);
        _refreshPolicy = new ClaudeRefreshPolicy(runner, SystemClock.Instance);

        _tray = new TrayIcon();
        WireTray(_tray);

        _controller = new UsageController(BuildSource(), SystemClock.Instance,
            _settings.EffectivePollSeconds, log: Log);
        _controller.SnapshotChanged += OnSnapshot;

        _flyout = new FlyoutWindow();
        _flyout.RefreshRequested += (_, _) => RefreshNow();
        _flyout.SettingsRequested += (_, _) => OpenSettings();
        ConfigureFlyout();

        if (_settings.ShowNotch)
            CreateNotch();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tray.SetChecks(_settings.ShowNotch, _settings.TrayPercent, StartupRegistration.IsEnabled);
        _tray.Render(null, ToneBrushes.IsDark, _settings.TrayPercent);
        _tray.SetTooltip(TrayIcon.TooltipFor(null, DateTimeOffset.UtcNow, SourceLabel()));
        _tray.Show();

        // Start() does the first fetch immediately rather than one interval from now.
        _controller.Start();
    }

    /// <summary>
    /// One long-lived client for the whole process, per the plan: connection reuse
    /// matters when we poll two vendors every minute for hours.
    /// </summary>
    private static HttpClient BuildHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Endpoint mode is one source; built-in mode is Claude and Codex fanned out,
    /// filtered by Settings.Providers when the user pinned a list.
    /// </summary>
    private IUsageSource BuildSource()
    {
        HttpClient http = _http ??= BuildHttpClient();

        if (_settings.IsEndpointMode)
            return new EndpointSource(http, _settings.Endpoint!);

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IReadOnlyDictionary<string, string?> env = ReadEnvironment();

        var sources = new List<IUsageSource>(2);
        if (Wanted(UsageSourceIds.Claude))
        {
            sources.Add(new ClaudeOAuthSource(http,
                ClaudeCredentialsFile.ResolvePath(env, userProfile),
                _refreshPolicy ??= new ClaudeRefreshPolicy(new ProcessRunner(), SystemClock.Instance),
                _gate ??= new RateLimitGate(SystemClock.Instance),
                _versionProbe ??= new ClaudeVersionProbe(new ProcessRunner()),
                SystemClock.Instance));
        }
        if (Wanted(UsageSourceIds.Codex))
        {
            sources.Add(new CodexOAuthSource(http,
                CodexCredentialsFile.ResolvePath(env, userProfile),
                SystemClock.Instance));
        }
        return new CompositeSource(sources);
    }

    private bool Wanted(string id)
    {
        IReadOnlyList<string>? providers = _settings.Providers;
        if (providers is null)
            return true;                    // automatic: IsAvailable() decides
        foreach (string provider in providers)
        {
            if (string.Equals(provider, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { ClaudeCredentialsFile.ConfigDirVariable, CodexCredentialsFile.HomeVariable })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                env[name] = value;
        }
        return env;
    }

    private void WireTray(TrayIcon tray)
    {
        tray.LeftClicked += (_, _) => _flyout?.ToggleFlyout();
        tray.RefreshRequested += (_, _) => RefreshNow();
        tray.SettingsRequested += (_, _) => OpenSettings();
        tray.QuitRequested += (_, _) => Shutdown(0);
        tray.ShowNotchToggled += (_, value) => ApplySettings(_settings with { ShowNotch = value });
        tray.TrayPercentToggled += (_, value) => ApplySettings(_settings with { TrayPercent = value });
        tray.StartWithWindowsToggled += (_, value) =>
        {
            if (value)
                StartupRegistration.Enable();
            else
                StartupRegistration.Disable();
            tray.SetChecks(_settings.ShowNotch, _settings.TrayPercent, StartupRegistration.IsEnabled);
        };
    }

    private void RefreshNow() => _ = _controller?.RefreshNowAsync();

    private void OnSnapshot(object? sender, Snapshot snapshot)
    {
        try
        {
            _tray?.Render(snapshot.Worst, ToneBrushes.IsDark, _settings.TrayPercent);
            _tray?.SetTooltip(TrayIcon.TooltipFor(snapshot, DateTimeOffset.UtcNow, SourceLabel()));
            _flyout?.Update(snapshot);
            _notch?.Update(snapshot, SourceLabel());

            foreach (string message in _notifier.Evaluate(snapshot.Readings, snapshot.UpdatedAt,
                         TimeZoneInfo.Local, _settings.Notify))
            {
                _tray?.ShowBalloon(message);
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    /// <summary>What the offline states name: the vendor host, or the endpoint's host.</summary>
    private string SourceLabel()
    {
        if (_settings.IsEndpointMode)
        {
            return Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out Uri? uri)
                ? uri.Host
                : "the endpoint";
        }
        if (!Wanted(UsageSourceIds.Claude) && Wanted(UsageSourceIds.Codex))
            return "chatgpt.com";
        return "api.anthropic.com";
    }

    private void ConfigureFlyout()
        => _flyout?.Configure(_settings.IsEndpointMode, _settings.Endpoint, SourceLabel());

    private void CreateNotch()
    {
        if (_notch is not null)
            return;
        try
        {
            _notch = new NotchWindow();
            _notch.ContextMenuRequested += (_, _) => _tray?.ShowContextMenu();
            _notch.Show();
            _notch.Place();
            _notch.StartWatching();
            if (_controller?.Current is Snapshot snapshot)
                _notch.Update(snapshot, SourceLabel());
        }
        catch (Exception ex)
        {
            Log(ex);
            _notch = null;
        }
    }

    private void DestroyNotch()
    {
        if (_notch is null)
            return;
        try
        {
            _notch.StopWatching();
            _notch.Close();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
        _notch = null;
    }

    private void OpenSettings()
    {
        try
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }
            var window = new SettingsWindow(_settings, SettingsStore.DefaultPath);
            window.Saved += (_, saved) => ApplySettings(saved, persist: false);
            window.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            Log(ex);
            _settingsWindow = null;
        }
    }

    /// <summary>
    /// Applies a settings change live: rebuild the source graph when the source
    /// changed, reset the timer when the interval did, show or hide the notch, and
    /// re-render the tray icon.
    /// </summary>
    private void ApplySettings(Settings updated, bool persist = true)
    {
        Settings previous = _settings;
        _settings = updated;

        if (persist)
        {
            try
            {
                SettingsStore.Save(SettingsStore.DefaultPath, updated);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        try
        {
            bool sourceChanged = previous.Endpoint != updated.Endpoint
                || !SameProviders(previous.Providers, updated.Providers);

            if (previous.EffectivePollSeconds != updated.EffectivePollSeconds)
                _controller?.SetPollSeconds(updated.EffectivePollSeconds);

            if (sourceChanged)
            {
                ConfigureFlyout();
                _notifier = new ThresholdNotifier();     // thresholds are per source graph
                _controller?.ReplaceSource(BuildSource());
            }

            if (updated.ShowNotch && _notch is null)
                CreateNotch();
            else if (!updated.ShowNotch && _notch is not null)
                DestroyNotch();

            _tray?.SetChecks(updated.ShowNotch, updated.TrayPercent, StartupRegistration.IsEnabled);
            _tray?.Render(_controller?.Current?.Worst, ToneBrushes.IsDark, updated.TrayPercent);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private static bool SameProviders(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// SystemEvents raises on its own ".NET System Events" thread, never on the UI
    /// thread, so every handler below has to hop onto the dispatcher first: brushes,
    /// windows and DrawingVisuals are all thread-affine and would otherwise throw
    /// (silently, into the log) and leave the theme half-applied.
    /// </summary>
    private void OnDispatcher(Action action)
    {
        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            Guarded(action);
            return;
        }
        try
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Guarded(action)));
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle))
        {
            return;
        }
        OnDispatcher(() =>
        {
            bool dark = ToneBrushes.ReadSystemIsDark();
            // Mutates the brushes in place, so every binding and every drawn ring follows.
            ToneBrushes.Apply(dark);
            _flyout?.ApplyTheme(dark);
            _notch?.ApplyTheme(dark);
            _tray?.Render(_controller?.Current?.Worst, dark, _settings.TrayPercent);
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        OnDispatcher(() =>
        {
            _notch?.Place();
            _tray?.Render(_controller?.Current?.Worst, ToneBrushes.IsDark, _settings.TrayPercent);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }
        catch (Exception)
        {
        }

        _controller?.Dispose();
        DestroyNotch();
        try
        {
            _flyout?.Close();
        }
        catch (Exception)
        {
        }

        // Dispose the NotifyIcon before shutdown finishes, or Explorer keeps a ghost
        // icon in the notification area until the user hovers it.
        _tray?.Dispose();
        _http?.Dispose();

        if (_ownsMutex && _singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (Exception)
            {
            }
            _singleInstance.Dispose();
            _singleInstance = null;
            _ownsMutex = false;
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Appends to %LOCALAPPDATA%\Codenotch\error.log. Best effort by design: if we
    /// cannot even log, there is nothing sensible left to do.
    /// </summary>
    internal static void Log(Exception ex)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codenotch");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "error.log");
            File.AppendAllText(path,
                $"{DateTimeOffset.UtcNow:O} {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }
}
