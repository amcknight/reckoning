using System;

namespace LiveSplit.Reckoning.Engine;

public readonly record struct ComposedPrediction(
    TimeSpan? StockValue,
    TimeSpan? Value,
    // No production consumers as of the death-cost hit (which baselines on
    // Value instead) — kept as the diagnostic/v2 surface (death-aware minus
    // stock); production display uses Value.
    TimeSpan? Sunk);

/// <summary>The stock Run Prediction running-phase formula, with an optional
/// death-aware substitute for the live delta.
/// Ported from LiveSplit's RunPrediction component (MIT).</summary>
public static class PredictionMath
{
    public static ComposedPrediction Compose(
        TimeSpan? lastDelta,
        TimeSpan? elapsed,
        TimeSpan? comparisonAtCurrentSplit,
        TimeSpan? comparisonFinal,
        TimeSpan? predictedFinish)
    {
        TimeSpan locked = lastDelta ?? TimeSpan.Zero;   // stock coalesces null to zero

        TimeSpan? WithFinal(TimeSpan delta) => comparisonFinal is TimeSpan f ? delta + f : (TimeSpan?)null;
        static TimeSpan Beat(TimeSpan lockedDelta, TimeSpan? candidate) =>
            candidate is TimeSpan cand && cand > lockedDelta ? cand : lockedDelta;

        // Null elapsed (e.g. game time with no game-time data yet) must stay
        // stock-parity: stock stays in its Running branch and simply drops
        // the live term, letting the locked delta survive. Same shape as a
        // null comparisonAtCurrentSplit below.
        TimeSpan? liveDelta = elapsed is TimeSpan e && comparisonAtCurrentSplit is TimeSpan atSplit ? e - atSplit : (TimeSpan?)null;
        TimeSpan? stockValue = WithFinal(Beat(locked, liveDelta));

        // With no elapsed there is no death-aware anchor either (the caller
        // never has a predictedFinish to offer without elapsed, but even if
        // it did, there's nothing to anchor it against) — treat drLive as
        // null and fall back to the stock value, same as the other
        // insufficient-data cases.
        if (elapsed is not TimeSpan || predictedFinish is not TimeSpan pf || comparisonAtCurrentSplit is not TimeSpan cc)
            return new ComposedPrediction(stockValue, stockValue,
                stockValue is null ? null : TimeSpan.Zero);

        TimeSpan? value = WithFinal(Beat(locked, pf - cc));
        TimeSpan? sunk = value is TimeSpan v && stockValue is TimeSpan s ? v - s : (TimeSpan?)null;
        return new ComposedPrediction(stockValue, value, sunk);
    }
}
