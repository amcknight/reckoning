using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Ordered-progress-marker state for the segment in flight.
/// Markers are identified by order-within-segment (marker 0 = segment start);
/// observations open on arrival at a (marker, variant) situation and complete
/// only when the caller reports a real split.</summary>
public sealed class SegmentTracker
{
    // Open observations: (marker, variant) -> run-elapsed at arrival.
    // Re-arrival overwrites: the later arrival is the shorter achieved span,
    // which is the only one a min-merge could keep.
    private readonly Dictionary<(int Marker, Variant Variant), TimeSpan> open = new();

    public int CurrentMarker { get; private set; }
    public Variant CurrentVariant { get; private set; }
    public bool DiedThisSegment { get; private set; }
    public bool IsActive { get; private set; }

    public void StartSegment(TimeSpan elapsed)
    {
        open.Clear();
        CurrentMarker = 0;
        CurrentVariant = Variant.Hot;
        DiedThisSegment = false;
        IsActive = true;
        open[(0, Variant.Hot)] = elapsed;
    }

    public void OnCheckpoint(TimeSpan elapsed)
    {
        if (!IsActive) return;
        CurrentMarker++;
        CurrentVariant = Variant.Hot;
        open[(CurrentMarker, Variant.Hot)] = elapsed;
    }

    public void OnDeath()
    {
        if (!IsActive) return;
        DiedThisSegment = true;
        // Spec: on death the runner is assumed to respawn at the last touched
        // marker — the situation is cold from this moment even before respawn.
        CurrentVariant = Variant.Cold;
    }

    public void OnRespawn(TimeSpan elapsed)
    {
        if (!IsActive) return;
        CurrentVariant = Variant.Cold;
        open[(CurrentMarker, Variant.Cold)] = elapsed;
    }

    public IReadOnlyList<Observation> CompleteSegment(TimeSpan splitElapsed)
    {
        var result = open
            .Select(kv => new Observation(kv.Key.Marker, kv.Key.Variant, splitElapsed - kv.Value))
            .ToList();
        open.Clear();
        IsActive = false;
        return result;
    }

    public void Discard()
    {
        open.Clear();
        CurrentMarker = 0;
        CurrentVariant = Variant.Hot;
        DiedThisSegment = false;
        IsActive = false;
    }
}
