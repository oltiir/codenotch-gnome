using System.Globalization;
using Codenotch.Core.Model;

namespace Codenotch.Core.Sources.Codex;

/// <summary>
/// Turns /backend-api/wham/usage into the CodexBar wire shape. The only real work
/// is the timestamps: Codex sends epoch seconds where CodexBar's JSON carries an
/// ISO string, and the presentation layer parses strings.
/// </summary>
public static class CodexUsageMapper
{
    public const int DefaultPrimaryWindowMinutes = 300;
    public const int DefaultSecondaryWindowMinutes = 10080;

    public static ProviderEntry ToEntry(CodexUsageResponse usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        CodexRateLimitDto? limits = usage.RateLimit;
        var dto = new UsageDto
        {
            Primary = Window(limits?.PrimaryWindow, DefaultPrimaryWindowMinutes),
            Secondary = Window(limits?.SecondaryWindow, DefaultSecondaryWindowMinutes),
        };

        // additional_rate_limits are skipped in v1: the notch draws two rings, and
        // a per-feature Spark cap is not what the session dial is about.

        return new ProviderEntry
        {
            Provider = UsageSourceIds.Codex,
            Source = "codex-oauth",
            Account = usage.AccountId,
            Usage = dto,
        };
    }

    private static RateWindowDto? Window(CodexWindowDto? window, int defaultWindowMinutes)
    {
        if (window?.UsedPercent is not double used)
            return null;

        // Clamped: a bogus limit_window_seconds must not wrap the cast into a
        // negative window length that the pace projection would then read as real.
        int windowMinutes = window.LimitWindowSeconds is long seconds && seconds >= 60
            ? (int)Math.Min(seconds / 60, int.MaxValue)
            : defaultWindowMinutes;

        return new RateWindowDto
        {
            UsedPercent = used,
            WindowMinutes = windowMinutes,
            ResetsAt = IsoFromEpochSeconds(window.ResetAt),
        };
    }

    /// <summary>0 or absent means unknown, which is a null reset line rather than 1970.</summary>
    internal static string? IsoFromEpochSeconds(long? epochSeconds)
    {
        if (epochSeconds is not long seconds || seconds <= 0)
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
