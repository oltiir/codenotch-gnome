using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Codenotch.App.Interop;

/// <summary>
/// Fullscreen detection for the notch, standing in for GNOME's trackFullscreen:
/// a video or a game covering the primary monitor should not have a pill floating
/// over it. Polled once a second, cheap, and wrong in the harmless direction (we
/// keep showing the notch) whenever a call fails.
/// </summary>
internal static class Foreground
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>
    /// True when the foreground window belongs to someone else, is not the shell
    /// itself, and covers its whole monitor bounds (not merely the working area —
    /// a maximised window is not fullscreen).
    /// </summary>
    public static bool IsFullscreenOnPrimary()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;                  // nothing focused: the desktop, so not fullscreen
            if (!IsWindowVisible(hwnd))
                return false;

            if (GetWindowThreadProcessId(hwnd, out uint pid) != 0
                && pid == (uint)Environment.ProcessId)
            {
                return false;              // our own flyout or callout
            }

            string className = ClassNameOf(hwnd);
            // The desktop and the taskbar are permanently "fullscreen" by this test.
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow"
                or "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow")
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out RECT window))
                return false;

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info))
                return false;

            // The notch only ever lives on the primary monitor, so a fullscreen video on
            // a second screen must not hide it.
            if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0)
                return false;

            RECT bounds = info.rcMonitor;
            return window.Left <= bounds.Left && window.Top <= bounds.Top
                && window.Right >= bounds.Right && window.Bottom >= bounds.Bottom;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        try
        {
            var buffer = new StringBuilder(256);
            int length = GetClassNameW(hwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString(0, length) : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
