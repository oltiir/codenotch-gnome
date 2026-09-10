using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Codenotch.App.Controls;
using Codenotch.App.Interop;
using Codenotch.App.Theme;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using WinForms = System.Windows.Forms;

namespace Codenotch.App;

/// <summary>
/// The left-click flyout, anchored near the tray the way the Wi-Fi and Battery
/// flyouts are: borderless, rounded, acrylic, no taskbar entry, gone on deactivate
/// or Esc. Content is the GNOME popup — one ProviderDetail block per provider,
/// with a footer saying how fresh the reading is.
/// </summary>
public partial class FlyoutWindow : Window
{
    private const double BarWidth = 160;      // extension.js uses BAR_WIDTH + 40 in the popup
    private const double EdgeGap = 12;
    private const double ReopenGuardMs = 300;
    private const double TextMaxWidth = 460;   // inside the window's MaxWidth, so it wraps

    private readonly Dictionary<string, ProviderDetail> _blocks = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _freshness;
    private Snapshot? _snapshot;
    private string _sourceLabel = "the source";
    private bool _endpointMode;
    private string? _endpointUrl;
    private bool _backdropActive;
    private DateTime _hiddenAt = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();

        _freshness = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _freshness.Tick += (_, _) => UpdateFreshness();

        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        SettingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) => Anchor();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;

    public void Configure(bool endpointMode, string? endpointUrl, string sourceLabel)
    {
        _endpointMode = endpointMode;
        _endpointUrl = endpointUrl;
        _sourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "the source" : sourceLabel;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // No Alt-Tab entry, but NOT WS_EX_NOACTIVATE: the flyout needs focus so
            // Deactivated is what closes it.
            WindowStyles.AddExStyles(hwnd, WindowStyles.WS_EX_TOOLWINDOW);
            Dwm.TrySetCorner(hwnd, Dwm.DWMWCP_ROUND);
            Dwm.TrySetBorderColorNone(hwnd);
            Dwm.TrySetDarkMode(hwnd, ToneBrushes.IsDark);
            _backdropActive = Dwm.TrySetBackdrop(hwnd, Dwm.DWMSBT_TRANSIENTWINDOW);
            if (!_backdropActive)
                Background = ToneBrushes.Surface;      // Windows 10: solid instead of acrylic
        }
        catch (Exception)
        {
            _backdropActive = false;
            Background = ToneBrushes.Surface;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideFlyout();
        }
    }

    public void ApplyTheme(bool dark)
    {
        try
        {
            Dwm.TrySetDarkMode(new WindowInteropHelper(this).Handle, dark);
        }
        catch (Exception)
        {
        }
        if (!_backdropActive)
            Background = ToneBrushes.Surface;
        UpdatedText.Foreground = ToneBrushes.Muted;
        if (_snapshot is not null)
            Update(_snapshot);
    }

    /// <summary>Toggles visibility; the tray's left click calls this.</summary>
    public void ToggleFlyout()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }
        // Clicking the tray icon activates the taskbar first, which fires Deactivated
        // and hides us; without this guard the click that should close the flyout
        // reopens it instead.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < ReopenGuardMs)
            return;
        ShowFlyout();
    }

    public void ShowFlyout()
    {
        try
        {
            Show();
            Anchor();
            Activate();
            _freshness.Start();
            UpdateFreshness();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Hide, never Close: recreating the HWND would lose the DWM attributes.</summary>
    public void HideFlyout()
    {
        _hiddenAt = DateTime.UtcNow;
        _freshness.Stop();
        try
        {
            Hide();
        }
        catch (Exception)
        {
        }
    }

    public void Update(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        UpdatedText.Foreground = ToneBrushes.Muted;

        try
        {
            Body.Children.Clear();

            bool anyData = false;
            foreach (ProviderReading reading in snapshot.Readings)
            {
                if (reading.HasData)
                    anyData = true;
                Body.Children.Add(BlockFor(reading, snapshot.UpdatedAt));
            }

            if (!anyData)
            {
                string reason = snapshot.FatalError ?? FirstError(snapshot.Readings) ?? "no data";
                Body.Children.Add(new TextBlock
                {
                    Text = $"No reading: {reason}",
                    FontSize = 12,
                    Foreground = ToneBrushes.Text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = TextMaxWidth,
                    Margin = new Thickness(0, Body.Children.Count > 0 ? 12 : 0, 0, 0),
                });
                Body.Children.Add(new TextBlock
                {
                    Text = _endpointMode
                        ? $"Check: curl {_endpointUrl}"
                        : "Check that Claude Code is signed in",
                    FontSize = 11,
                    Foreground = ToneBrushes.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = TextMaxWidth,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            UpdateFreshness();
        }
        catch (Exception)
        {
            // A layout failure must not kill the poll loop that called us.
        }
    }

    private UIElement BlockFor(ProviderReading reading, DateTimeOffset now)
    {
        // Reuse the block per provider so WPF is not rebuilding visuals every poll.
        if (!_blocks.TryGetValue(reading.Provider.Id, out ProviderDetail? detail))
        {
            detail = new ProviderDetail { BarWidth = BarWidth };
            _blocks[reading.Provider.Id] = detail;
        }
        detail.Margin = new Thickness(0, Body.Children.Count > 0 ? 16 : 0, 0, 0);
        detail.Update(reading, now, TimeZoneInfo.Local);
        return detail;
    }

    private static string? FirstError(IReadOnlyList<ProviderReading> readings)
    {
        foreach (ProviderReading reading in readings)
        {
            if (reading.Error is string error && error.Length > 0)
                return error;
        }
        return null;
    }

    private void UpdateFreshness()
    {
        if (_snapshot is null)
        {
            UpdatedText.Text = string.Empty;
            return;
        }
        double seconds = (DateTimeOffset.UtcNow - _snapshot.UpdatedAt).TotalSeconds;
        if (seconds < 0)
            seconds = 0;
        UpdatedText.Text = seconds < 90
            ? $"Updated {(int)seconds} s ago"
            : $"Updated {(int)(seconds / 60)} min ago";
    }

    /// <summary>
    /// Bottom-right of the primary screen's working area by default, flipped when the
    /// taskbar is somewhere else. The working area is in physical pixels, so it has
    /// to go through TransformFromDevice — the only correct conversion under
    /// Per-Monitor V2.
    /// </summary>
    private void Anchor()
    {
        try
        {
            WinForms.Screen? screen = WinForms.Screen.PrimaryScreen;
            if (screen is null)
                return;

            CompositionTarget? target = PresentationSource.FromVisual(this)?.CompositionTarget;
            if (target is null)
                return;

            Point topLeft = target.TransformFromDevice.Transform(
                new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            Point bottomRight = target.TransformFromDevice.Transform(
                new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
            Point boundsTopLeft = target.TransformFromDevice.Transform(
                new Point(screen.Bounds.Left, screen.Bounds.Top));

            double width = ActualWidth > 0 ? ActualWidth : MinWidth;
            double height = ActualHeight > 0 ? ActualHeight : 200;
            if (double.IsNaN(width) || double.IsNaN(height))
                return;

            Left = Math.Max(topLeft.X + EdgeGap, bottomRight.X - width - EdgeGap);
            // A top taskbar means the tray is up there, so the flyout goes with it.
            Top = screen.WorkingArea.Top > screen.Bounds.Top
                ? topLeft.Y + EdgeGap
                : Math.Max(boundsTopLeft.Y, bottomRight.Y - height - EdgeGap);
        }
        catch (Exception)
        {
            // Leave it where WPF put it rather than not showing at all.
        }
    }
}
