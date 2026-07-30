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
    public void DeathlessEqualsStandardBptWithZeroSunk()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(100), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 1, markerBest: Bests((1, Variant.Hot, 10)));
        Assert.Equal(S(90 + 30 + 200), r.DrBpt);   // marker data ignored while deathless
        Assert.Equal(TimeSpan.Zero, r.Sunk);
        Assert.False(r.Unlearned);
        Assert.Equal(BestSource.StandardBpt, r.Source);
    }

    [Fact]
    public void DeathlessClampsToElapsedWhenBehindBestSegment()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(130), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 0, markerBest: NoBests);
        Assert.Equal(S(130 + 200), r.DrBpt);   // max(90+30, 130) = 130
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void AfterDeathUsesColdBestFromCurrentMarker()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1,
            markerBest: Bests((1, Variant.Cold, 22), (1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 22 + 200), r.DrBpt);
        // standard = max(90+30, 140) + 200 = 340 ; sunk = 362 - 340
        Assert.Equal(S(22), r.Sunk);
        Assert.False(r.Unlearned);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }

    [Fact]
    public void MissingColdFallsBackToHotFlaggedUnlearned()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1,
            markerBest: Bests((1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 18 + 200), r.DrBpt);
        Assert.True(r.Unlearned);
        Assert.Equal(BestSource.HotBest, r.Source);
    }

    [Fact]
    public void NoMarkerDataDegradesToStandardBptFlaggedUnlearned()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1, markerBest: NoBests);
        Assert.Equal(S(140 + 200), r.DrBpt);   // max(120, 140) + 200
        Assert.Equal(TimeSpan.Zero, r.Sunk);   // identical to standard by definition
        Assert.True(r.Unlearned);
        Assert.Equal(BestSource.StandardBpt, r.Source);
    }

    [Fact]
    public void NullBestSegmentMakesResultNull()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: null, remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 0, markerBest: NoBests);
        Assert.Null(r.DrBpt);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void ColdBestStillWorksWhenLiveSplitBestsMissing()
    {
        // Learned cold data answers even when LiveSplit has no best segment yet,
        // but Sunk needs standard BPT so it stays null.
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: null, remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 25)));
        Assert.Equal(S(140 + 25 + 200), r.DrBpt);
        Assert.Null(r.Sunk);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }

    [Fact]
    public void NullRemainingSumMakesResultNull()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: null,
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 25)));
        Assert.Null(r.DrBpt);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void LastSegmentUsesZeroRemainingSum()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(300), segmentStartElapsed: S(280),
            currentSegmentFullBest: S(40), remainingFullBestsSum: TimeSpan.Zero,
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 35)));
        Assert.Equal(S(335), r.DrBpt);
        Assert.Equal(S(335 - 320), r.Sunk);    // standard = max(320, 300) = 320
    }
}
