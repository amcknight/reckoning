using System;

namespace LiveSplit.Reckoning.Engine;

public static class ReckoningCalculator
{
    public static ReckoningResult Compute(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        TimeSpan? remainingFullBestsSum,
        bool diedThisSegment,
        int currentMarker,
        Func<int, Variant, TimeSpan?> markerBest)
    {
        // Standard BPT's current-segment finish: can't finish in the past,
        // hence the max with elapsed (mirrors LiveSplit's own BPT).
        TimeSpan? standardFinish = currentSegmentFullBest is TimeSpan fb
            ? Max(segmentStartElapsed + fb, elapsed)
            : null;
        TimeSpan? standardBpt = Add(standardFinish, remainingFullBestsSum);

        if (!diedThisSegment)
        {
            // Deathless: the naive calculation is still achievable. DR-BPT is
            // standard BPT by definition, which makes Sunk exactly zero.
            return new ReckoningResult(standardBpt, standardBpt is null ? null : TimeSpan.Zero, false, BestSource.StandardBpt);
        }

        // Fallback chain: cold -> hot -> standard BPT (spec §Hot/cold).
        TimeSpan? finish;
        BestSource source;
        bool unlearned;
        if (markerBest(currentMarker, Variant.Cold) is TimeSpan cold)
        {
            finish = elapsed + cold;
            source = BestSource.ColdBest;
            unlearned = false;
        }
        else if (markerBest(currentMarker, Variant.Hot) is TimeSpan hot)
        {
            finish = elapsed + hot;
            source = BestSource.HotBest;
            unlearned = true;
        }
        else
        {
            finish = standardFinish;
            source = BestSource.StandardBpt;
            unlearned = true;
        }

        TimeSpan? drBpt = Add(finish, remainingFullBestsSum);
        // Sunk is honest arithmetic, not clamped: with consistent data it is
        // >= 0, and a negative value would expose an inconsistency worth seeing.
        TimeSpan? sunk = drBpt is TimeSpan d && standardBpt is TimeSpan s ? d - s : null;
        return new ReckoningResult(drBpt, sunk, unlearned, source);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static TimeSpan? Add(TimeSpan? a, TimeSpan? b) =>
        a is TimeSpan x && b is TimeSpan y ? x + y : null;
}
