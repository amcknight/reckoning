using System;

namespace LiveSplit.Reckoning.Engine;

public readonly record struct ComposedPrediction(TimeSpan? StockValue, TimeSpan? Value, TimeSpan? Sunk);

/// <summary>The stock Run Prediction running-phase formula, with an optional
/// death-aware substitute for the live delta.
/// Ported from LiveSplit's RunPrediction component (MIT).</summary>
public static class PredictionMath
{
    public static ComposedPrediction Compose(
        TimeSpan? lastDelta,
        TimeSpan elapsed,
        TimeSpan? comparisonAtCurrentSplit,
        TimeSpan? comparisonFinal,
        TimeSpan? predictedFinish)
    {
        TimeSpan locked = lastDelta ?? TimeSpan.Zero;   // stock coalesces null to zero

        TimeSpan? liveDelta = comparisonAtCurrentSplit is TimeSpan c ? elapsed - c : (TimeSpan?)null;
        TimeSpan stockDelta = liveDelta is TimeSpan ld && ld > locked ? ld : locked;
        TimeSpan? stockValue = comparisonFinal is TimeSpan f ? stockDelta + f : (TimeSpan?)null;

        if (predictedFinish is not TimeSpan pf || comparisonAtCurrentSplit is not TimeSpan cc)
            return new ComposedPrediction(stockValue, stockValue,
                stockValue is null ? null : TimeSpan.Zero);

        TimeSpan drLive = pf - cc;
        TimeSpan drDelta = drLive > locked ? drLive : locked;
        TimeSpan? value = comparisonFinal is TimeSpan f2 ? drDelta + f2 : (TimeSpan?)null;
        TimeSpan? sunk = value is TimeSpan v && stockValue is TimeSpan s ? v - s : (TimeSpan?)null;
        return new ComposedPrediction(stockValue, value, sunk);
    }
}
