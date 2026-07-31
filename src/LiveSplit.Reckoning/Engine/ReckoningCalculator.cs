using System;

namespace LiveSplit.Reckoning.Engine;

public static class ReckoningCalculator
{
    /// <summary>Predicts when the current split will finish, given death state.
    /// Returns null while deathless: no adjustment — the stock Run Prediction
    /// formula applies untouched, which makes Sunk exactly zero by construction.</summary>
    public static SituationPrediction PredictFinish(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        bool diedThisSegment,
        int currentMarker,
        Variant currentVariant,
        TimeSpan? situationArrivalElapsed,
        Func<int, Variant, TimeSpan?> markerBest)
    {
        if (!diedThisSegment) return null;

        // The situation's best is anchored at the moment the situation was entered,
        // not at "now" — otherwise the estimate ramps upward during normal play.
        // Before respawn there is no anchor yet: time is genuinely still bleeding,
        // so `elapsed` is the honest anchor and the estimate rises until respawn.
        TimeSpan anchor = situationArrivalElapsed ?? elapsed;
        Variant other = currentVariant == Variant.Cold ? Variant.Hot : Variant.Cold;

        if (markerBest(currentMarker, currentVariant) is TimeSpan preferred)
            return new SituationPrediction(Max(anchor + preferred, elapsed), false, ToSource(currentVariant));

        // Wrong-variant data beats no data; flagged unlearned (spec fallback chain).
        if (markerBest(currentMarker, other) is TimeSpan fallback)
            return new SituationPrediction(Max(anchor + fallback, elapsed), true, ToSource(other));

        // Last rung: the segment gold from split start (can't finish in the past).
        if (currentSegmentFullBest is TimeSpan fb)
            return new SituationPrediction(Max(segmentStartElapsed + fb, elapsed), true, BestSource.StandardBpt);

        return new SituationPrediction(null, true, BestSource.StandardBpt);
    }

    private static BestSource ToSource(Variant v) => v == Variant.Cold ? BestSource.ColdBest : BestSource.HotBest;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
