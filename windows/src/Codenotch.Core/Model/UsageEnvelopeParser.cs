using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codenotch.Core.Model;

/// <summary>
/// Port of providersFrom() in extension.js. `codexbar serve` mirrors the CLI's
/// JSON, which is a single object for one provider and a collection for several,
/// and the collection has been spelled four different ways across versions. Be
/// liberal about the envelope.
/// </summary>
public static class UsageEnvelopeParser
{
    internal static readonly string[] EnvelopeKeys = ["providers", "usages", "results", "items"];

    /// <summary>
    /// Throws <see cref="JsonException"/> on malformed JSON — the caller turns that
    /// into a visible "can't read the source" state. Valid-but-unrecognised JSON
    /// returns an empty list, which the UI shows as "No providers enabled".
    /// </summary>
    public static IReadOnlyList<ProviderEntry> ProvidersFrom(string json)
        => ProvidersFrom(JsonNode.Parse(json, documentOptions: CodexBarJson.DocumentOptions));

    public static IReadOnlyList<ProviderEntry> ProvidersFrom(JsonNode? node)
    {
        if (node is JsonArray array)
            return FromArray(array);

        if (node is JsonObject obj)
        {
            foreach (string key in EnvelopeKeys)
            {
                if (obj.TryGetPropertyValue(key, out JsonNode? value) && value is JsonArray wrapped)
                    return FromArray(wrapped);
            }
            // A single-provider payload. extension.js keys off `usage` or `provider`.
            if (HasValue(obj, "usage") || HasValue(obj, "provider"))
            {
                ProviderEntry? single = Bind(obj);
                return single is null ? [] : [single];
            }
        }

        return [];
    }

    private static bool HasValue(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out JsonNode? value) && value is not null;

    private static List<ProviderEntry> FromArray(JsonArray array)
    {
        var out_ = new List<ProviderEntry>(array.Count);
        foreach (JsonNode? element in array)
        {
            // cosmic's `let Ok(raw) = ... else { continue }`: one junk element must
            // not cost us the providers that parsed fine.
            ProviderEntry? entry = Bind(element);
            if (entry is not null)
                out_.Add(entry);
        }
        return out_;
    }

    private static ProviderEntry? Bind(JsonNode? node)
    {
        if (node is null)
            return null;
        try
        {
            return node.Deserialize<ProviderEntry>(CodexBarJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
