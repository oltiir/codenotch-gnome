using System;
using System.Collections.Generic;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;

namespace Codenotch.App;

/// <summary>
/// One toast per window per crossing. The stored level only ever moves up while
/// usage climbs; it resets when usage drops back below the level's own bound, so a
/// window reset re-arms the warning instead of going quiet forever.
/// </summary>
internal sealed class ThresholdNotifier
{
    private enum Level
    {
        None = 0,
        Warn = 1,
        Critical = 2,
    }

    private readonly Dictionary<(string Provider, string Window), Level> _crossed = new();

    /// <summary>
    /// Returns the messages to show for this snapshot, in reading order. Empty when
    /// nothing crossed — which is the normal case, so this is cheap.
    /// </summary>
    public IReadOnlyList<string> Evaluate(IReadOnlyList<ProviderReading> readings,
                                          DateTimeOffset now, TimeZoneInfo zone, bool enabled)
    {
        var messages = new List<string>();
        var seen = new HashSet<(string, string)>();

        foreach (ProviderReading reading in readings)
        {
            if (!reading.HasData)
                continue;
            foreach (UsageWindow window in reading.Windows)
            {
                var key = (reading.Provider.Id, window.Key);
                seen.Add(key);

                Level level = LevelFor(window.Used);
                _crossed.TryGetValue(key, out Level previous);

                if (level > previous)
                {
                    _crossed[key] = level;
                    if (enabled)
                        messages.Add(Message(reading.Provider, window, now, zone));
                }
                else if (level < previous)
                {
                    // Dropped below the band we last announced: re-arm.
                    _crossed[key] = level;
                }
            }
        }

        // Forget windows that have gone away, so a provider disappearing and coming
        // back does not carry a stale level. Counts can match while the keys differ,
        // so this always walks the dictionary rather than short-circuiting on size.
        var stale = new List<(string, string)>();
        foreach (var key in _crossed.Keys)
        {
            if (!seen.Contains(key))
                stale.Add(key);
        }
        foreach (var key in stale)
            _crossed.Remove(key);

        return messages;
    }

    public void Reset() => _crossed.Clear();

    private static Level LevelFor(int used)
    {
        if (used >= Thresholds.CriticalAt)
            return Level.Critical;
        if (used >= Thresholds.WarnAt)
            return Level.Warn;
        return Level.None;
    }

    private static string Message(ProviderIdentity provider, UsageWindow window,
                                  DateTimeOffset now, TimeZoneInfo zone)
    {
        string countdown = UsageText.Countdown(window.ResetsAt, now);
        string tail = countdown.Length switch
        {
            0 => string.Empty,
            _ => countdown == "now" ? ", resetting now" : $", resets in {countdown}",
        };
        return $"{provider.Name} — {window.Label} at {window.Used}% used{tail}";
    }
}
