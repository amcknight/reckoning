using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Transient "damage number" for a death: appears at the death event,
/// grows while time bleeds (death animation), freezes at respawn, then fades.
/// Pure: callers supply death-aware values and a monotonic millisecond clock.
///
/// Baselines on the death-aware VALUE at the death instant — not the sunk
/// time — because the value is what the run was headed for. The death
/// re-anchors the estimate to "now + replay-from-respawn", which ticks 1:1
/// through the animation while the death-instant value stays fixed, so the
/// frozen amount is replay estimate + death→spawn downtime: this death's
/// true cost, independent of whether the sunk clock happened to be ticking
/// too. Sunk-baselining cancelled the downtime whenever the runner was
/// behind pace at the moment of death.</summary>
public sealed class DamageHit
{
    // 2500 ms: long enough to read a short number after respawn, short enough
    // to be gone before the next obstacle needs the player's eyes.
    public const long FadeDurationMs = 2500;

    private TimeSpan baseline;     // value at the moment this death started
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

    /// <summary>Arms a hit measuring THIS death's cost: baseline is the
    /// death-aware value the run was headed for at the death instant, so the
    /// growing amount is replay estimate + death downtime — independent of
    /// whether the stock value happens to be ticking too. A null value means
    /// there is no comparison data and nothing honest to show: no activation.</summary>
    public void OnDeath(TimeSpan? valueNow)
    {
        if (valueNow is not TimeSpan v)
        {
            active = false;
            return;
        }
        baseline = v;
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

    public void Update(TimeSpan? valueNow, long nowMs)
    {
        if (!active) return;
        if (!fading)
        {
            if (valueNow is TimeSpan v)
            {
                var grown = v - baseline;
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
