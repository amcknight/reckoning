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
        TimeSpan? hotArrivalAtCurrentMarker,
        Func<int, Variant, TimeSpan?> markerBest)
    {
        if (!diedThisSegment) return null;

        // The situation's best is anchored at the moment the situation was entered,
        // not at "now" — otherwise the estimate ramps upward during normal play.
        // Before respawn there is no anchor yet: time is genuinely still bleeding,
        // so `elapsed` is the honest anchor and the estimate rises until respawn.
        TimeSpan anchor = situationArrivalElapsed ?? elapsed;
        Variant other = currentVariant == Variant.Cold ? Variant.Hot : Variant.Cold;

        // The three rungs below all anchor at the situation entry; only the
        // final (unanchored-resume) rung anchors elsewhere.
        SituationPrediction FromAnchor(TimeSpan best, bool unlearned, BestSource src) =>
            new(Max(anchor + best, elapsed), unlearned, src);

        if (markerBest(currentMarker, currentVariant) is TimeSpan preferred)
            return FromAnchor(preferred, false, ToSource(currentVariant));

        // Wrong-variant data beats no data; flagged unlearned (spec fallback chain).
        if (markerBest(currentMarker, other) is TimeSpan fallback)
            return FromAnchor(fallback, true, ToSource(other));

        // Gold prior (no learned data for this marker): the segment gold is the
        // hot prior; what remains of it from this marker is gold minus the hot
        // time already spent reaching the marker this run. Marker 0's hot
        // arrival is the segment start, so this collapses to "replay the
        // segment from the anchor". Slower-than-gold arrivals clamp to zero:
        // the prior never grants future credit for time already lost.
        if (currentSegmentFullBest is TimeSpan gold && hotArrivalAtCurrentMarker is TimeSpan hotArrival)
        {
            TimeSpan remaining = gold - (hotArrival - segmentStartElapsed);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            return FromAnchor(remaining, true, BestSource.GoldPrior);
        }

        // Unanchored resume (undo/skip left the hot arrival unknowable): the
        // segment gold from split start remains the only honest floor.
        if (currentSegmentFullBest is TimeSpan fb)
            return new SituationPrediction(Max(segmentStartElapsed + fb, elapsed), true, BestSource.SegmentGold);

        return new SituationPrediction(null, true, BestSource.SegmentGold);
    }

    private static BestSource ToSource(Variant v) => v == Variant.Cold ? BestSource.ColdBest : BestSource.HotBest;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
