using System;
using System.Collections.Generic;

namespace DeathPace.Engine;

/// <summary>Learned marker→exit bests, keyed (segment, marker, variant).
/// Pure data — no LiveSplit or WRAM types.</summary>
public sealed class BestsStore
{
    private readonly Dictionary<MarkerKey, BestEntry> entries = new();

    public IReadOnlyCollection<MarkerKey> Keys => entries.Keys;

    public void Record(int segmentIndex, int markerIndex, Variant variant, TimeSpan duration)
    {
        var key = new MarkerKey(segmentIndex, markerIndex, variant);
        long ms = (long)duration.TotalMilliseconds;
        entries[key] = entries.TryGetValue(key, out var prior)
            ? new BestEntry(Math.Min(prior.BestMs, ms), prior.Attempts + 1)
            : new BestEntry(ms, 1);
    }

    public bool TryGetBest(int segmentIndex, int markerIndex, Variant variant, out TimeSpan best)
    {
        if (entries.TryGetValue(new MarkerKey(segmentIndex, markerIndex, variant), out var entry))
        {
            best = TimeSpan.FromMilliseconds(entry.BestMs);
            return true;
        }
        best = default;
        return false;
    }

    public int GetAttempts(int segmentIndex, int markerIndex, Variant variant) =>
        entries.TryGetValue(new MarkerKey(segmentIndex, markerIndex, variant), out var entry) ? entry.Attempts : 0;

    public bool TryGetEntry(MarkerKey key, out BestEntry entry) => entries.TryGetValue(key, out entry);

    public void SetEntry(MarkerKey key, BestEntry entry) => entries[key] = entry;

    public void RemoveEntry(MarkerKey key) => entries.Remove(key);
}
