using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class PredictionMathTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void DeathlessMatchesStockFormula_AheadOfPace()
    {
        // Stock: delta = max(lastDelta, liveDelta); value = delta + comparisonFinal.
        // liveDelta (100-120=-20) < lastDelta (-5) → locked delta wins.
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(100),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(595), r.StockValue);
        Assert.Equal(S(595), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void DeathlessLiveDeltaOverridesWhenLosingTime()
    {
        // liveDelta (130-120=+10) > lastDelta (-5) → live wins (stock behavior).
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(130),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(610), r.StockValue);
        Assert.Equal(S(610), r.Value);
    }

    [Fact]
    public void NullLastDeltaCoalescesToZero()
    {
        var r = PredictionMath.Compose(
            lastDelta: null, elapsed: S(100),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(600), r.StockValue);   // max(0, -20) = 0
    }

    [Fact]
    public void DeathRaisesValueAndSunkIsTheDifference()
    {
        // Death-aware finish 145 vs comparison split 120 → drLiveDelta +25
        // beats both lastDelta (-5) and liveDelta (+10).
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(130),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: S(145));
        Assert.Equal(S(610), r.StockValue);
        Assert.Equal(S(625), r.Value);
        Assert.Equal(S(15), r.Sunk);
    }

    [Fact]
    public void DeathStillAheadOfLockedDeltaShowsNoLoss()
    {
        // Died, but predicted finish (118) still beats the comparison split (120):
        // drLiveDelta (-2) < lastDelta (+3) → locked delta rules both values.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: S(118));
        Assert.Equal(r.StockValue, r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void NullComparisonFinalYieldsNulls()
    {
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: S(120), comparisonFinal: null,
            predictedFinish: S(118));
        Assert.Null(r.StockValue);
        Assert.Null(r.Value);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void NullComparisonAtCurrentSplitFallsBackToLockedDelta()
    {
        // Stock: liveDelta null → the > comparison is false → locked delta kept.
        // Death-aware side has no split target either → identical to stock.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: null, comparisonFinal: S(600),
            predictedFinish: S(145));
        Assert.Equal(S(603), r.StockValue);
        Assert.Equal(S(603), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void NullElapsedLockedDeltaSurvives()
    {
        // Stock parity: stock stays in the Running branch when
        // CurrentTime[method] is null — liveDelta goes null and the locked
        // delta (lastDelta) survives untouched, exactly like a null
        // comparisonAtCurrentSplit.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: null,
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(603), r.StockValue);
        Assert.Equal(S(603), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void NullElapsedWithNonNullPredictedFinishStillFallsBackToStock()
    {
        // With null elapsed there is no death-aware prediction anyway (the
        // component skips ComputePrediction whenever elapsed is null) — but
        // Compose must handle a non-null predictedFinish arriving alongside
        // a null elapsed sanely regardless: treat drLive as null and fall
        // back to the stock value rather than throw.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: null,
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: S(118));
        Assert.Equal(S(603), r.StockValue);
        Assert.Equal(S(603), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }
}
