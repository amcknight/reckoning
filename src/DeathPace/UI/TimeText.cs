using System;

namespace DeathPace.UI;

/// <summary>Deterministic time formatting for the damage-hit overlay. Local
/// rather than LiveSplit's TimeFormatters so the exact string is unit-pinned.</summary>
internal static class TimeText
{
    /// <summary>Damage-number format: always a leading minus (time lost), fixed
    /// tenths — one glanceable decimal, like an HP hit.</summary>
    public static string FormatHit(TimeSpan amount)
    {
        var t = amount.Duration();
        int tenths = t.Milliseconds / 100;
        return t.TotalSeconds < 60
            ? $"-{(long)t.TotalSeconds}.{tenths}"
            : $"-{(long)t.TotalMinutes}:{t.Seconds:00}.{tenths}";
    }
}
