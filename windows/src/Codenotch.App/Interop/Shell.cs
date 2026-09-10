using System;
using System.Runtime.InteropServices;

namespace Codenotch.App.Interop;

internal static class Shell
{
    internal const int SM_CXSMICON = 49;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// A tray context menu only dismisses on a click elsewhere while it owns the
    /// foreground; NotifyIcon does this itself on its own right-click, but a
    /// ContextMenuStrip we open by hand from a WPF window does not get it for free.
    /// </summary>
    public static void TryForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            SetForegroundWindow(hwnd);
        }
        catch (Exception)
        {
            // Worst case the menu waits for a selection instead of a click-away.
        }
    }

    /// <summary>
    /// Bitmap.GetHicon() hands out a GDI handle that the Icon wrapper does not own.
    /// Every re-render leaks one unless we destroy the old one by hand, and the tray
    /// icon re-renders on every poll — which is a handle leak with a timer attached.
    /// </summary>
    public static void TryDestroyIcon(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;
        try
        {
            DestroyIcon(handle);
        }
        catch (Exception)
        {
            // Nothing useful to do; the handle is lost either way.
        }
    }

    /// <summary>The notification area's icon size, in physical pixels. 16 when unknown.</summary>
    public static int SmallIconSize()
    {
        try
        {
            int size = GetSystemMetrics(SM_CXSMICON);
            return size > 0 ? size : 16;
        }
        catch (Exception)
        {
            return 16;
        }
    }
}
