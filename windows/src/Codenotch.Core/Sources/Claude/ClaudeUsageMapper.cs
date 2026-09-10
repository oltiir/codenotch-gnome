using System.Text;
using Codenotch.Core.Model;

namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// Turns /api/oauth/usage into the CodexBar wire shape, so one presentation path
/// serves the built-in fetch and a `codexbar serve` endpoint alike.
///
/// Ruling I.1: match what `codexbar serve` actually emits. The newer limits[]
/// array becomes extraRateWindows with CodexBar's exact ids and titles; only when
/// limits[] is absent or yields nothing do the flat seven_day_&lt;model&gt; keys
/// land in usage.tertiary (which the UI labels "Model", like the GNOME port).
/// Never both.
/// </summary>
public static class ClaudeUsageMapper
{
    public const int SessionWindowMinutes = 300;      // five_hour
    public const int WeeklyWindowMinutes = 10080;     // seven_day
    public const string ScopedIdPrefix = "claude-weekly-scoped-";

    public static ProviderEntry ToEntry(ClaudeUsageResponse usage, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var dto = new UsageDto
        {
            Primary = Window(usage.FiveHour, SessionWindowMinutes),
            Secondary = Window(usage.SevenDay, WeeklyWindowMinutes),
            UpdatedAt = now.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        };

        List<NamedRateWindowDto> scoped = ScopedWeekly(usage.Limits);
        if (scoped.Count > 0)
        {
            dto.ExtraRateWindows = scoped;
        }
        else
        {
            // CodexBar's mapOAuthUsage prefers sonnet over opus for this slot.
            dto.Tertiary = Window(usage.SevenDaySonnet ?? usage.SevenDayOpus, WeeklyWindowMinutes);
        }

        // seven_day_oauth_apps and seven_day_routines are ignored in v1: they are
        // not limits the editor spends against, and the notch has three rings, not five.

        var entry = new ProviderEntry
        {
            Provider = UsageSourceIds.Claude,
            Source = "claude-oauth",
            Usage = dto,
        };
        PaceAttachment.Attach(entry, now, SessionWindowMinutes, WeeklyWindowMinutes);
        return entry;
    }

    private static RateWindowDto? Window(ClaudeUsageWindowDto? window, int windowMinutes)
    {
        if (window?.Utilization is not double used)
            return null;                              // a window with no number does not exist
        return new RateWindowDto
        {
            UsedPercent = used,
            WindowMinutes = windowMinutes,
            ResetsAt = window.ResetsAt,
        };
    }

    private static List<NamedRateWindowDto> ScopedWeekly(List<ClaudeLimitEntryDto>? limits)
    {
        var out_ = new List<NamedRateWindowDto>();
        if (limits is null)
            return out_;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClaudeLimitEntryDto limit in limits)
        {
            if (limit is null)
                continue;
            if (!string.Equals(limit.Group, "weekly", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(limit.Kind, "weekly_scoped", StringComparison.OrdinalIgnoreCase))
                continue;
            if (limit.Percent is not double percent)
                continue;

            ClaudeLimitScopeModelDto? model = limit.Scope?.Model;
            string? displayName = model?.DisplayName?.Trim();
            if (string.IsNullOrEmpty(displayName))
                continue;
            // The all-models scope is already usage.secondary ("All models"); a
            // second identical row would just be noise.
            if (IsAllModels(model))
                continue;

            string id = ScopedIdPrefix + Slug(string.IsNullOrWhiteSpace(model?.Id) ? displayName : model!.Id!);
            if (!seen.Add(id))
                continue;                             // the API repeats a scope across active/inactive rows

            out_.Add(new NamedRateWindowDto
            {
                Id = id,
                // CodexBar titles these "Fable only"; UsageNormalizer strips the suffix.
                Title = $"{displayName} only",
                Window = new RateWindowDto
                {
                    UsedPercent = percent,
                    WindowMinutes = WeeklyWindowMinutes,
                    ResetsAt = limit.ResetsAt,
                },
            });
        }
        return out_;
    }

    private static bool IsAllModels(ClaudeLimitScopeModelDto? model)
    {
        if (model is null)
            return false;
        if (string.Equals(model.Id, "all", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(model.DisplayName?.Trim(), "all models", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lowercase, non-alphanumerics collapsed to a single '-', trimmed.</summary>
    internal static string Slug(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var sb = new StringBuilder(value.Length);
        bool pendingDash = false;
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0)
                    sb.Append('-');
                pendingDash = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingDash = true;
            }
        }
        return sb.ToString();
    }
}
