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
        hit.Update(S(600), nowMs: 0);
        Assert.False(hit.Visible);
    }

    [Fact]
    public void GrowsFromTheValueAtDeathDuringTheAnimation()
    {
        var hit = new DamageHit();
        hit.OnDeath(valueNow: S(600));         // run was headed for 10:00
        hit.Update(S(610), nowMs: 100);        // re-anchor + bleeding: now 10:10
        Assert.True(hit.Visible);
        Assert.Equal(S(10), hit.Amount);       // this death's cost so far
        Assert.Equal(255, hit.Alpha(100));
    }

    [Fact]
    public void RespawnJumpIsCapturedThenAmountFreezes()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.Update(S(603), nowMs: 0);          // bleeding through the animation
        hit.OnRespawn();
        hit.Update(S(622), nowMs: 100);        // re-anchored estimate lands on THIS sample
        Assert.Equal(S(22), hit.Amount);       // replay + downtime captured...
        Assert.Equal(255, hit.Alpha(100));
        hit.Update(S(630), nowMs: 200);
        Assert.Equal(S(22), hit.Amount);       // ...then frozen
    }

    [Fact]
    public void FadesLinearlyFromTheCapturingSample()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.OnRespawn();
        hit.Update(S(622), nowMs: 0);
        int alpha = hit.Alpha(DamageHit.FadeDurationMs / 2);
        Assert.InRange(alpha, 120, 135);
    }

    [Fact]
    public void ExpiresAfterFadeDuration()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.OnRespawn();
        hit.Update(S(605), nowMs: 0);
        hit.Update(S(605), nowMs: DamageHit.FadeDurationMs + 1);
        Assert.False(hit.Visible);
        Assert.Equal(0, hit.Alpha(DamageHit.FadeDurationMs + 1));
    }

    [Fact]
    public void NullValueAfterRespawnStillFreezesSoTheHitCannotLingerForever()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.Update(S(604), nowMs: 0);
        hit.OnRespawn();
        hit.Update(null, nowMs: 100);          // value unavailable: freeze anyway
        hit.Update(S(690), nowMs: 200);
        Assert.Equal(S(4), hit.Amount);        // pre-respawn amount kept
        hit.Update(S(690), nowMs: 100 + DamageHit.FadeDurationMs + 1);
        Assert.False(hit.Visible);
    }

    [Fact]
    public void SecondDeathRestartsFromTheNewValueBaseline()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.Update(S(622), 0);
        hit.OnRespawn();
        hit.OnDeath(S(622));                   // died again later in the segment
        hit.Update(S(630), 500);
        Assert.True(hit.Visible);
        Assert.Equal(S(8), hit.Amount);        // only the NEW death's cost
        Assert.Equal(255, hit.Alpha(500));     // fade restarted
    }

    [Fact]
    public void NegativeGrowthClampsToZero()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.Update(S(598), 0);                 // inconsistent data must not show "-(-2)"
        Assert.Equal(TimeSpan.Zero, hit.Amount);
    }

    [Fact]
    public void NullValueAtDeathDoesNotActivate()
    {
        var hit = new DamageHit();
        hit.OnDeath(valueNow: null);           // no comparison data: nothing honest to show
        hit.Update(S(600), 0);
        Assert.False(hit.Visible);
    }

    [Fact]
    public void ClearHidesImmediately()
    {
        var hit = new DamageHit();
        hit.OnDeath(S(600));
        hit.Update(S(605), 0);
        hit.Clear();
        Assert.False(hit.Visible);
    }
}
