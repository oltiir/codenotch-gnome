namespace Codenotch.Core.Presentation;

public enum ToneKind
{
    Ok,
    Warn,
    Critical,
    Stale,
}

/// <summary>Percent *used* at which the colour turns. Mirrors claude.ai's usage panel.</summary>
public static class Thresholds
{
    public const int WarnAt = 70;
    public const int CriticalAt = 90;
}

public static class Tone
{
    public static ToneKind Of(int used)
    {
        if (used >= Thresholds.CriticalAt)
            return ToneKind.Critical;
        if (used >= Thresholds.WarnAt)
            return ToneKind.Warn;
        return ToneKind.Ok;
    }

    /// <summary>No reading at all is Stale — the grey dial, not a green one.</summary>
    public static ToneKind Of(int? used) => used is int u ? Of(u) : ToneKind.Stale;
}

/// <summary>
/// The dark tones are the GNOME stylesheet's exact values, so the three ports
/// look like one product. The light ones are darker equivalents, because
/// #57e389 on a white Fluent surface is unreadable.
/// </summary>
public static class ToneColors
{
    public const string DarkOk = "#57E389";
    public const string DarkWarn = "#F8E45C";
    public const string DarkCritical = "#FF7B63";
    public const string DarkStale = "#9A9996";

    public const string LightOk = "#1A7F37";
    public const string LightWarn = "#9A6700";
    public const string LightCritical = "#CF222E";
    public const string LightStale = "#6E7781";

    /// <summary>ARGB for rgba(255,255,255,.12) — the ring/bar track in the stylesheet.</summary>
    public const string DarkTrack = "#1FFFFFFF";

    /// <summary>ARGB for rgba(0,0,0,.10).</summary>
    public const string LightTrack = "#1A000000";

    public static string Hex(ToneKind tone, bool dark) => tone switch
    {
        ToneKind.Ok => dark ? DarkOk : LightOk,
        ToneKind.Warn => dark ? DarkWarn : LightWarn,
        ToneKind.Critical => dark ? DarkCritical : LightCritical,
        _ => dark ? DarkStale : LightStale,
    };

    public static string TrackHex(bool dark) => dark ? DarkTrack : LightTrack;
}
