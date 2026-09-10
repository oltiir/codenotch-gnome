using System.Text.RegularExpressions;

namespace Codenotch.Core.Model;

/// <summary>
/// Port of windowsFrom() and clampPct() in extension.js. This is the one place
/// the CodexBar wire shape turns into what the UI draws, so the built-in
/// providers and a `codexbar serve` endpoint render identically.
/// </summary>
public static partial class UsageNormalizer
{
    /// <summary>
    /// CodexBar titles the scoped weekly windows "Fable only"; claude.ai just
    /// says "Fable". Anchored on whitespace + end, so "Lonely" and a bare "Only"
    /// keep their text — /\s+only$/i in extension.js.
    /// </summary>
    [GeneratedRegex(@"\s+only$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnlySuffix();

    public static IReadOnlyList<UsageWindow> WindowsFrom(ProviderEntry? entry)
    {
        UsageDto? usage = entry?.Usage;
        if (usage is null)
            return [];

        var out_ = new List<UsageWindow>(4);
        Push(out_, "session", WindowGroup.Session, "Current session", usage.Primary);
        Push(out_, "weekly", WindowGroup.Weekly, "All models", usage.Secondary);
        Push(out_, "tertiary", WindowGroup.Weekly, "Model", usage.Tertiary);

        foreach (NamedRateWindowDto extra in usage.ExtraRateWindows ?? [])
        {
            if (extra is null)
                continue;
            string label = OnlySuffix().Replace(extra.Title ?? "Scoped", string.Empty);
            Push(out_, extra.Id ?? "extra", WindowGroup.Weekly, label, extra.Window);
        }
        return out_;
    }

    /// <summary>
    /// A window only exists if it carries a number. CodexBar sends `"tertiary": null`
    /// and, for providers it could only partly read, windows with no usedPercent.
    /// </summary>
    private static void Push(List<UsageWindow> into, string key, WindowGroup group, string label, RateWindowDto? window)
    {
        if (window?.UsedPercent is not double used)
            return;
        into.Add(new UsageWindow(key, group, label, ClampPct(used), window.ResetsAt));
    }

    public static ProviderReading ReadingFrom(ProviderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        IReadOnlyList<UsageWindow> windows = WindowsFrom(entry);
        string? error = entry.Error?.Message;
        if (error is null && windows.Count == 0)
            error = "no data";      // cosmic's fallback when a provider answered with nothing
        return new ProviderReading(ProviderIdentity.For(entry.Provider), windows, error,
                                   entry.Pace?.Primary?.Summary);
    }

    public static IReadOnlyList<ProviderReading> ReadingsFrom(IEnumerable<ProviderEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var out_ = new List<ProviderReading>();
        foreach (ProviderEntry entry in entries)
        {
            if (entry is not null)
                out_.Add(ReadingFrom(entry));
        }
        return out_;
    }

    /// <summary>Math.max(0, Math.min(100, Math.round(n))) with JS rounding.</summary>
    public static int ClampPct(double n)
    {
        // Clamp before rounding so an out-of-range or non-finite percent cannot
        // overflow the cast; the result is identical for anything sane.
        if (double.IsNaN(n) || n <= 0)
            return 0;
        if (n >= 100)
            return 100;
        return (int)Num.JsRound(n);
    }
}
