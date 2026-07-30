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
}
