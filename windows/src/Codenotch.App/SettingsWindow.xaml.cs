using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Codenotch.App.Interop;
using Codenotch.App.Theme;
using Codenotch.Core;

namespace Codenotch.App;

/// <summary>
/// The settings editor. Everything except "Start with Windows" lives in
/// settings.json; that one is the HKCU Run key, read and written here directly.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly string _settingsPath;

    public SettingsWindow(Settings settings, string settingsPath)
    {
        InitializeComponent();
        _settingsPath = settingsPath;

        SourceInitialized += OnSourceInitialized;
        SaveButton.Click += OnSave;
        CancelButton.Click += (_, _) => Close();
        EndpointCheck.Checked += (_, _) => EndpointBox.IsEnabled = true;
        EndpointCheck.Unchecked += (_, _) => EndpointBox.IsEnabled = false;
        AutoProvidersCheck.Checked += (_, _) => SetProviderBoxesEnabled(false);
        AutoProvidersCheck.Unchecked += (_, _) => SetProviderBoxesEnabled(true);

        Load(settings);
    }

    /// <summary>Raised with the saved settings, on the dispatcher, before the window closes.</summary>
    public event EventHandler<Settings>? Saved;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Dwm.TrySetCorner(hwnd, Dwm.DWMWCP_ROUND);
            Dwm.TrySetDarkMode(hwnd, ToneBrushes.IsDark);
            Dwm.TrySetBackdrop(hwnd, Dwm.DWMSBT_MAINWINDOW);
        }
        catch (Exception)
        {
        }
    }

    private void Load(Settings settings)
    {
        PollBox.Text = settings.PollSeconds.ToString(CultureInfo.InvariantCulture);

        bool endpointMode = settings.IsEndpointMode;
        EndpointCheck.IsChecked = endpointMode;
        EndpointBox.Text = settings.Endpoint ?? string.Empty;
        EndpointBox.IsEnabled = endpointMode;
        EndpointHint.Foreground = ToneBrushes.Muted;
        PathText.Foreground = ToneBrushes.Muted;

        bool auto = settings.Providers is null;
        AutoProvidersCheck.IsChecked = auto;
        ClaudeCheck.IsChecked = auto || Contains(settings.Providers, "claude");
        CodexCheck.IsChecked = auto || Contains(settings.Providers, "codex");
        SetProviderBoxesEnabled(!auto);

        NotchCheck.IsChecked = settings.ShowNotch;
        PercentCheck.IsChecked = settings.TrayPercent;
        NotifyCheck.IsChecked = settings.Notify;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled;

        PathText.Text = $"Settings file: {_settingsPath}";
    }

    private static bool Contains(IReadOnlyList<string>? providers, string id)
    {
        if (providers is null)
            return false;
        foreach (string provider in providers)
        {
            if (string.Equals(provider, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void SetProviderBoxesEnabled(bool enabled)
    {
        ClaudeCheck.IsEnabled = enabled;
        CodexCheck.IsEnabled = enabled;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(PollBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int pollSeconds)
                || pollSeconds < Settings.MinPollSeconds || pollSeconds > Settings.MaxPollSeconds)
            {
                Fail($"Poll interval must be a whole number between {Settings.MinPollSeconds} and {Settings.MaxPollSeconds} seconds.");
                return;
            }

            string? endpoint = null;
            if (EndpointCheck.IsChecked == true)
            {
                endpoint = EndpointBox.Text.Trim();
                if (endpoint.Length == 0)
                {
                    Fail("Give the endpoint a URL, or untick it to use the built-in providers.");
                    return;
                }
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    Fail("The endpoint must be an http:// or https:// URL.");
                    return;
                }
            }

            IReadOnlyList<string>? providers = null;
            if (AutoProvidersCheck.IsChecked != true)
            {
                var chosen = new List<string>(2);
                if (ClaudeCheck.IsChecked == true)
                    chosen.Add("claude");
                if (CodexCheck.IsChecked == true)
                    chosen.Add("codex");
                if (chosen.Count == 0)
                {
                    Fail("Pick at least one provider, or switch providers back to Automatic.");
                    return;
                }
                providers = chosen;
            }

            var settings = new Settings
            {
                PollSeconds = pollSeconds,
                Endpoint = endpoint,
                ShowNotch = NotchCheck.IsChecked == true,
                TrayPercent = PercentCheck.IsChecked == true,
                Notify = NotifyCheck.IsChecked == true,
                Providers = providers,
            };

            try
            {
                SettingsStore.Save(_settingsPath, settings);
            }
            catch (Exception ex)
            {
                Fail($"Could not write {_settingsPath}: {ex.Message}");
                return;
            }

            // The Run key is separate on purpose; failure there is worth saying.
            bool wantStartup = StartupCheck.IsChecked == true;
            if (wantStartup != StartupRegistration.IsEnabled)
            {
                bool ok = wantStartup ? StartupRegistration.Enable() : StartupRegistration.Disable();
                if (!ok)
                {
                    Fail("Settings were saved, but the Start with Windows registry value could not be changed.");
                    return;
                }
            }

            Saved?.Invoke(this, settings);
            Close();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
