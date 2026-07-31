using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Transient "damage number" for a death: appears at the death event,
/// grows while time bleeds (death animation), freezes at respawn, then fades.
/// Pure: callers supply sunk values and a monotonic millisecond clock.</summary>
public sealed class DamageHit
{
    // 2500 ms: long enough to read a short number after respawn, short enough
    // to be gone before the next obstacle needs the player's eyes.
    public const long FadeDurationMs = 2500;

    private TimeSpan baseline;     // sunk at the moment this death started
    private bool active;
    private bool fading;           // respawn seen: amount frozen, fade running
    private long fadeStartMs;

    public bool Visible => active;
    public TimeSpan Amount { get; private set; }

    public int Alpha(long nowMs) =>
        !active ? 0
        : !fading ? 255
        : (int)Math.Max(0, 255 - 255 * (nowMs - fadeStartMs) / FadeDurationMs);

    public void OnDeath(TimeSpan? sunkNow)
    {
        baseline = sunkNow ?? TimeSpan.Zero;
        Amount = TimeSpan.Zero;
        active = true;
        fading = false;
    }

    public void OnRespawn(long nowMs)
    {
        if (!active || fading) return;
        fading = true;
        fadeStartMs = nowMs;
    }

    public void Update(TimeSpan? sunkNow, long nowMs)
    {
        if (!active) return;
        if (!fading && sunkNow is TimeSpan s)
        {
            var grown = s - baseline;
            Amount = grown < TimeSpan.Zero ? TimeSpan.Zero : grown;
        }
        if (fading && nowMs - fadeStartMs >= FadeDurationMs) active = false;
    }

    public void Clear() => active = false;
}
