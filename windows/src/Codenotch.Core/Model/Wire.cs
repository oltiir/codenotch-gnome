using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codenotch.Core.Model;

/// <summary>
/// One options object for every parse in this library. Everything is lenient on
/// purpose: `codexbar serve` and the vendor APIs both add keys over time, and a
/// tray icon that stops reporting because a new field appeared is worse than one
/// that ignores it.
/// </summary>
public static class CodexBarJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // UnmappedMemberHandling defaults to Skip: unknown fields are ignored.
    };

    /// <summary>The same leniency for JsonNode/JsonDocument parsing.</summary>
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

// The keys below are exactly the ones in waybar/tests/fixtures/live.json. Every
// numeric field is nullable and every object optional, because CodexBar emits
// `"tertiary": null` and omits whole branches for providers that failed.

public sealed class ProviderEntry
{
    public string? Provider { get; set; }
    public string? Source { get; set; }
    public string? Account { get; set; }
    public UsageDto? Usage { get; set; }
    public PaceDto? Pace { get; set; }
    public ErrorDto? Error { get; set; }
}

public sealed class UsageDto
{
    public RateWindowDto? Primary { get; set; }
    public RateWindowDto? Secondary { get; set; }
    public RateWindowDto? Tertiary { get; set; }
    [JsonPropertyName("extraRateWindows")] public List<NamedRateWindowDto>? ExtraRateWindows { get; set; }
    public string? UpdatedAt { get; set; }
    public string? DataConfidence { get; set; }
}

public sealed class RateWindowDto
{
    [JsonPropertyName("usedPercent")] public double? UsedPercent { get; set; }
    [JsonPropertyName("windowMinutes")] public int? WindowMinutes { get; set; }
    [JsonPropertyName("resetsAt")] public string? ResetsAt { get; set; }
    [JsonPropertyName("resetDescription")] public string? ResetDescription { get; set; }
}

public sealed class NamedRateWindowDto
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public RateWindowDto? Window { get; set; }
}

public sealed class PaceDto
{
    public PaceEntryDto? Primary { get; set; }
    public PaceEntryDto? Secondary { get; set; }
}

public sealed class PaceEntryDto
{
    public string? Stage { get; set; }
    [JsonPropertyName("deltaPercent")] public double? DeltaPercent { get; set; }
    [JsonPropertyName("expectedUsedPercent")] public double? ExpectedUsedPercent { get; set; }
    [JsonPropertyName("willLastToReset")] public bool? WillLastToReset { get; set; }
    [JsonPropertyName("etaSeconds")] public double? EtaSeconds { get; set; }
    [JsonPropertyName("runOutProbability")] public double? RunOutProbability { get; set; }
    public string? Summary { get; set; }
}

public sealed class ErrorDto
{
    public string? Message { get; set; }
    public string? Kind { get; set; }
    public int? Code { get; set; }
}
