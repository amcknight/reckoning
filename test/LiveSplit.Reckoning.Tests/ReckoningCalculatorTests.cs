using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningCalculatorTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static Func<int, Variant, TimeSpan?> NoBests => (_, _) => null;

    private static Func<int, Variant, TimeSpan?> Bests(params (int Marker, Variant V, double Secs)[] entries) =>
        (m, v) =>
        {
            foreach (var e in entries)
                if (e.Marker == m && e.V == v) return S(e.Secs);
            return null;
        };

    [Fact]
    public void DeathlessReturnsNull_StockFormulaApplies()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(100), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: false, currentMarker: 1, currentVariant: Variant.Hot,
            situationArrivalElapsed: null, hotArrivalAtCurrentMarker: null, markerBest: Bests((1, Variant.Hot, 10)));
        Assert.Null(p);
    }

    [Fact]
    public void AfterDeathUsesLearnedBestAnchoredAtArrival()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(150), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: null, markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(140 + 22), p.Finish);   // anchored at arrival, not at elapsed=150
        Assert.False(p.Unlearned);
        Assert.Equal(BestSource.ColdBest, p.Source);
    }

    [Fact]
    public void AfterDeathClampsToElapsedWhenBehindLearnedPace()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(170), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: null, markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(170), p.Finish);   // max(140+22, 170)
    }

    [Fact]
    public void PreRespawnAnchorsAtElapsedSoEstimateRises()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(145), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: null, hotArrivalAtCurrentMarker: null, markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(145 + 22), p.Finish);   // no anchor yet: time is bleeding
    }

    [Fact]
    public void FallsBackToOtherVariantFlaggedUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: null, markerBest: Bests((1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 18), p.Finish);
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.HotBest, p.Source);
    }

    [Fact]
    public void FallsBackToSegmentGoldWhenNoMarkerData()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), hotArrivalAtCurrentMarker: null, markerBest: NoBests);
        Assert.Equal(S(140), p.Finish);   // max(90+30, 140) = elapsed
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.SegmentGold, p.Source);
    }

    [Fact]
    public void NoDataAtAllYieldsNullFinishUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: null,
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), hotArrivalAtCurrentMarker: null, markerBest: NoBests);
        Assert.NotNull(p);
        Assert.Null(p.Finish);
        Assert.True(p.Unlearned);
    }

    [Fact]
    public void FirstDeathAtMarkerZeroPricesReplayFromTheAnchor()
    {
        // No learned data at all; hot arrival at marker 0 IS the segment start.
        // Prior: replay the segment from the respawn -> anchor + gold.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(145), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(90),
            markerBest: NoBests);
        Assert.Equal(S(140 + 30), p.Finish);
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.GoldPrior, p.Source);
    }

    [Fact]
    public void GoldPriorSubtractsHotProgressAtLaterMarkers()
    {
        // Reached marker 2 hot at 110 (20s into a 30s-gold segment): 10s of gold
        // remains from there; anchored at the cold arrival.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(141), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 2, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(110),
            markerBest: NoBests);
        Assert.Equal(S(140 + 10), p.Finish);
        Assert.Equal(BestSource.GoldPrior, p.Source);
    }

    [Fact]
    public void SlowerThanGoldArrivalClampsThePriorRemainingToZero()
    {
        // Hot arrival 50s into a 30s gold: no future credit; finish = max(anchor, elapsed).
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(150), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(145), hotArrivalAtCurrentMarker: S(140),
            markerBest: NoBests);
        Assert.Equal(S(150), p.Finish);   // max(145 + 0, 150)
    }

    [Fact]
    public void PreRespawnGoldPriorAnchorsAtElapsedSoItTicks()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(143), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: null, hotArrivalAtCurrentMarker: S(90),
            markerBest: NoBests);
        Assert.Equal(S(143 + 30), p.Finish);   // anchor = elapsed until respawn
    }

    [Fact]
    public void NoHotArrivalFallsBackToSegmentStartGold()
    {
        // Unanchored resume (undo/skip): old last-rung behavior, SegmentGold source.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), hotArrivalAtCurrentMarker: null,
            markerBest: NoBests);
        Assert.Equal(S(140), p.Finish);   // max(90+30, 140)
        Assert.Equal(BestSource.SegmentGold, p.Source);
    }

    [Fact]
    public void LearnedDataStillBeatsTheGoldPrior()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(141), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(100),
            markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(140 + 22), p.Finish);
        Assert.Equal(BestSource.ColdBest, p.Source);
        Assert.False(p.Unlearned);
    }
}
