using System.Text.Json.Serialization;

namespace Codenotch.Core.Sources.Claude;

/// <summary>
/// One window from GET /api/oauth/usage. Note there is no window length on the
/// wire: CodexBar synthesises 300 / 10080 minutes, and so do we (ruling I.3).
/// </summary>
public sealed class ClaudeUsageWindowDto
{
    /// <summary>Percent used, 0..100.</summary>
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }
}

/// <summary>
/// Keys verified against CodexBar's ClaudeOAuthUsageFetcher.swift. Everything is
/// optional: the API sends `null` for windows a plan does not have, and adds keys
/// (extra_usage, seven_day_routines) without warning.
/// </summary>
public sealed class ClaudeUsageResponse
{
    [JsonPropertyName("five_hour")] public ClaudeUsageWindowDto? FiveHour { get; set; }
    [JsonPropertyName("seven_day")] public ClaudeUsageWindowDto? SevenDay { get; set; }
    [JsonPropertyName("seven_day_opus")] public ClaudeUsageWindowDto? SevenDayOpus { get; set; }
    [JsonPropertyName("seven_day_sonnet")] public ClaudeUsageWindowDto? SevenDaySonnet { get; set; }
    [JsonPropertyName("seven_day_oauth_apps")] public ClaudeUsageWindowDto? SevenDayOAuthApps { get; set; }
    [JsonPropertyName("seven_day_routines")] public ClaudeUsageWindowDto? SevenDayRoutines { get; set; }

    /// <summary>Newer shape; supersedes the flat seven_day_&lt;model&gt; keys when present.</summary>
    public List<ClaudeLimitEntryDto>? Limits { get; set; }
}

public sealed class ClaudeLimitEntryDto
{
    /// <summary>"weekly_scoped" for the per-model weekly caps.</summary>
    public string? Kind { get; set; }

    public string? Group { get; set; }
    public double? Percent { get; set; }

    [JsonPropertyName("resets_at")] public string? ResetsAt { get; set; }

    public ClaudeLimitScopeDto? Scope { get; set; }

    /// <summary>
    /// Parsed but intentionally NOT used as a filter: an inactive scoped limit is
    /// still a limit the user is spending against, and CodexBar shows it too.
    /// </summary>
    [JsonPropertyName("is_active")] public bool? IsActive { get; set; }
}

public sealed class ClaudeLimitScopeDto
{
    public ClaudeLimitScopeModelDto? Model { get; set; }
}

public sealed class ClaudeLimitScopeModelDto
{
    public string? Id { get; set; }

    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
}
