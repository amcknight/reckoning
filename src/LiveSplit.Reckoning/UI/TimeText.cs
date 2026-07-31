using System;

namespace LiveSplit.Reckoning.UI;

internal enum RowAccuracy
{
    Seconds,
    Tenths,
    Hundredths,
}

/// <summary>Deterministic time formatting for the two rows. Local rather than
/// LiveSplit's TimeFormatters so the exact strings are unit-pinned.</summary>
internal static class TimeText
{
    private const string NoValue = "—";   // em dash: data unavailable

    public static string Format(TimeSpan? time, RowAccuracy accuracy)
    {
        if (time is not TimeSpan t) return NoValue;
        string frac = accuracy switch
        {
            RowAccuracy.Tenths => "." + (t.Milliseconds / 100),
            RowAccuracy.Hundredths => "." + (t.Milliseconds / 10).ToString("00"),
            _ => "",
        };
        long totalMinutes = (long)t.TotalMinutes;
        return t.TotalHours >= 1
            ? $"{(long)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}{frac}"
            : $"{totalMinutes}:{t.Seconds:00}{frac}";
    }

    public static string FormatSunk(TimeSpan? sunk, RowAccuracy accuracy)
    {
        if (sunk is not TimeSpan s) return NoValue;
        string body = Format(s.Duration(), accuracy);
        return s > TimeSpan.Zero ? "+" + body : s < TimeSpan.Zero ? "-" + body : body;
    }

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
