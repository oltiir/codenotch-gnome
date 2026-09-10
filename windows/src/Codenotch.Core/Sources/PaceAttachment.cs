using Codenotch.Core.Model;
using Codenotch.Core.Pace;

namespace Codenotch.Core.Sources;

/// <summary>
/// `codexbar serve` ships a pace line with every entry; the built-in sources
/// compute the same thing so endpoint mode and built-in mode look identical. The
/// UI renders nothing when it comes back null, so a window we cannot pace simply
/// has no third line.
/// </summary>
internal static class PaceAttachment
{
    public static void Attach(ProviderEntry entry, DateTimeOffset now,
                              int sessionWindowMinutes, int weeklyWindowMinutes)
    {
        UsageDto? usage = entry.Usage;
        if (usage is null)
            return;

        PaceEntryDto? primary = usage.Primary is null ? null
            : PaceCalculator.For(usage.Primary, now, sessionWindowMinutes);
        PaceEntryDto? secondary = usage.Secondary is null ? null
            : PaceCalculator.For(usage.Secondary, now, weeklyWindowMinutes);

        if (primary is null && secondary is null)
            return;

        entry.Pace = new PaceDto { Primary = primary, Secondary = secondary };
    }
}
