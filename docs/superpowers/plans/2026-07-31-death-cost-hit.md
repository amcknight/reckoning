# Death Cost Hit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two fixes from Andrew's second live review: every death re-anchors the estimate at the death (so the display bleeds during the animation on repeat deaths exactly like first deaths), and the red hit measures the death's true cost — replay estimate plus death→spawn downtime — by baselining on the death-aware value at the death instant instead of on Sunk (which cancels out downtime whenever the runner is behind pace).

**Architecture:** `SegmentTracker.OnDeath` drops the current marker's open Cold observation, so the situation anchor is null until the new respawn (identical to first-death semantics; the dropped observation would have been overwritten at the new respawn anyway — re-arrival overwrites). `DamageHit` consumes the death-aware *value* rather than Sunk: baseline = value at death, amount = value now − baseline, activation skipped when the value is unavailable. The shell passes `lastComposed.Value` at both call sites.

**Tech Stack:** C# / net481, xUnit. Branch `feature/death-cost-hit` off main (`7fb6290`).

## Global Constraints

- Engine stays pure (System only, no locks). Red-green TDD for all engine changes.
- Tests: `dotnet test test/LiveSplit.Reckoning.Tests` (117 green at base). Release build zero warnings: `dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release`.
- NEVER commit anything under `.github/`.
- Commits end with: Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>

---

### Task 1: Every death clears the current marker's cold anchor

**Files:**
- Modify: `src/LiveSplit.Reckoning/Engine/SegmentTracker.cs`
- Test: `test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs`

**Interfaces:** no signature changes — `OnDeath()` gains behavior: the open `(CurrentMarker, Cold)` observation is removed, so `CurrentArrival` reads null (anchor = elapsed → estimate bleeds) until `OnRespawn` re-opens it. `CurrentHotArrival` is untouched (the gold prior still works mid-animation).

- [ ] **Step 1: Write the failing test**

Add to `SegmentTrackerTests.cs`:

```csharp
    [Fact]
    public void RepeatDeathClearsTheStaleColdAnchorUntilRespawn()
    {
        var t = new SegmentTracker();
        t.StartSegment(TimeSpan.FromSeconds(90));
        t.OnDeath();
        t.OnRespawn(TimeSpan.FromSeconds(100));
        Assert.Equal(TimeSpan.FromSeconds(100), t.CurrentArrival);   // anchored at respawn
        t.OnDeath();                                                 // die again at the same marker
        Assert.Null(t.CurrentArrival);                               // stale anchor gone: time bleeds again
        Assert.Equal(TimeSpan.FromSeconds(90), t.CurrentHotArrival); // gold prior input survives
        t.OnRespawn(TimeSpan.FromSeconds(130));
        Assert.Equal(TimeSpan.FromSeconds(130), t.CurrentArrival);   // fresh anchor
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter SegmentTrackerTests`
Expected: the new fact FAILS on the `Assert.Null` (stale arrival still present).

- [ ] **Step 3: Implement**

In `SegmentTracker.cs`, `OnDeath()`:

```csharp
    public void OnDeath()
    {
        if (!IsActive) return;
        DiedThisSegment = true;
        // Spec: on death the runner is assumed to respawn at the last touched
        // marker — the situation is cold from this moment even before respawn.
        CurrentVariant = Variant.Cold;
        // A previous respawn's cold arrival must not anchor THIS death: until
        // the new respawn the estimate has no anchor and time is bleeding,
        // exactly like a first death. The dropped observation loses nothing —
        // the new respawn would have overwritten it anyway (re-arrival
        // overwrites; the later arrival is the only one a min-merge keeps).
        open.Remove((CurrentMarker, Variant.Cold));
    }
```

- [ ] **Step 4: Full suite**

Run: `dotnet test test/LiveSplit.Reckoning.Tests`
Expected: PASS (no other behavior changes — calculator already handles null arrival).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine/SegmentTracker.cs test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs
git commit -m "fix(engine): every death clears the stale cold anchor so the estimate bleeds until respawn"
```

---

### Task 2: Hit measures true death cost (value-baseline) + docs

**Files:**
- Modify: `src/LiveSplit.Reckoning/Engine/DamageHit.cs`
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs` (two call-site argument changes)
- Modify: `docs/TESTING.md` (death bullets)
- Test: `test/LiveSplit.Reckoning.Tests/DamageHitTests.cs`

**Interfaces:**
- `DamageHit.OnDeath(TimeSpan? valueNow)` — baseline is the death-aware VALUE at the death instant. **Null valueNow → the hit does not activate** (no comparison data = nothing honest to show; previously null coalesced to a zero Sunk baseline).
- `DamageHit.Update(TimeSpan? valueNow, long nowMs)` — amount = valueNow − baseline (clamped ≥ 0), frozen one sample after `OnRespawn()` as today.
- Shell: `hit.OnDeath(lastComposed.Value)` in PollCore and `hit.Update(lastComposed.Value, …)` in Update replace the `.Sunk` arguments. Nothing else changes in the shell.

Why this baseline is the true cost: pre-death, the value V0 is what the run was headed for. The death re-anchors the estimate to "now + replay-from-respawn", which ticks 1:1 through the animation while V0 stays fixed — so the frozen amount = replay estimate + death→spawn downtime, independent of whether stock's value happened to be ticking too (Sunk-baselining cancelled the downtime whenever the runner was behind pace — live-observed as "-3.8 shown, ~10s real").

- [ ] **Step 1: Rewrite the DamageHit tests**

Replace the test file's facts with value-semantics equivalents (same helpers). Every existing scenario keeps an equivalent; the meaningful deltas are the baseline meaning and the null-at-death rule:

```csharp
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
```

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter DamageHitTests`
Expected: FAIL — `NullValueAtDeathDoesNotActivate` fails against current null→Zero coalescing, and the value-semantics facts fail against sunk-baselining.

- [ ] **Step 3: Implement**

In `DamageHit.cs`: rename the parameter/field meanings (`sunkNow` → `valueNow`, `baseline` = value at death), and change `OnDeath`:

```csharp
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
```

`Update(TimeSpan? valueNow, long nowMs)` keeps its exact structure (grow-unless-fading with clamp, pending-freeze capture, expiry); only the parameter name and doc wording change. Update the class doc comment to describe value-baselining.

In `ReckoningComponent.cs`: `hit.OnDeath(lastComposed.Sunk)` → `hit.OnDeath(lastComposed.Value)` and `hit.Update(lastComposed.Sunk, …)` → `hit.Update(lastComposed.Value, …)`. Nothing else.

- [ ] **Step 4: Full suite + Release build**

Run: `dotnet test test/LiveSplit.Reckoning.Tests && dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release`
Expected: all green, zero warnings.

- [ ] **Step 5: Update TESTING.md death bullets**

Amend the quick-play death bullets to the new expectations, keeping the current length discipline: the red hit appears at the death for every death (first or repeat), ticks 1:1 through the death animation (the estimate is bleeding — it also ticks on the row for repeat deaths now, same as first deaths), freezes just after respawn at the death's full cost (replay estimate + death→spawn downtime), then fades. The frozen number should roughly match a hand-count of seconds lost to the death.

- [ ] **Step 6: Commit**

```bash
git add -A src test docs/TESTING.md
git commit -m "fix(engine): hit measures true death cost via value baseline"
```
