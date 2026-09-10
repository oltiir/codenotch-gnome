using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Codenotch.App.Interop;
using Codenotch.App.Theme;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;
using Drawing = System.Drawing;

namespace Codenotch.App.Rendering;

/// <summary>
/// Renders the notification-area icon at runtime: the concentric dial by default,
/// or the session percent as bold tabular digits (the battery-tray idiom) when the
/// user asks for it.
///
/// The whole reason this is a class and not a function is handle discipline.
/// Bitmap.GetHicon() hands out a GDI icon handle that nothing owns; NotifyIcon
/// keeps using the old one until we hand it a new one, so the order is always
/// assign-then-destroy. Called on every poll, so a leak here is a leak with a
/// timer attached.
/// </summary>
internal sealed class TrayIconRenderer : IDisposable
{
    private Drawing.Icon? _current;
    private IntPtr _currentHandle;
    private bool _disposed;

    /// <summary>16 / 20 / 24 / 32 device px, matching the shell's own icon steps.</summary>
    public static int SizeForDpi(double dpiScale)
    {
        if (dpiScale < 1.25)
            return 16;
        if (dpiScale < 1.5)
            return 20;
        if (dpiScale < 2.0)
            return 24;
        return 32;
    }

    /// <summary>
    /// Builds the icon. The caller must assign it to NotifyIcon.Icon before the
    /// previous one is released, which <see cref="Retire"/> then does.
    /// </summary>
    public Drawing.Icon Render(int size, bool dark, WorstReading? worst, bool trayPercent)
    {
        if (size < 8)
            size = 16;

        // A Render whose icon never made it onto the NotifyIcon (an exception between
        // here and Retire) would otherwise strand a GDI handle for the process's life.
        DropPending();

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // An explicit transparent rect pins the visual's bounds to the icon box;
            // without it a thin dial renders off-centre.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));

            ToneKind tone = worst?.Tone ?? ToneKind.Stale;
            Color toneColor = ToneBrushes.ColorFor(tone, dark);

            if (trayPercent)
                DrawPercent(dc, size, worst, toneColor);
            else
                RingGeometry.DrawDial(dc, size, worst?.Used, worst?.Weekly, toneColor, ToneBrushes.TrackColor(dark));
        }

        // 96 dpi with an explicit pixel size: the geometry above is already in real
        // device pixels, so no further scaling must happen here.
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var bitmap = new Drawing.Bitmap(stream);
        IntPtr handle = bitmap.GetHicon();
        Drawing.Icon icon = Drawing.Icon.FromHandle(handle);
        _pendingHandle = handle;
        _pendingIcon = icon;
        return icon;
    }

    private IntPtr _pendingHandle;
    private Drawing.Icon? _pendingIcon;

    private void DropPending()
    {
        if (_pendingIcon is null && _pendingHandle == IntPtr.Zero)
            return;
        _pendingIcon?.Dispose();
        Shell.TryDestroyIcon(_pendingHandle);
        _pendingIcon = null;
        _pendingHandle = IntPtr.Zero;
    }

    /// <summary>
    /// Call once the freshly rendered icon is live on the NotifyIcon. Releases the
    /// one it replaced. A no-op when there is nothing pending, so the live icon is
    /// never dropped out from under the shell.
    /// </summary>
    public void Retire()
    {
        if (_pendingIcon is null && _pendingHandle == IntPtr.Zero)
            return;

        Drawing.Icon? old = _current;
        IntPtr oldHandle = _currentHandle;
        _current = _pendingIcon;
        _currentHandle = _pendingHandle;
        _pendingIcon = null;
        _pendingHandle = IntPtr.Zero;

        old?.Dispose();
        Shell.TryDestroyIcon(oldHandle);
    }

    /// <summary>
    /// The percent as digits filling the box. Shrinks until it fits, because 100%
    /// at 16 px otherwise runs off both edges.
    /// </summary>
    private static void DrawPercent(DrawingContext dc, int size, WorstReading? worst, Color toneColor)
    {
        string text = worst is null ? "—" : worst.Used.ToString(CultureInfo.InvariantCulture);
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        double available = size - 2;
        double fontSize = text.Length >= 3 ? size * 0.56 : size * 0.72;
        FormattedText formatted = Build(text, typeface, fontSize, toneColor);
        // Shrink rather than clip: a truncated number is worse than a small one.
        while (fontSize > 4 && (formatted.Width > available || formatted.Height > size))
        {
            fontSize -= 0.5;
            formatted = Build(text, typeface, fontSize, toneColor);
        }

        double x = (size - formatted.Width) / 2;
        double y = (size - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(Math.Max(0, x), Math.Max(0, y)));
    }

    private static FormattedText Build(string text, Typeface typeface, double fontSize, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        // No TextAlignment: without a MaxTextWidth it offsets the glyphs instead of
        // centring them, and DrawPercent already centres by measured width.
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize, brush, 96);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingIcon?.Dispose();
        Shell.TryDestroyIcon(_pendingHandle);
        _pendingIcon = null;
        _pendingHandle = IntPtr.Zero;

        _current?.Dispose();
        Shell.TryDestroyIcon(_currentHandle);
        _current = null;
        _currentHandle = IntPtr.Zero;
    }
}
