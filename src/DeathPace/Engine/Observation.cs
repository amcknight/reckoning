using System;

namespace DeathPace.Engine;

/// <summary>A completed marker→exit span within one segment attempt.</summary>
public sealed record Observation(int MarkerIndex, Variant Variant, TimeSpan Duration);
