# Priors and Hit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three fixes from Andrew's first live review of the Run Prediction rebase: comparison labels show the comparison's own name (no "Current Pace (…)" wrapper), the damage hit captures the post-respawn re-anchor jump before freezing (so repeat deaths reliably show red), and the no-learned-data fallback prices deaths with a gold-based prior anchored at the respawn (nonzero Sunk and a red hit on first-ever deaths, multi-checkpoint aware).

**Architecture:** All engine changes are pure and TDD'd. `DamageHit` gains a pending-freeze state (freeze on the first Update after respawn, not at the respawn event). `ReckoningCalculator` gains a gold-prior rung in the fallback chain using the current run's hot arrival at the marker: `remaining = gold − (hotArrival − segmentStart)`, anchored at the situation entry; `SegmentTracker` exposes that hot arrival. The shell changes are one call-site signature tweak (`hit.OnRespawn()` loses its argument).

**Tech Stack:** C# / net481, xUnit 2.9.2. Branch: `feature/run-prediction-rebase` (continues the existing branch — Andrew is mid-review; do not merge or rebase anything).

## Global Constraints

- Engine stays pure: nothing under `src/LiveSplit.Reckoning/Engine/` may reference LiveSplit or WRAM types, and no locks in Engine. Red-green TDD for all engine changes.
- Run tests: `dotnet test test/LiveSplit.Reckoning.Tests` (repo root). Release build must stay at zero warnings: `dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release`.
- No magic numbers without a justifying comment.
- NEVER commit or `git add` anything under `.github/`.
- Commits end with: Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>

---

### Task 1: Comparison labels show the comparison's own name

Andrew's live check confirmed our fallback label ("Current Pace (Best Split Times)") does not match what his stock build renders and is too long regardless. Deliberate deviation from the ported table: the fallback case shows the comparison's (short) name, nothing else. The special mappings stay ("Best Segments" → "Best Possible Time", "Worst Segments" → "Worst Possible Time", "Average Segments" → "Predicted Time", "Current Comparison"/"Personal Best" → "Current Pace").

**Files:**
- Modify: `src/LiveSplit.Reckoning/UI/Components/ComparisonNaming.cs`
- Test: `test/LiveSplit.Reckoning.Tests/ComparisonNamingTests.cs`

**Interfaces:**
- Consumes/Produces: same two methods, `GetDisplayedName(string)` and `GetAbbreviations(string)`; only the default-case behavior changes.

- [ ] **Step 1: Update the tests to pin the new fallback**

In `ComparisonNamingTests.cs`, replace the `CustomComparisonGetsCurrentPaceParenthetical` fact with:

```csharp
    [Fact]
    public void CustomComparisonShowsItsOwnName()
        => Assert.Equal("My Comp", ComparisonNaming.GetDisplayedName("My Comp"));

    [Fact]
    public void CustomComparisonAbbreviatesToItsOwnShortName()
        => Assert.Equal(new[] { "My Comp" }, ComparisonNaming.GetAbbreviations("My Comp"));
```

The five `[InlineData]` displayed-name mappings and the Best Segments abbreviation test stay unchanged.

- [ ] **Step 2: Run to verify the new facts fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ComparisonNamingTests`
Expected: 2 FAIL (old parenthetical behavior), rest pass.

- [ ] **Step 3: Change both default cases**

In `ComparisonNaming.cs`:
- `GetDisplayedName` default case: `_ => CompositeComparisons.GetShortComparisonName(comparison),`
- `GetAbbreviations` default case: `_ => new[] { CompositeComparisons.GetShortComparisonName(comparison) },`
- Update the class doc comment to note the deviation, e.g. append: `Deviation from stock (Andrew, 2026-07-31 live review): unmapped comparisons display their own name instead of "Current Pace (name)".`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ComparisonNamingTests`
Expected: PASS (8 facts).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/UI/Components/ComparisonNaming.cs test/LiveSplit.Reckoning.Tests/ComparisonNamingTests.cs
git commit -m "fix(ui): unmapped comparisons display their own name, not Current Pace (name)"
```

---

### Task 2: Damage hit freezes one sample after respawn

Live-observed bug: the amount freezes at the respawn *event*, but the estimate's re-anchor jump (`arrival + best`) is computed on the next Update — so repeat deaths freeze at 0.0 and the zero-suppression hides them. Fix: `OnRespawn()` marks a pending freeze; the next `Update` with a sunk sample captures the jump, then starts the fade.

**Files:**
- Modify: `src/LiveSplit.Reckoning/Engine/DamageHit.cs`
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs` (call site only: `hit.OnRespawn(clock.ElapsedMilliseconds)` → `hit.OnRespawn()`)
- Test: `test/LiveSplit.Reckoning.Tests/DamageHitTests.cs`

**Interfaces:**
- Produces: `OnRespawn()` (no argument — fade timing now comes from the Update that captures the jump). Everything else unchanged: `OnDeath(TimeSpan?)`, `Update(TimeSpan?, long)`, `Clear()`, `Visible`, `Amount`, `Alpha(long)`, `FadeDurationMs`.

