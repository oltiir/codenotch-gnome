using System;
using System.Windows.Forms;
using Codenotch.App.Rendering;
using Codenotch.Core.Presentation;
using Drawing = System.Drawing;

namespace Codenotch.App;

/// <summary>
/// The notification-area icon and its menu — the one surface that is never
/// optional, so everything here is defensive. Owns the dynamic icon's handles via
/// <see cref="TrayIconRenderer"/>.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>WinForms throws above 63 characters for NotifyIcon.Text.</summary>
    private const int TooltipLimit = 63;

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly TrayIconRenderer _renderer = new();

    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _notchItem;
    private readonly ToolStripMenuItem _percentItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _quitItem;

    private bool _suppressCheckEvents;
    private bool _disposed;

    public TrayIcon()
    {
        _refreshItem = new ToolStripMenuItem("Refresh now");
        _notchItem = new ToolStripMenuItem("Show edge notch") { CheckOnClick = true };
        _percentItem = new ToolStripMenuItem("Show percent in tray") { CheckOnClick = true };
        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _settingsItem = new ToolStripMenuItem("Settings…");
        _quitItem = new ToolStripMenuItem("Quit");

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _refreshItem,
            new ToolStripSeparator(),
            _notchItem,
            _percentItem,
            _startupItem,
            new ToolStripSeparator(),
            _settingsItem,
            _quitItem,
        ]);

        _icon = new NotifyIcon
        {
            Text = "Codenotch",
            ContextMenuStrip = _menu,
            Visible = false,
        };

        _refreshItem.Click += (_, _) => Raise(RefreshRequested);
        _settingsItem.Click += (_, _) => Raise(SettingsRequested);
        _quitItem.Click += (_, _) => Raise(QuitRequested);
        _notchItem.CheckedChanged += (_, _) => RaiseToggle(ShowNotchToggled, _notchItem.Checked);
        _percentItem.CheckedChanged += (_, _) => RaiseToggle(TrayPercentToggled, _percentItem.Checked);
        _startupItem.CheckedChanged += (_, _) => RaiseToggle(StartWithWindowsToggled, _startupItem.Checked);

        _icon.MouseClick += OnMouseClick;
    }

    public event EventHandler? LeftClicked;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler<bool>? ShowNotchToggled;
    public event EventHandler<bool>? TrayPercentToggled;
    public event EventHandler<bool>? StartWithWindowsToggled;

    public void Show() => Guarded(() => _icon.Visible = true);

    /// <summary>
    /// Reused by the notch's right click, so both menus are the same instance. The
    /// dropdown is handed the foreground afterwards: WinForms' ToolStrip modal filter
    /// needs a WinForms message loop we do not have, so without this the menu stays
    /// up until an item is picked instead of closing on a click elsewhere.
    /// </summary>
    public void ShowContextMenu() => Guarded(() =>
    {
        _menu.Show(Control.MousePosition);
        Interop.Shell.TryForeground(_menu.Handle);
    });

    public void SetChecks(bool showNotch, bool trayPercent, bool startWithWindows)
    {
        _suppressCheckEvents = true;
        try
        {
            _notchItem.Checked = showNotch;
            _percentItem.Checked = trayPercent;
            _startupItem.Checked = startWithWindows;
        }
        catch (Exception)
        {
            // A menu item that will not take a check is cosmetic.
        }
        finally
        {
            _suppressCheckEvents = false;
        }
    }

    /// <summary>
    /// Re-renders the icon and swaps it in. Called only on a new snapshot, a DPI or
    /// theme change, or a trayPercent toggle: every call costs a GDI handle round
    /// trip.
    /// </summary>
    public void Render(WorstReading? worst, bool dark, bool trayPercent)
    {
        Guarded(() =>
        {
            int size = TrayIconRenderer.SizeForDpi(Interop.Shell.SmallIconSize() / 16.0);
            Drawing.Icon icon = _renderer.Render(size, dark, worst, trayPercent);
            _icon.Icon = icon;              // assign first…
            _renderer.Retire();             // …then release the one it replaced
        });
    }

    public void SetTooltip(string text)
    {
        Guarded(() =>
        {
            string value = text ?? string.Empty;
            if (value.Length > TooltipLimit)
                value = value[..(TooltipLimit - 1)] + "…";
            _icon.Text = value;
        });
    }

    public void ShowBalloon(string text)
    {
        Guarded(() => _icon.ShowBalloonTip(10000, "Codenotch", text, ToolTipIcon.Warning));
    }

    /// <summary>
    /// "Claude 68% · resets in 59 min", or the offline/empty states. Kept short on
    /// purpose: the tooltip is capped at 63 characters.
    /// </summary>
    public static string TooltipFor(Snapshot? snapshot, DateTimeOffset now, string sourceLabel)
    {
        if (snapshot is null)
            return "Codenotch — starting…";
        if (snapshot.Worst is not WorstReading worst)
        {
            return snapshot.FatalError is "No providers enabled"
                ? "Codenotch — no providers"
                : $"Codenotch — offline ({sourceLabel})";
        }
        string countdown = UsageText.Countdown(worst.ResetsAt, now);
        return countdown.Length > 0
            ? $"{worst.Provider.Name} {worst.Used}% · resets in {countdown}"
            : $"{worst.Provider.Name} {worst.Used}%";
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Raise(LeftClicked);
    }

    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Never let a menu click take the process down; the tray is the app.
        }
    }

    private void RaiseToggle(EventHandler<bool>? handler, bool value)
    {
        if (_suppressCheckEvents)
            return;
        try
        {
            handler?.Invoke(this, value);
        }
        catch (Exception)
        {
        }
    }

    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Explorer restarting takes the tray icon with it; the next poll re-renders.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            // Hide before disposing, or Explorer leaves a ghost icon behind.
            _icon.Visible = false;
            _icon.MouseClick -= OnMouseClick;
            _icon.Icon = null;
            _icon.Dispose();
            _menu.Dispose();
        }
        catch (Exception)
        {
        }
        _renderer.Dispose();
    }
}
