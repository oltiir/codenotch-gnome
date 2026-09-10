namespace Codenotch.Core.Model;

/// <summary>
/// One provider after normalization: the identity the UI draws, its windows in
/// display order, and whatever went wrong instead.
/// </summary>
public sealed record ProviderReading(
    ProviderIdentity Provider,
    IReadOnlyList<UsageWindow> Windows,
    string? Error,
    string? PaceSummary)
{
    /// <summary>
    /// The window the dial's outer ring shows. Falls back to the first window so a
    /// provider that only reports a weekly cap still draws something — the same
    /// `windows.find(...) ?? windows[0]` fallback extension.js uses.
    /// </summary>
    public UsageWindow? Session
    {
        get
        {
            foreach (UsageWindow w in Windows)
            {
                if (w.Key == "session")
                    return w;
            }
            return Windows.Count > 0 ? Windows[0] : null;
        }
    }

    /// <summary>The inner ring. Null for a provider with no all-models weekly cap.</summary>
    public UsageWindow? Weekly
    {
        get
        {
            foreach (UsageWindow w in Windows)
            {
                if (w.Key == "weekly")
                    return w;
            }
            return null;
        }
    }

    /// <summary>extension.js: `entry.error || windows.length === 0` is the "no data" state.</summary>
    public bool HasData => Error is null && Windows.Count > 0;
}
