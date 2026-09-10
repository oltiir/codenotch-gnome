using System;
using System.Runtime.InteropServices;

namespace Codenotch.App.Interop;

/// <summary>
/// The Windows 11 window dressing: Mica/acrylic backdrops, rounded corners, dark
/// mode for the non-client area. Every one of these attributes arrived in a
/// different Windows build, so every call is a Try* that reports failure instead
/// of throwing — on Windows 10 the windows fall back to a solid brush and still
/// work.
/// </summary>
internal static class Dwm
{
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWA_BORDER_COLOR = 34;
    internal const int DWMWA_CAPTION_COLOR = 35;
    internal const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // DWM_WINDOW_CORNER_PREFERENCE
    internal const int DWMWCP_DEFAULT = 0;
    internal const int DWMWCP_DONOTROUND = 1;
    internal const int DWMWCP_ROUND = 2;
    internal const int DWMWCP_ROUNDSMALL = 3;

    // DWM_SYSTEMBACKDROP_TYPE
    internal const int DWMSBT_AUTO = 0;
    internal const int DWMSBT_NONE = 1;
    internal const int DWMSBT_MAINWINDOW = 2;        // Mica
    internal const int DWMSBT_TRANSIENTWINDOW = 3;   // Acrylic — the flyout idiom
    internal const int DWMSBT_TABBEDWINDOW = 4;

    internal const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [DllImport("dwmapi.dll", SetLastError = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", SetLastError = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Mica/acrylic needs WindowStyle=None, AllowsTransparency=false and a
    /// transparent Background on the WPF side; when this returns false the caller
    /// must paint an opaque fallback brush instead.
    /// </summary>
    public static bool TrySetBackdrop(IntPtr hwnd, int type) => TrySet(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, type);

    public static bool TrySetCorner(IntPtr hwnd, int preference) => TrySet(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, preference);

    public static bool TrySetDarkMode(IntPtr hwnd, bool dark) => TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    /// <summary>Drops the 1 px system border so our own hairline is the only edge.</summary>
    public static bool TrySetBorderColorNone(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            uint value = DWMWA_COLOR_NONE;
            return DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref value, sizeof(uint)) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (Exception) { return false; }
    }

    private static bool TrySet(IntPtr hwnd, int attribute, int value)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            int local = value;
            return DwmSetWindowAttribute(hwnd, attribute, ref local, sizeof(int)) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (Exception) { return false; }
    }
}
