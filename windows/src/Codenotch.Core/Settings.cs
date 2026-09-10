namespace Codenotch.Core;

/// <summary>
/// Everything the user can change, persisted as camelCase JSON at
/// <see cref="SettingsStore.DefaultPath"/>. Deliberately flat and small.
///
/// "Start with Windows" is NOT here: it lives only in the HKCU Run key, so the
/// registry stays the single source of truth and a copied settings.json cannot
/// claim the app autostarts when it does not.
/// </summary>
public sealed record Settings
{
    /// <summary>
    /// null means "never set", which is what lets <see cref="EffectivePollSeconds"/>
    /// pick 30 s in endpoint mode and 60 s built-in. The public property still
    /// reports 60 so the settings window has a number to show.
    /// </summary>
    private readonly int? _pollSeconds;

    public const int DefaultBuiltInPollSeconds = 60;
    public const int DefaultEndpointPollSeconds = 30;
    public const int MinPollSeconds = 10;
    public const int MaxPollSeconds = 3600;

    public int PollSeconds
    {
        get => _pollSeconds ?? DefaultBuiltInPollSeconds;
        init => _pollSeconds = value;
    }

    /// <summary>A `codexbar serve` URL, e.g. http://127.0.0.1:8787/usage. null = built-in providers.</summary>
    public string? Endpoint { get; init; }

    public bool ShowNotch { get; init; } = true;

    /// <summary>false draws the concentric dial in the tray, true the session percent.</summary>
    public bool TrayPercent { get; init; }

    public bool Notify { get; init; } = true;

    /// <summary>null = automatic: every provider whose credential file exists, like install.sh's enable_if.</summary>
    public IReadOnlyList<string>? Providers { get; init; }

    public static Settings Default { get; } = new();

    /// <summary>
    /// We hit the vendors directly in built-in mode, so behave like `codexbar serve`'s
    /// own refresh interval (60 s); reading a local serve is cheap, so 30 s there.
    /// </summary>
    public int EffectivePollSeconds
    {
        get
        {
            int raw = _pollSeconds ?? (IsEndpointMode ? DefaultEndpointPollSeconds : DefaultBuiltInPollSeconds);
            return Math.Clamp(raw, MinPollSeconds, MaxPollSeconds);
        }
    }

    public bool IsEndpointMode => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>True when the user has never pinned an interval, so the mode default applies.</summary>
    internal bool PollSecondsIsExplicit => _pollSeconds.HasValue;
}
