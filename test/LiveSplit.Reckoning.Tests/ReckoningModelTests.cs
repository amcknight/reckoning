using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningModelTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void SplitRecordsObservationsForFinishedSegmentAndAdvances()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnSplit(S(50));
        Assert.Equal(1, m.CurrentSegmentIndex);
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var hot0));
        Assert.Equal(S(50), hot0);
        Assert.True(store.TryGetBest(0, 1, Variant.Hot, out var hot1));
        Assert.Equal(S(30), hot1);
        Assert.False(m.DiedThisSegment);   // new segment starts clean
    }

    [Fact]
    public void DeathRespawnSplitRecordsColdBest()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnDeath();
        m.OnRespawn(S(26));
        m.OnSplit(S(70));
        Assert.True(store.TryGetBest(0, 1, Variant.Cold, out var cold));
        Assert.Equal(S(44), cold);
    }

    [Fact]
    public void UndoSplitRevertsRecordsAndStepsBack()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(0, 0, Variant.Hot), new BestEntry(60_000, 3));
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));            // improves hot-0 best to 50s, attempts 4
        Assert.Equal(1, m.CurrentSegmentIndex);
        m.OnUndoSplit(S(55));
        Assert.Equal(0, m.CurrentSegmentIndex);
        Assert.True(store.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out var entry));
        Assert.Equal(60_000, entry.BestMs);   // prior best restored
        Assert.Equal(3, entry.Attempts);
    }

    [Fact]
    public void UndoSplitRemovesRecordsThatDidNotExistBefore()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));
        m.OnUndoSplit(S(55));
        Assert.False(store.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out _));
    }

    [Fact]
    public void SkipSplitRecordsNothingButAdvances()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnSkipSplit(S(30));
        Assert.Equal(1, m.CurrentSegmentIndex);
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void ResetDiscardsInFlightObservations()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnReset();
        Assert.False(m.IsRunning);
        Assert.Empty(store.Keys);
        m.OnStart(S(0));
        Assert.Equal(0, m.CurrentSegmentIndex);
    }

    [Fact]
    public void EventsBeforeStartAreIgnored()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnDeath();
        m.OnCheckpoint(S(5));
        m.OnSplit(S(10));
        Assert.False(m.IsRunning);
        Assert.Equal(0, m.CurrentSegmentIndex);
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void ComputeUsesCurrentSegmentBests()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(1, 1, Variant.Cold), new BestEntry(22_000, 2));
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(90));            // now in segment 1
        m.OnCheckpoint(S(110));
        m.OnDeath();
        var r = m.Compute(S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200));
        Assert.Equal(S(140 + 22 + 200), r.DrBpt);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }

    [Fact]
    public void UndoThenSplitRecordsNoMarkerZeroBest()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));            // records hot-0 = 50s
        m.OnUndoSplit(S(55));        // reverts the 50s record; resumes unanchored
        m.OnSplit(S(70));            // no anchored observation existed: records nothing
        Assert.False(store.TryGetBest(0, 0, Variant.Hot, out _));
    }

    [Fact]
    public void SkipThenSplitRecordsNothingForSkippedIntoSegment()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSkipSplit(S(30));        // segment 1 begins unanchored
        m.OnSplit(S(60));            // no anchored observation existed: records nothing
        Assert.False(store.TryGetBest(1, 0, Variant.Hot, out _));
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void CheckpointAfterUndoStillRecordsAnchored()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));
        m.OnUndoSplit(S(55));        // resumes segment 0 unanchored
        m.OnCheckpoint(S(60));       // genuinely anchored arrival at marker 1
        m.OnSplit(S(80));
        Assert.True(store.TryGetBest(0, 1, Variant.Hot, out var marker1));
        Assert.Equal(S(20), marker1);
        Assert.False(store.TryGetBest(0, 0, Variant.Hot, out _));
    }

    [Fact]
    public void PostRespawnCheckpointPricesHotVariant()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(0, 1, Variant.Hot), new BestEntry(18_000, 1));
        store.SetEntry(new MarkerKey(0, 1, Variant.Cold), new BestEntry(22_000, 1));
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnDeath();
        m.OnRespawn(S(10));
        m.OnCheckpoint(S(30));       // reached checkpoint alive: hot at marker 1
        var r = m.Compute(S(40), segmentStartElapsed: S(0),
            currentSegmentFullBest: S(50), remainingFullBestsSum: S(100));
        Assert.Equal(S(30 + 18 + 100), r.DrBpt);
        Assert.Equal(BestSource.HotBest, r.Source);
        Assert.False(r.Unlearned);
    }
}
