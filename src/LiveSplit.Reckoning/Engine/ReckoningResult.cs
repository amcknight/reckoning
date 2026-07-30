using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Which rung of the fallback chain answered the current-segment term.</summary>
public enum BestSource
{
    StandardBpt,
    ColdBest,
    HotBest,
}

public sealed record ReckoningResult(TimeSpan? DrBpt, TimeSpan? Sunk, bool Unlearned, BestSource Source);
