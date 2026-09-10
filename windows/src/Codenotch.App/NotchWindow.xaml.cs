using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Codenotch.App.Controls;
using Codenotch.App.Interop;
using Codenotch.App.Theme;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using WinForms = System.Windows.Forms;
// Window.Foreground (a Brush) shadows the interop helper's name, and `Interop` alone
// would be ambiguous with System.Windows.Interop, so the probe gets an alias.
using FullscreenProbe = Codenotch.App.Interop.Foreground;

namespace Codenotch.App;

/// <summary>
/// The edge notch: a left-rounded pill flush to the primary monitor's right edge,
/// vertically centred, holding one dial per provider. Hovering slides a callout out
/// to the left with the same detail blocks the flyout shows; a click pins it open
/// and a right click opens the tray menu (ruling I.6).
///
/// It hides itself while a fullscreen window owns the primary monitor, standing in
/// for GNOME's trackFullscreen.
/// </summary>
public partial class NotchWindow : Window
{
    private const double RingSize = 46;          // RING_SIZE in extension.js
    private const double CalloutBarWidth = 120;  // BAR_WIDTH
    private const int FullscreenPollSeconds = 1;

    private readonly Dictionary<string, ProviderDetail> _blocks = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _fullscreenTimer;
    private bool _placeQueued;
    private bool _open;
    private bool _hiddenForFullscreen;
    private Snapshot? _snapshot;
    private string _sourceLabel = "the source";

    public NotchWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => QueuePlace();
        DpiChanged += (_, _) => QueuePlace();

        Pill.MouseEnter += (_, _) => SetOpen(true);
        Pill.MouseLeave += (_, _) => SetOpen(false);
        Pill.MouseLeftButtonUp += OnLeftButtonUp;
        Pill.MouseRightButtonUp += OnRightButtonUp;

