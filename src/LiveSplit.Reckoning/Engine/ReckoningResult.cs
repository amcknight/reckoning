using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Which rung of the fallback chain answered the current-segment term.</summary>
public enum BestSource
{
    StandardBpt,
    ColdBest,
    HotBest,
}

/// <summary>Death-aware prediction of the current split's finish time
/// (run-elapsed). Null Finish: died, but no estimate is available — the
/// display falls back to the stock value, flagged unlearned.</summary>
public sealed record SituationPrediction(TimeSpan? Finish, bool Unlearned, BestSource Source);
