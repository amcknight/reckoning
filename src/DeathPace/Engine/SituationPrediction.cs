using System;

namespace DeathPace.Engine;

/// <summary>Which rung of the fallback chain answered the current-segment term.</summary>
public enum BestSource
{
    /// <summary>Unanchored resume (undo/skip left the hot arrival unknowable):
    /// priced from the segment gold at segment start, with no situation
    /// anchor. The "standard BPT" concept this used to name no longer
    /// exists post-rebase.</summary>
    SegmentGold,
    ColdBest,
    HotBest,
    /// <summary>No learned data: segment gold minus this run's hot progress to the marker, anchored at the situation.</summary>
    GoldPrior,
}

/// <summary>Death-aware prediction of the current split's finish time
/// (run-elapsed). Null Finish: died, but no estimate is available — the
/// display falls back to the stock value, flagged unlearned.</summary>
public sealed record SituationPrediction(TimeSpan? Finish, bool Unlearned, BestSource Source);
