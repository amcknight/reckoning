using System;
using System.Collections.Generic;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Run-scoped orchestrator: maps timer lifecycle events onto the
/// tracker and store, and answers DR-BPT queries. Pure — the component shell
/// is the only place LiveSplit types appear.</summary>
public sealed class ReckoningModel
{
    private readonly BestsStore store;
    private readonly SegmentTracker tracker = new();
    // One journal frame per real split: (key, prior entry or null) per record,
    // so an undone split's records can be reverted exactly.
    private readonly Stack<List<(MarkerKey Key, BestEntry Prior)>> journal = new();

    public ReckoningModel(BestsStore store) => this.store = store;

    public int CurrentSegmentIndex { get; private set; }
    public bool IsRunning { get; private set; }
    public bool DiedThisSegment => tracker.DiedThisSegment;
    public int CurrentMarker => tracker.CurrentMarker;

    public void OnStart(TimeSpan elapsed)
    {
        CurrentSegmentIndex = 0;
        journal.Clear();
        IsRunning = true;
        tracker.StartSegment(elapsed);
    }

    public void OnDeath() { if (IsRunning) tracker.OnDeath(); }
    public void OnCheckpoint(TimeSpan elapsed) { if (IsRunning) tracker.OnCheckpoint(elapsed); }
    public void OnRespawn(TimeSpan elapsed) { if (IsRunning) tracker.OnRespawn(elapsed); }

    public void OnSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        var frame = new List<(MarkerKey, BestEntry)>();
        foreach (var obs in tracker.CompleteSegment(elapsed))
        {
            var key = new MarkerKey(CurrentSegmentIndex, obs.MarkerIndex, obs.Variant);
            store.TryGetEntry(key, out var prior);   // prior is null when absent
            frame.Add((key, prior));
            store.Record(CurrentSegmentIndex, obs.MarkerIndex, obs.Variant, obs.Duration);
        }
        journal.Push(frame);
        CurrentSegmentIndex++;
        tracker.StartSegment(elapsed);
    }

    public void OnUndoSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        if (journal.Count > 0)
        {
            foreach (var (key, prior) in journal.Pop())
            {
                if (prior is null) store.RemoveEntry(key);
                else store.SetEntry(key, prior);
            }
        }
        if (CurrentSegmentIndex > 0) CurrentSegmentIndex--;
        // Spec: the affected segment's marker state and in-flight observations
        // are discarded — we restart it at marker 0.
        tracker.Discard();
        tracker.StartSegment(elapsed);
    }

    public void OnSkipSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        tracker.Discard();                       // skip is not a real split: record nothing
        journal.Push(new List<(MarkerKey, BestEntry)>());   // keep journal aligned with undo depth
        CurrentSegmentIndex++;
        tracker.StartSegment(elapsed);
    }

    public void OnReset()
    {
        tracker.Discard();
        journal.Clear();
        IsRunning = false;
        CurrentSegmentIndex = 0;
    }

    public ReckoningResult Compute(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        TimeSpan? remainingFullBestsSum)
    {
        int segment = CurrentSegmentIndex;
        return ReckoningCalculator.Compute(
            elapsed, segmentStartElapsed, currentSegmentFullBest, remainingFullBestsSum,
            tracker.DiedThisSegment, tracker.CurrentMarker,
            (marker, variant) => store.TryGetBest(segment, marker, variant, out var b) ? b : null);
    }
}
