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
    private bool pendingFreeze;    // respawn seen: freeze on the next sample

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
        pendingFreeze = false;
    }

    /// <summary>The re-anchor jump (arrival + best) is computed on the Update
    /// AFTER the respawn event, so the freeze is deferred one sample — freezing
    /// at the event itself would miss the death's real cost.</summary>
    public void OnRespawn()
    {
        if (active && !fading) pendingFreeze = true;
    }

    public void Update(TimeSpan? sunkNow, long nowMs)
    {
        if (!active) return;
        if (!fading)
        {
            if (sunkNow is TimeSpan s)
            {
                var grown = s - baseline;
                Amount = grown < TimeSpan.Zero ? TimeSpan.Zero : grown;
            }
            if (pendingFreeze)
            {
                fading = true;
                fadeStartMs = nowMs;
                pendingFreeze = false;
            }
        }
        if (fading && nowMs - fadeStartMs >= FadeDurationMs) active = false;
    }

    public void Clear() => active = false;
}
