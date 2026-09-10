using System.Globalization;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;

namespace Codenotch.Core.Pace;

/// <summary>
/// The pace line CodexBar puts under each provider — "27% in reserve | Expected
/// 39% used | Lasts until reset". `codexbar serve` computes it for us in endpoint
/// mode; in built-in mode we compute the same thing, because the two numbers it
/// needs (the window length and its reset instant) are both on the wire already.
///
/// Everything here is a straight-line projection: no history is kept, so a burst
/// followed by an idle hour reads as "in reserve" the moment the average catches
/// down. That is what CodexBar does too.
/// </summary>
public static class PaceCalculator
{
    /// <summary>
    /// CodexBar's paceMinimumExpectedPercent. Below this the window has barely
    /// started and the projection is noise, so we say nothing at all.
    /// </summary>
    public const double MinimumExpectedPercent = 3;

    private const double OnTrackDelta = 2;
    private const double SlightDelta = 6;
    private const double ClearDelta = 12;

    /// <summary>
    /// Null whenever the projection would be meaningless: no reset instant, no
    /// window length, a reset already past or further out than the whole window
    /// (which means the timestamps disagree), a full window, or a window that has
    /// only just opened.
    /// </summary>
    public static PaceEntryDto? For(RateWindowDto window, DateTimeOffset now, int defaultWindowMinutes)
    {
        if (window is null)
            return null;
        if (window.UsedPercent is not double usedRaw)
            return null;
        if (!UsageText.TryParseIso(window.ResetsAt, out DateTimeOffset resetsAt))
            return null;

        int windowMinutes = window.WindowMinutes is int wm && wm > 0 ? wm : defaultWindowMinutes;
        if (windowMinutes <= 0)
            return null;

        double used = Math.Clamp(usedRaw, 0, 100);
        double remaining = 100 - used;
        if (remaining <= 0)
            return null;                                  // already empty; nothing to pace

        TimeSpan duration = TimeSpan.FromMinutes(windowMinutes);
        TimeSpan untilReset = resetsAt - now;
        if (untilReset <= TimeSpan.Zero || untilReset > duration)
            return null;

        TimeSpan elapsed = duration - untilReset;
        if (elapsed <= TimeSpan.Zero)
            return null;                                  // window just opened: no rate yet

        double expected = Math.Clamp(elapsed.TotalSeconds / duration.TotalSeconds * 100.0, 0, 100);
        if (expected < MinimumExpectedPercent)
            return null;

        double delta = used - expected;
        double magnitude = Math.Abs(delta);
        bool ahead = delta >= 0;                          // burning faster than the clock
        string stage = magnitude <= OnTrackDelta ? "onTrack"
            : magnitude <= SlightDelta ? (ahead ? "slightlyAhead" : "slightlyBehind")
            : magnitude <= ClearDelta ? (ahead ? "ahead" : "behind")
            : (ahead ? "farAhead" : "farBehind");

        // Straight-line burn: at this average, when does the window hit 100?
        double burnPerSecond = used / elapsed.TotalSeconds;
        double? etaSeconds = burnPerSecond > 0 ? remaining / burnPerSecond : null;
        bool willLastToReset = etaSeconds is null || etaSeconds > untilReset.TotalSeconds;

        bool isSessionWindow = defaultWindowMinutes <= 1440;
        string summary = string.Join(" | ", [
            LeftLabel(stage, magnitude, ahead),
            $"Expected {Round(expected)}% used",
            RightLabel(willLastToReset, etaSeconds, now, isSessionWindow),
        ]);

        return new PaceEntryDto
        {
            Stage = stage,
            DeltaPercent = delta,
            ExpectedUsedPercent = expected,
            WillLastToReset = willLastToReset,
            EtaSeconds = etaSeconds,
            Summary = summary,
        };
    }

    private static string LeftLabel(string stage, double magnitude, bool ahead)
    {
        if (stage == "onTrack")
            return "On pace";
        // "in deficit" reads as "you have spent more than the clock says"; "in
        // reserve" is the happy direction. CodexBar's wording, kept verbatim.
        return ahead ? $"{Round(magnitude)}% in deficit" : $"{Round(magnitude)}% in reserve";
    }

    private static string RightLabel(bool willLast, double? etaSeconds, DateTimeOffset now, bool isSessionWindow)
    {
        if (willLast)
            return "Lasts until reset";

        string verb = isSessionWindow ? "Projected empty" : "Runs out";
        if (etaSeconds is not double eta || eta <= 0)
            return isSessionWindow ? "Projected empty now" : "Runs out now";

        string cd = UsageText.Countdown(now.AddSeconds(eta).ToString("o", CultureInfo.InvariantCulture), now);
        if (cd.Length == 0 || cd == "now")
            return isSessionWindow ? "Projected empty now" : "Runs out now";
        return $"{verb} in {cd}";
    }

    private static long Round(double value) => Num.JsRound(value);
}