- [ ] **Step 1: Rewrite the affected tests**

In `DamageHitTests.cs`, replace `FreezesAtRespawnThenFadesLinearly` and `ExpiresAfterFadeDuration` and add one new fact; the other four facts stay as-is except any `OnRespawn(nowMs)` call sites drop the argument:

```csharp
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
```

In `SecondDeathRestartsWithNewBaseline`, drop the argument from `OnRespawn` and keep its assertions — but note the second `Update` now happens after a fresh `OnDeath`, which must clear any pending freeze (the existing assertions already prove this if `OnDeath` resets the flag).

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter DamageHitTests`
Expected: FAIL — `OnRespawn()` has no zero-argument overload yet, and the new semantics aren't implemented.

- [ ] **Step 3: Implement**

Replace `OnRespawn` and `Update` in `DamageHit.cs` (add a `pendingFreeze` field; `OnDeath` must reset it):

```csharp
    private bool pendingFreeze;    // respawn seen: freeze on the next sample

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
```

In `ReckoningComponent.cs` PollCore, change `hit.OnRespawn(clock.ElapsedMilliseconds)` to `hit.OnRespawn()` (the respawn tick still calls it before `model.OnRespawn(elapsed)` — keep that order; the following `Update()` computes the jumped sunk and captures it).

- [ ] **Step 4: Run the full suite and Release build**

Run: `dotnet test test/LiveSplit.Reckoning.Tests && dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release`
Expected: all green, zero warnings.

- [ ] **Step 5: Commit**

```bash
git add -A src test
git commit -m "fix(engine): damage hit captures the post-respawn re-anchor jump before freezing"
```

---

### Task 3: Gold prior anchored at the situation for unlearned markers

Live-observed gap: with no learned data, the fallback ("segment gold from segment start") collapses to stock BPT, so Sunk ≡ 0 and no hit on first-ever deaths. Fix (Andrew's "hot as a prior"): when neither variant has data for the current marker, price the future as what remains of the segment gold from this marker — `remaining = gold − (hot arrival at this marker − segment start)` — anchored at the situation entry. Marker 0's hot arrival is the segment start, so this collapses to "replay the segment from the respawn" (`anchor + gold`). Multi-checkpoint segments get an honest per-marker prior from this run's own hot arrival. Stays flagged unlearned (gray).

**Files:**
- Modify: `src/LiveSplit.Reckoning/Engine/SegmentTracker.cs` (expose the hot arrival)
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningResult.cs` (new `BestSource.GoldPrior` member)
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningCalculator.cs` (new fallback rung + parameter)
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningModel.cs` (pass the hot arrival through)
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningCalculatorTests.cs` (new facts), `test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs` (one fact)

**Interfaces:**
- Produces:
  - `SegmentTracker.CurrentHotArrival` — `public TimeSpan?`: run-elapsed of this run's hot arrival at the current marker (marker 0 = segment start), null when none is open (unanchored resume).
  - `BestSource.GoldPrior` — new enum member (append after `HotBest`; nothing serializes this enum).
  - `ReckoningCalculator.PredictFinish(TimeSpan elapsed, TimeSpan segmentStartElapsed, TimeSpan? currentSegmentFullBest, bool diedThisSegment, int currentMarker, Variant currentVariant, TimeSpan? situationArrivalElapsed, TimeSpan? hotArrivalAtCurrentMarker, Func<int, Variant, TimeSpan?> markerBest)` — one new parameter, inserted before `markerBest`.
  - `ReckoningModel.Compute` signature unchanged (it passes `tracker.CurrentHotArrival` internally); the shell is untouched by this task.

- [ ] **Step 1: Write the failing tests**

Add to `ReckoningCalculatorTests.cs` (update every existing `PredictFinish` call site to pass `hotArrivalAtCurrentMarker: null` — with null the behavior must be exactly as before, which the existing facts then prove):

```csharp
    [Fact]
    public void FirstDeathAtMarkerZeroPricesReplayFromTheAnchor()
    {
        // No learned data at all; hot arrival at marker 0 IS the segment start.
        // Prior: replay the segment from the respawn -> anchor + gold.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(145), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(90),
            markerBest: NoBests);
        Assert.Equal(S(140 + 30), p.Finish);
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.GoldPrior, p.Source);
    }

    [Fact]
    public void GoldPriorSubtractsHotProgressAtLaterMarkers()
    {
        // Reached marker 2 hot at 110 (20s into a 30s-gold segment): 10s of gold
        // remains from there; anchored at the cold arrival.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(141), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 2, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(110),
            markerBest: NoBests);
        Assert.Equal(S(140 + 10), p.Finish);
        Assert.Equal(BestSource.GoldPrior, p.Source);
    }

    [Fact]
    public void SlowerThanGoldArrivalClampsThePriorRemainingToZero()
    {
        // Hot arrival 50s into a 30s gold: no future credit; finish = max(anchor, elapsed).
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(150), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(145), hotArrivalAtCurrentMarker: S(140),
            markerBest: NoBests);
        Assert.Equal(S(150), p.Finish);   // max(145 + 0, 150)
    }

    [Fact]
    public void PreRespawnGoldPriorAnchorsAtElapsedSoItTicks()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(143), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: null, hotArrivalAtCurrentMarker: S(90),
            markerBest: NoBests);
        Assert.Equal(S(143 + 30), p.Finish);   // anchor = elapsed until respawn
    }

    [Fact]
    public void NoHotArrivalFallsBackToSegmentStartGold()
    {
        // Unanchored resume (undo/skip): old last-rung behavior, StandardBpt source.
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), hotArrivalAtCurrentMarker: null,
            markerBest: NoBests);
        Assert.Equal(S(140), p.Finish);   // max(90+30, 140)
        Assert.Equal(BestSource.StandardBpt, p.Source);
    }

    [Fact]
    public void LearnedDataStillBeatsTheGoldPrior()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(141), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), hotArrivalAtCurrentMarker: S(100),
            markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(140 + 22), p.Finish);
        Assert.Equal(BestSource.ColdBest, p.Source);
        Assert.False(p.Unlearned);
    }
