using System;
using System.Linq;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SegmentTrackerTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void StartSegmentIsHotMarkerZero()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(10));
        Assert.Equal(0, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        Assert.False(t.DiedThisSegment);
        Assert.True(t.IsActive);
    }

    [Fact]
    public void DeathlessSegmentCompletesSingleHotZeroObservation()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(10));
        var obs = t.CompleteSegment(S(45));
        var o = Assert.Single(obs);
        Assert.Equal(new Observation(0, Variant.Hot, S(35)), o);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void CheckpointsAreOrderedMarkers()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));   // marker 1
        t.OnCheckpoint(S(35));   // marker 2 (multi-checkpoint retry hack)
        Assert.Equal(2, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        var obs = t.CompleteSegment(S(60));
        Assert.Equal(3, obs.Count);
        Assert.Contains(new Observation(0, Variant.Hot, S(60)), obs);
        Assert.Contains(new Observation(1, Variant.Hot, S(40)), obs);
        Assert.Contains(new Observation(2, Variant.Hot, S(25)), obs);
    }

    [Fact]
    public void DeathFlipsVariantColdAtCurrentMarker()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        Assert.True(t.DiedThisSegment);
        Assert.Equal(1, t.CurrentMarker);
        Assert.Equal(Variant.Cold, t.CurrentVariant);
    }

    [Fact]
    public void RespawnOpensColdObservationAtRespawnTime()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.OnRespawn(S(26));
        var obs = t.CompleteSegment(S(70));
        Assert.Contains(new Observation(1, Variant.Cold, S(44)), obs);   // 70 - 26
        Assert.Contains(new Observation(1, Variant.Hot, S(50)), obs);    // hot obs survives the death
        Assert.Contains(new Observation(0, Variant.Hot, S(70)), obs);
    }

    [Fact]
    public void SecondDeathAtSameMarkerOverwritesColdObservation()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.OnRespawn(S(26));
        t.OnDeath();
        t.OnRespawn(S(40));
        var obs = t.CompleteSegment(S(70));
        Assert.Single(obs.Where(o => o.Variant == Variant.Cold));
        Assert.Contains(new Observation(1, Variant.Cold, S(30)), obs);   // latest respawn wins
    }

    [Fact]
    public void CheckpointAfterColdRespawnIsHotAgain()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnDeath();             // death at marker 0
        t.OnRespawn(S(8));       // cold at marker 0 (spec: marker 0 has both variants)
        Assert.Equal(Variant.Cold, t.CurrentVariant);
        t.OnCheckpoint(S(30));   // reached checkpoint alive -> hot at marker 1
        Assert.Equal(1, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        var obs = t.CompleteSegment(S(50));
        Assert.Contains(new Observation(0, Variant.Cold, S(42)), obs);
        Assert.Contains(new Observation(1, Variant.Hot, S(20)), obs);
    }

    [Fact]
    public void DiscardDropsEverything()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.Discard();
        Assert.False(t.IsActive);
        t.StartSegment(S(30));
        Assert.Equal(0, t.CurrentMarker);
        Assert.False(t.DiedThisSegment);
        var obs = t.CompleteSegment(S(40));
        Assert.Equal(new Observation(0, Variant.Hot, S(10)), Assert.Single(obs));
    }

    [Fact]
    public void CurrentHotArrivalTracksTheMarkersHotOpen()
    {
        var t = new SegmentTracker();
        t.StartSegment(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(90), t.CurrentHotArrival);   // marker 0 = segment start
        t.OnCheckpoint(TimeSpan.FromSeconds(110));
        Assert.Equal(TimeSpan.FromSeconds(110), t.CurrentHotArrival);
        t.OnDeath();
        Assert.Equal(TimeSpan.FromSeconds(110), t.CurrentHotArrival);  // hot arrival survives death
        t.ResumeSegmentUnanchored();
        Assert.Null(t.CurrentHotArrival);                              // unanchored: unknown
    }

    [Fact]
    public void RepeatDeathClearsTheStaleColdAnchorUntilRespawn()
    {
        var t = new SegmentTracker();
        t.StartSegment(TimeSpan.FromSeconds(90));
        t.OnDeath();
        t.OnRespawn(TimeSpan.FromSeconds(100));
        Assert.Equal(TimeSpan.FromSeconds(100), t.CurrentArrival);   // anchored at respawn
        t.OnDeath();                                                 // die again at the same marker
        Assert.Null(t.CurrentArrival);                               // stale anchor gone: time bleeds again
        Assert.Equal(TimeSpan.FromSeconds(90), t.CurrentHotArrival); // gold prior input survives
        t.OnRespawn(TimeSpan.FromSeconds(130));
        Assert.Equal(TimeSpan.FromSeconds(130), t.CurrentArrival);   // fresh anchor
    }
}
