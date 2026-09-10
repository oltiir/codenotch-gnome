using System.Globalization;

namespace Codenotch.Core.Presentation;

/// <summary>
/// The reset strings, ported from countdown()/clockText()/resetLine() in
/// extension.js. Every one takes an explicit `now` (and time zone) so the tests
/// can pin an instant instead of racing the wall clock.
/// </summary>
public static class UsageText
{
    /// <summary>
    /// "" if iso is null/empty/unparsable, "now" once the instant has passed.
    /// claude.ai's phrasing: "59 min", "13 hr 49 min", "2 days 3 hr".
    /// </summary>
    public static string Countdown(string? iso, DateTimeOffset now)
    {
        if (!TryParseIso(iso, out DateTimeOffset target))
            return string.Empty;

        double ms = (target - now).TotalMilliseconds;
        if (ms <= 0)
            return "now";

        // Ruling I.5: floor at one minute, agreeing with cosmic and waybar. The
        // GNOME port renders "0 min" for the last half minute and is left as is.
        long mins = Math.Max(1, Model.Num.JsRound(ms / 60000.0));
        if (mins < 60)
            return $"{mins} min";

        long hours = mins / 60;
        if (hours < 24)
        {
            long rest = mins % 60;
            return rest != 0 ? $"{hours} hr {rest} min" : $"{hours} hr";
        }

        long days = hours / 24;
        long remHours = hours % 24;
        string dayWord = days == 1 ? "day" : "days";
        return remHours != 0 ? $"{days} {dayWord} {remHours} hr" : $"{days} {dayWord}";
    }

    /// <summary>"17:10" for the same local day, "Tue 06:00" otherwise. "" if unparsable.</summary>
    public static string ClockText(string? iso, DateTimeOffset now, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        if (!TryParseIso(iso, out DateTimeOffset target))
            return string.Empty;

        DateTimeOffset local = TimeZoneInfo.ConvertTime(target, tz);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, tz);
        bool sameDay = local.Date == localNow.Date;
        // Invariant culture so "Tue 06:00" is stable whatever the machine locale is.
        return local.ToString(sameDay ? "HH:mm" : "ddd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string ClockText(string? iso, DateTimeOffset now)
        => ClockText(iso, now, TimeZoneInfo.Local);

    /// <summary>
    /// "Resets in 59 min  ·  17:10". The separator is two spaces, U+00B7, two
    /// spaces — the same literal as extension.js and cosmic's reset_line().
    /// </summary>
    public static string ResetLine(string? iso, DateTimeOffset now, TimeZoneInfo tz)
    {
        string cd = Countdown(iso, now);
        if (cd.Length == 0)
            return string.Empty;
        if (cd == "now")
            return "Resetting";
        string clock = ClockText(iso, now, tz);
        return clock.Length != 0 ? $"Resets in {cd}  ·  {clock}" : $"Resets in {cd}";
    }

    public static string ResetLine(string? iso, DateTimeOffset now)
        => ResetLine(iso, now, TimeZoneInfo.Local);

    /// <summary>
    /// Accepts the shapes CodexBar and the vendor APIs actually emit:
    /// "2026-09-07T20:10:00Z", "...T20:10:00.000Z", "...+02:00". AssumeUniversal
    /// covers a stray timestamp with no zone rather than reading it as local.
    /// </summary>
    internal static bool TryParseIso(string? iso, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(iso))
            return false;
        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out value);
    }
}