```

Add to `SegmentTrackerTests.cs`:

```csharp
    [Fact]
    public void CurrentHotArrivalTracksTheMarkersHotOpen()
    {
        var t = new SegmentTracker();
        t.StartSegment(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(90), t.CurrentHotArrival);   // marker 0 = segment start
        t.OnCheckpoint(TimeSpan.FromSeconds(110));
        Assert.Equal(TimeSpan.FromSeconds(110), t.CurrentHotArrival);
        t.OnDeath();
        Assert.Equal(TimeSpan.FromSeconds(110), t.CurrentHotArrival);  // hot arrival survives death
        t.ResumeSegmentUnanchored();
        Assert.Null(t.CurrentHotArrival);                              // unanchored: unknown
    }
```

- [ ] **Step 2: Run to verify failures**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter "ReckoningCalculatorTests|SegmentTrackerTests"`
Expected: FAIL — new parameter and members don't exist.

- [ ] **Step 3: Implement**

`SegmentTracker.cs` — add below `CurrentArrival`:

```csharp
    /// <summary>Run-elapsed of this run's HOT arrival at the current marker
    /// (marker 0's hot arrival is the segment start). Null when no hot
    /// observation is open — an unanchored resume after undo/skip.</summary>
    public TimeSpan? CurrentHotArrival =>
        open.TryGetValue((CurrentMarker, Variant.Hot), out var t) ? t : (TimeSpan?)null;
```

`ReckoningResult.cs` — append `GoldPrior,` to `BestSource` with a doc line: `/// <summary>No learned data: segment gold minus this run's hot progress to the marker, anchored at the situation.</summary>`

`ReckoningCalculator.cs` — add the parameter `TimeSpan? hotArrivalAtCurrentMarker` before `markerBest`, and replace the last-rung block with:

```csharp
        // Gold prior (no learned data for this marker): the segment gold is the
        // hot prior; what remains of it from this marker is gold minus the hot
        // time already spent reaching the marker this run. Marker 0's hot
        // arrival is the segment start, so this collapses to "replay the
        // segment from the anchor". Slower-than-gold arrivals clamp to zero:
        // the prior never grants future credit for time already lost.
        if (currentSegmentFullBest is TimeSpan gold && hotArrivalAtCurrentMarker is TimeSpan hotArrival)
        {
            TimeSpan remaining = gold - (hotArrival - segmentStartElapsed);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            return new SituationPrediction(Max(anchor + remaining, elapsed), true, BestSource.GoldPrior);
        }

        // Unanchored resume (undo/skip left the hot arrival unknowable): the
        // segment gold from split start remains the only honest floor.
        if (currentSegmentFullBest is TimeSpan fb)
            return new SituationPrediction(Max(segmentStartElapsed + fb, elapsed), true, BestSource.StandardBpt);

        return new SituationPrediction(null, true, BestSource.StandardBpt);
```

`ReckoningModel.cs` — in `Compute`, pass `tracker.CurrentHotArrival` as the new argument.

- [ ] **Step 4: Run the full suite and Release build**

Run: `dotnet test test/LiveSplit.Reckoning.Tests && dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release`
Expected: all green (existing facts prove null-hot-arrival behavior is unchanged), zero warnings.

- [ ] **Step 5: Update TESTING.md's death expectations**

In `docs/TESTING.md`'s quick play list, amend the death bullets: a first-ever death now shows a gray estimate priced off the segment gold ("replay from respawn") AND a red hit that ticks while dead (including sitting out a timeout), freezes just after respawn, then fades; repeat deaths always re-show the hit. Keep it to the existing bullets' length discipline.

- [ ] **Step 6: Commit**

```bash
git add -A src test docs/TESTING.md
git commit -m "feat(engine): gold prior anchored at the situation for unlearned markers"
```
