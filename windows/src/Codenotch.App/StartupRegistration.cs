using System;
using Microsoft.Win32;

namespace Codenotch.App;

/// <summary>
/// "Start with Windows", stored only in the HKCU Run key. Deliberately not in
/// settings.json: the registry is the thing Windows actually reads, so a copied
/// settings file cannot claim the app autostarts when it does not.
/// </summary>
internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Codenotch";

    /// <summary>
    /// Environment.ProcessPath, not Assembly.Location: under single-file publish the
    /// latter is empty.
    /// </summary>
    private static string? ExePath => Environment.ProcessPath;

    /// <summary>True only when the value exists AND points at this exe.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
                if (key?.GetValue(ValueName) is not string value || value.Length == 0)
                    return false;
                string expected = ExePath ?? string.Empty;
                if (expected.Length == 0)
                    return true;              // cannot compare; the value existing is enough
                return string.Equals(value.Trim('"'), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static bool Enable()
    {
        string? exe = ExePath;
        if (string.IsNullOrEmpty(exe))
            return false;
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
                ?? throw new InvalidOperationException("the Run key could not be opened");
            // Quoted: the path contains spaces under %LOCALAPPDATA%\Programs.
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
