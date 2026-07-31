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
            situationArrivalElapsed: null, markerBest: Bests((1, Variant.Hot, 10)));
        Assert.Null(p);
    }

    [Fact]
    public void AfterDeathUsesLearnedBestAnchoredAtArrival()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(150), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Cold, 22)));
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
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(170), p.Finish);   // max(140+22, 170)
    }

    [Fact]
    public void PreRespawnAnchorsAtElapsedSoEstimateRises()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(145), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: null, markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(145 + 22), p.Finish);   // no anchor yet: time is bleeding
    }

    [Fact]
    public void FallsBackToOtherVariantFlaggedUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Hot, 18)));
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
            situationArrivalElapsed: S(135), markerBest: NoBests);
        Assert.Equal(S(140), p.Finish);   // max(90+30, 140) = elapsed
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.StandardBpt, p.Source);
    }

    [Fact]
    public void NoDataAtAllYieldsNullFinishUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: null,
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), markerBest: NoBests);
        Assert.NotNull(p);
        Assert.Null(p.Finish);
        Assert.True(p.Unlearned);
    }
}
