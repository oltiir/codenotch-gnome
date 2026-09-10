namespace Codenotch.Core.Model;

/// <summary>
/// The PROVIDERS map from extension.js. Glyphs are single BMP characters so
/// Segoe UI Symbol can render them without an emoji font.
/// </summary>
public sealed record ProviderIdentity(string Id, string Name, string Glyph)
{
    public static ProviderIdentity Claude { get; } = new("claude", "Claude", "✱");    // ✱
    public static ProviderIdentity Codex { get; } = new("codex", "Codex", "◎");       // ◎
    public static ProviderIdentity Cursor { get; } = new("cursor", "Cursor", "△");    // △
    public static ProviderIdentity Copilot { get; } = new("copilot", "Copilot", "⌘"); // ⌘
    public static ProviderIdentity Gemini { get; } = new("gemini", "Gemini", "✦");    // ✦

    /// <summary>Display order, matching the PROVIDERS literal in extension.js.</summary>
    public static IReadOnlyList<ProviderIdentity> Known { get; } =
        [Claude, Codex, Cursor, Copilot, Gemini];

    /// <summary>
    /// Unknown ids get the extension.js treatment: the name is the id with its
    /// first letter capitalised, the glyph is that letter. (cosmic uses ● here;
    /// ruling I.7 keeps the first letter, so a new CodexBar provider still reads
    /// as itself rather than as an anonymous dot.)
    /// </summary>
    public static ProviderIdentity For(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new ProviderIdentity("unknown", "Unknown", "U");

        foreach (ProviderIdentity known in Known)
        {
            if (string.Equals(known.Id, id, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        string first = char.ToUpperInvariant(id[0]).ToString();
        return new ProviderIdentity(id, first + id[1..], first);
    }
}
