using System.Text.Json.Serialization;

namespace Codenotch.Core.Sources.Codex;

/// <summary>
/// One window from GET /backend-api/wham/usage. Unlike Claude's endpoint this one
/// does carry the window length, and its reset instant is epoch SECONDS.
/// </summary>
public sealed class CodexWindowDto
{
    [JsonPropertyName("used_percent")] public double? UsedPercent { get; set; }

    /// <summary>Epoch seconds. 0 means "unknown", not 1970.</summary>
    [JsonPropertyName("reset_at")] public long? ResetAt { get; set; }

    [JsonPropertyName("limit_window_seconds")] public long? LimitWindowSeconds { get; set; }
}

public sealed class CodexRateLimitDto
{
    [JsonPropertyName("primary_window")] public CodexWindowDto? PrimaryWindow { get; set; }
    [JsonPropertyName("secondary_window")] public CodexWindowDto? SecondaryWindow { get; set; }
}

public sealed class CodexUsageResponse
{
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("plan_type")] public string? PlanType { get; set; }
    [JsonPropertyName("rate_limit")] public CodexRateLimitDto? RateLimit { get; set; }

    /// <summary>Per-feature caps (Codex Spark and friends). Skipped in v1.</summary>
    [JsonPropertyName("additional_rate_limits")] public List<CodexAdditionalRateLimitDto>? AdditionalRateLimits { get; set; }

    public CodexCreditsDto? Credits { get; set; }
}

public sealed class CodexAdditionalRateLimitDto
{
    [JsonPropertyName("limit_name")] public string? LimitName { get; set; }
    [JsonPropertyName("metered_feature")] public string? MeteredFeature { get; set; }
    [JsonPropertyName("rate_limit")] public CodexRateLimitDto? RateLimit { get; set; }
}

public sealed class CodexCreditsDto
{
    [JsonPropertyName("has_credits")] public bool? HasCredits { get; set; }
    public bool? Unlimited { get; set; }
    public double? Balance { get; set; }
}
