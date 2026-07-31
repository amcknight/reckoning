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
    public void RespawnJumpIsCapturedThenAmountFreezes()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(3), nowMs: 0);            // grew a little pre-respawn
        hit.OnRespawn();
        hit.Update(S(22), nowMs: 100);         // re-anchor jump lands on THIS sample
        Assert.Equal(S(22), hit.Amount);       // jump captured...
        Assert.Equal(255, hit.Alpha(100));     // ...and fade starts here
        hit.Update(S(30), nowMs: 200);
        Assert.Equal(S(22), hit.Amount);       // ...then frozen
    }

    [Fact]
    public void FadesLinearlyFromTheCapturingSample()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.OnRespawn();
        hit.Update(S(22), nowMs: 0);           // capture + fade start at t=0
        int alpha = hit.Alpha(DamageHit.FadeDurationMs / 2);
        Assert.InRange(alpha, 120, 135);       // ~half faded
    }

    [Fact]
    public void ExpiresAfterFadeDuration()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.OnRespawn();
        hit.Update(S(5), nowMs: 0);            // capture + fade start
        hit.Update(S(5), nowMs: DamageHit.FadeDurationMs + 1);
        Assert.False(hit.Visible);
        Assert.Equal(0, hit.Alpha(DamageHit.FadeDurationMs + 1));
    }

    [Fact]
    public void NullSunkAfterRespawnStillFreezesSoTheHitCannotLingerForever()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(4), nowMs: 0);
        hit.OnRespawn();
        hit.Update(null, nowMs: 100);          // sunk unavailable: freeze anyway
        hit.Update(S(90), nowMs: 200);
        Assert.Equal(S(4), hit.Amount);        // pre-respawn amount kept
        hit.Update(S(90), nowMs: 100 + DamageHit.FadeDurationMs + 1);
        Assert.False(hit.Visible);
    }

    [Fact]
    public void SecondDeathRestartsWithNewBaseline()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(0));
        hit.Update(S(22), 0);
        hit.OnRespawn();
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
