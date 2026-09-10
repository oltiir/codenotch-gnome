using Codenotch.Core.Model;

namespace Codenotch.Core.Presentation;

/// <summary>The provider the tray icon and the panel label speak for.</summary>
public sealed record WorstReading(ProviderIdentity Provider, int Used, int? Weekly, string? ResetsAt)
{
    public ToneKind Tone => Presentation.Tone.Of(Used);
}

public static class Worst
{
    /// <summary>
    /// Highest *session* percent across providers. Replaces only on strictly
    /// greater (extension.js line 518), so a tie keeps the earlier provider and
    /// the tray icon does not flicker between two equal readings.
    /// </summary>
    public static WorstReading? Of(IEnumerable<ProviderReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        WorstReading? worst = null;
        foreach (ProviderReading reading in readings)
        {
            if (reading is null || !reading.HasData)
                continue;
            if (reading.Session is not UsageWindow session)
                continue;
            if (worst is null || session.Used > worst.Used)
                worst = new WorstReading(reading.Provider, session.Used, reading.Weekly?.Used, session.ResetsAt);
        }
        return worst;
    }
}