        _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(FullscreenPollSeconds),
        };
        _fullscreenTimer.Tick += (_, _) => PollFullscreen();

        ApplyTheme(ToneBrushes.IsDark);
    }

    /// <summary>Right click hands the menu back to the tray, so both use one instance.</summary>
    public event EventHandler? ContextMenuRequested;

    public bool IsPinned { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // No Alt-Tab, no taskbar button, and never takes focus from the editor.
            WindowStyles.AddExStyles(hwnd, WindowStyles.WS_EX_TOOLWINDOW | WindowStyles.WS_EX_NOACTIVATE);
        }
        catch (Exception)
        {
        }
        QueuePlace();
    }

    public void ApplyTheme(bool dark)
    {
        // A Border with a null Background is not hit-testable, and the notch lives or
        // dies on MouseEnter, so both surfaces always carry a brush.
        Pill.Background = _open ? ToneBrushes.NotchOpen : ToneBrushes.Notch;
        Pill.BorderBrush = ToneBrushes.NotchBorder;
        Callout.Background = System.Windows.Media.Brushes.Transparent;
        Callout.BorderBrush = ToneBrushes.NotchBorder;
        if (_snapshot is not null)
            Update(_snapshot, _sourceLabel);
    }

    public void StartWatching()
    {
        _fullscreenTimer.Start();
        PollFullscreen();
    }

    public void StopWatching() => _fullscreenTimer.Stop();

    public void Update(Snapshot snapshot, string sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _sourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "the source" : sourceLabel;

        try
        {
            Dials.Children.Clear();
            CalloutBody.Children.Clear();

            bool any = false;
            foreach (ProviderReading reading in snapshot.Readings)
            {
                if (!reading.HasData || reading.Session is not UsageWindow session)
                    continue;
                any = true;
                Dials.Children.Add(Cell(reading, session));
                CalloutBody.Children.Add(BlockFor(reading, snapshot.UpdatedAt));
            }

            if (!any)
                BuildOffline(snapshot);

            QueuePlace();
        }
        catch (Exception)
        {
            // Never throw back into the poll loop.
        }
    }

    private UIElement Cell(ProviderReading reading, UsageWindow session)
    {
        int? weekly = reading.Weekly?.Used;
        var cell = new StackPanel
        {
            Margin = new Thickness(0, Dials.Children.Count > 0 ? 12 : 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var dial = new Grid { Width = RingSize, Height = RingSize };
        dial.Children.Add(new RingsControl
        {
            Diameter = RingSize,
            SessionUsed = session.Used,
            WeeklyUsed = weekly,
        });
        dial.Children.Add(new TextBlock
        {
            Text = reading.Provider.Glyph,
            FontFamily = ProviderDetail.GlyphFont,
            FontSize = 13,
            Foreground = ToneBrushes.Glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        cell.Children.Add(dial);

        var percent = new TextBlock
        {
            Text = $"{session.Used}%",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = ToneBrushes.For(Tone.Of(session.Used)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        cell.Children.Add(percent);

        if (weekly is int wk)
        {
            var sub = new TextBlock
            {
                Text = $"wk {wk}%",
                FontSize = 10,
                Foreground = ToneBrushes.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            };
            Typography.SetNumeralAlignment(sub, FontNumeralAlignment.Tabular);
            cell.Children.Add(sub);
        }

        return cell;
    }

    /// <summary>One grey dial with "!" and "offline", the reason in the callout.</summary>
    private void BuildOffline(Snapshot snapshot)
    {
        var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var dial = new Grid { Width = RingSize, Height = RingSize };
        dial.Children.Add(new RingsControl { Diameter = RingSize, SessionUsed = null, WeeklyUsed = null });
        dial.Children.Add(new TextBlock
        {
            Text = "!",
            FontFamily = ProviderDetail.GlyphFont,
            FontSize = 13,
            Foreground = ToneBrushes.Glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        cell.Children.Add(dial);
        cell.Children.Add(new TextBlock
        {
            Text = "offline",
            FontSize = 10,
            Foreground = ToneBrushes.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });
        Dials.Children.Add(cell);

        string reason = snapshot.FatalError ?? FirstError(snapshot.Readings) ?? "no data";
        CalloutBody.Children.Add(new TextBlock
        {
            Text = $"Can't reach {_sourceLabel}:\n{reason}",
            FontSize = 11,
            Foreground = ToneBrushes.Muted,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
        });
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

    private UIElement BlockFor(ProviderReading reading, DateTimeOffset now)
    {
        if (!_blocks.TryGetValue(reading.Provider.Id, out ProviderDetail? detail))
        {
            detail = new ProviderDetail { BarWidth = CalloutBarWidth };
            _blocks[reading.Provider.Id] = detail;
        }
        detail.Margin = new Thickness(0, CalloutBody.Children.Count > 0 ? 14 : 0, 0, 0);
        detail.Update(reading, now, TimeZoneInfo.Local);
        return detail;
    }

    private void OnLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        IsPinned = !IsPinned;
        SetOpen(IsPinned || Pill.IsMouseOver);
        e.Handled = true;
    }

    private void OnRightButtonUp(object? sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>160 ms ease-out in, 120 ms ease-in out — the GNOME timings.</summary>
    private void SetOpen(bool open)
    {
        if (IsPinned && !open)
            return;

        _open = open;
        try
        {
            // Read the animated value BEFORE clearing the animation: BeginAnimation(null)
            // snaps the property back to its local base (0 from the XAML), so reading it
            // afterwards made every fade start — and the fade-out end — at zero, i.e. the
            // callout vanished instantly instead of fading.
            double from = Callout.Opacity;
            Callout.BeginAnimation(UIElement.OpacityProperty, null);
            Callout.Opacity = from;
            Pill.Background = open ? ToneBrushes.NotchOpen : ToneBrushes.Notch;

            if (open)
            {
                Callout.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(from, 1.0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                Callout.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
            else
            {
                var fadeOut = new DoubleAnimation(from, 0.0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                };
                fadeOut.Completed += (_, _) =>
                {
                    if (_open)
                        return;                      // re-entered before the fade finished
                    Callout.Visibility = Visibility.Collapsed;
                    QueuePlace();
                };
                Callout.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            QueuePlace();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The _queuePlaceNotch idle-callback pattern: many triggers, one placement per
    /// dispatcher turn.
    /// </summary>
    private void QueuePlace()
    {
        if (_placeQueued)
            return;
        _placeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _placeQueued = false;
            Place();
        }));
    }

    public void Place()
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

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            // Working area, not bounds: a right-hand taskbar must not sit on top of it.
            Left = bottomRight.X - width;
            Top = topLeft.Y + Math.Max(0, (bottomRight.Y - topLeft.Y - height) / 2);
        }
        catch (Exception)
        {
        }
    }

    private void PollFullscreen()
    {
        try
        {
            bool fullscreen = FullscreenProbe.IsFullscreenOnPrimary();
            if (fullscreen == _hiddenForFullscreen)
                return;
            _hiddenForFullscreen = fullscreen;
            if (fullscreen)
            {
                Hide();
            }
            else
            {
                Show();
                QueuePlace();
            }
        }
        catch (Exception)
        {
        }
    }
}
