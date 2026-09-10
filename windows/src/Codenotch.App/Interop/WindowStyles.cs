using System;
using System.Runtime.InteropServices;

namespace Codenotch.App.Interop;

/// <summary>
/// Extended window styles, applied from SourceInitialized — before the window is
/// shown — so the taskbar never flashes the notch or the flyout on the way past.
/// </summary>
internal static class WindowStyles
{
    internal const int GWL_EXSTYLE = -20;

    internal const int WS_EX_TOOLWINDOW = 0x00000080;   // no Alt-Tab, no taskbar button
    internal const int WS_EX_NOACTIVATE = 0x08000000;   // never takes focus
    internal const int WS_EX_TRANSPARENT = 0x00000020;  // click-through

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    public static void AddExStyles(IntPtr hwnd, int styles) => Update(hwnd, current => current | styles);

    public static void RemoveExStyles(IntPtr hwnd, int styles) => Update(hwnd, current => current & ~styles);

    private static void Update(IntPtr hwnd, Func<int, int> transform)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            // GetWindowLongPtr only exists as a real export in 64-bit user32.
            int current = Environment.Is64BitProcess
                ? (int)GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
                : GetWindowLong32(hwnd, GWL_EXSTYLE);
            int updated = transform(current);
            if (updated == current)
                return;
            if (Environment.Is64BitProcess)
                SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(updated));
            else
                SetWindowLong32(hwnd, GWL_EXSTYLE, updated);
        }
        catch (Exception)
        {
            // A window without WS_EX_TOOLWINDOW gets a taskbar button: ugly, not fatal.
        }
    }
}
