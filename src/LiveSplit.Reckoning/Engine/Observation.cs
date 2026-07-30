using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>A completed marker→exit span within one segment attempt.</summary>
public sealed record Observation(int MarkerIndex, Variant Variant, TimeSpan Duration);
