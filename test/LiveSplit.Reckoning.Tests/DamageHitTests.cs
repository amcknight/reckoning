using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class DamageHitTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void InvisibleUntilDeath()
    {
        var hit = new DamageHit();
        hit.Update(S(0), nowMs: 0);
        Assert.False(hit.Visible);
    }

    [Fact]
    public void GrowsFromBaselineDuringDeathAnimation()
    {
        var hit = new DamageHit();
        hit.OnDeath(sunkNow: S(10));           // 10s already sunk this segment
        hit.Update(S(13), nowMs: 100);
        Assert.True(hit.Visible);
        Assert.Equal(S(3), hit.Amount);        // only THIS death's cost
        Assert.Equal(255, hit.Alpha(100));
    }

    [Fact]
    public void FreezesAtRespawnThenFadesLinearly()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(22), nowMs: 0);
        hit.OnRespawn(nowMs: 0);
        hit.Update(S(30), nowMs: DamageHit.FadeDurationMs / 2);   // sunk keeps moving...
        Assert.Equal(S(22), hit.Amount);                          // ...amount does not
        int alpha = hit.Alpha(DamageHit.FadeDurationMs / 2);
        Assert.InRange(alpha, 120, 135);                          // ~half faded
    }

    [Fact]
    public void ExpiresAfterFadeDuration()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.OnRespawn(nowMs: 0);
        hit.Update(S(5), nowMs: DamageHit.FadeDurationMs + 1);
        Assert.False(hit.Visible);
        Assert.Equal(0, hit.Alpha(DamageHit.FadeDurationMs + 1));
    }

    [Fact]
    public void SecondDeathRestartsWithNewBaseline()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(22), 0);
        hit.OnRespawn(0);
        hit.OnDeath(S(22));                    // died again later in the segment
        hit.Update(S(30), 500);
        Assert.True(hit.Visible);
        Assert.Equal(S(8), hit.Amount);
        Assert.Equal(255, hit.Alpha(500));     // fade restarted
    }

    [Fact]
    public void NegativeGrowthClampsToZero()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(10));
        hit.Update(S(9), 0);                   // inconsistent data must not show "-(-1)"
        Assert.Equal(TimeSpan.Zero, hit.Amount);
    }

    [Fact]
    public void ClearHidesImmediately()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(5), 0);
        hit.Clear();
        Assert.False(hit.Visible);
    }
}
