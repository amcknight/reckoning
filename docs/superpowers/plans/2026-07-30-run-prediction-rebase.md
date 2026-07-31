# Run Prediction Rebase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild Reckoning's display on LiveSplit's stock Run Prediction semantics (identical numbers, settings, and look for any comparison), inject death-awareness as a smarter live delta, replace the Sunk row with a fading red damage-hit overlay, and persist the sidecar only when the user saves their splits.

**Architecture:** The engine's death-aware machinery shrinks to one job: predict the *current split's* finish time after a death (`ReckoningCalculator.PredictFinish`, null when deathless). A new pure `PredictionMath.Compose` mirrors stock Run Prediction's formula (`max(lastDelta, liveDelta) + comparisonFinal`) with our predicted finish substituted into the live delta. The shell becomes a thin port of `RunPrediction.cs` wrapping LiveSplit's public `InfoTimeComponent`, plus two overlays (damage hit, status dot). Sidecar saves move from on-split to a `FileSystemWatcher` on the `.lss` file, so learned data shadows the splits file exactly like golds do.

**Tech Stack:** C# / net481 / WinForms, xUnit 2.9.2, LiveSplit.Core public API (`LiveSplitStateHelper`, `InfoTimeComponent`, `SplitTimeFormatter`, `TimeAccuracy`, `SettingsHelper`).

## Global Constraints

- Engine stays pure: nothing under `src/LiveSplit.Reckoning/Engine/` may reference LiveSplit or WRAM types. TDD everything in Engine.
- Work on branch `feature/run-prediction-rebase`; Andrew reviews the diff against main and merges himself.
- Stock parity is the spec: where this plan says "port from RunPrediction.cs", copy the semantics *exactly* from `c:\Users\thedo\git\LiveSplit\components\LiveSplit.RunPrediction\src\LiveSplit.RunPrediction\UI\Components\RunPrediction.cs` (LiveSplit is MIT-licensed; add a one-line attribution comment at each port site: `// Ported from LiveSplit's RunPrediction component (MIT).`).
- No magic numbers without a justifying comment (spinlab rule). The damage-hit constants (2500 ms fade, damage red) each carry one.
- Read paths with the Read tool directly (Windows host: Glob/find can fail silently on separators).
- Run tests with: `dotnet test test/LiveSplit.Reckoning.Tests` (from repo root `c:\Users\thedo\git\reckoning`).
- All commits end with the standard Co-Authored-By line per harness rules.
- NEVER commit or `git add` anything under `.github/` (harness policy blocks it; the untracked `release.yml` there is Andrew's to commit).

---

### Task 1: Engine — `PredictFinish` (calculator predicts the current split's finish; null when deathless)

The calculator currently computes a full DR-BPT and Sunk from segment-best sums. The comparison now supplies everything beyond the current split, so the calculator's only job is: *given a death, when will this split finish?*

**Files:**
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningResult.cs` (replace `ReckoningResult` with `SituationPrediction`; keep `BestSource`)
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningCalculator.cs`
- Modify: `src/LiveSplit.Reckoning/Engine/ReckoningModel.cs` (Compute signature)
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningCalculatorTests.cs` (rewrite)
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningModelTests.cs` (update Compute call sites only)

**Interfaces:**
- Consumes: existing `Variant`, `SegmentTracker` state accessors (unchanged).
- Produces:
  - `public sealed record SituationPrediction(TimeSpan? Finish, bool Unlearned, BestSource Source);`
  - `public static SituationPrediction ReckoningCalculator.PredictFinish(TimeSpan elapsed, TimeSpan segmentStartElapsed, TimeSpan? currentSegmentFullBest, bool diedThisSegment, int currentMarker, Variant currentVariant, TimeSpan? situationArrivalElapsed, Func<int, Variant, TimeSpan?> markerBest)` — returns **null** when `diedThisSegment` is false ("no adjustment; stock formula applies untouched"). `Finish == null` inside a non-null result means "died but no estimate available" (display falls back to stock value, flagged unlearned).
  - `public SituationPrediction ReckoningModel.Compute(TimeSpan elapsed, TimeSpan segmentStartElapsed, TimeSpan? currentSegmentFullBest)` — may return null.

- [ ] **Step 1: Replace the result record**

In `ReckoningResult.cs`, delete the `ReckoningResult` record and add (keep the `BestSource` enum and its doc comment as-is):

```csharp
/// <summary>Death-aware prediction of the current split's finish time
/// (run-elapsed). Null Finish: died, but no estimate is available — the
/// display falls back to the stock value, flagged unlearned.</summary>
public sealed record SituationPrediction(TimeSpan? Finish, bool Unlearned, BestSource Source);
```

- [ ] **Step 2: Rewrite the calculator tests to pin the new contract**

Replace the entire body of `ReckoningCalculatorTests.cs` with (keep the existing `S`/`NoBests`/`Bests` helpers verbatim — they appear at the top of the current file):

```csharp
using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningCalculatorTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static Func<int, Variant, TimeSpan?> NoBests => (_, _) => null;

    private static Func<int, Variant, TimeSpan?> Bests(params (int Marker, Variant V, double Secs)[] entries) =>
        (m, v) =>
        {
            foreach (var e in entries)
                if (e.Marker == m && e.V == v) return S(e.Secs);
            return null;
        };

    [Fact]
    public void DeathlessReturnsNull_StockFormulaApplies()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(100), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: false, currentMarker: 1, currentVariant: Variant.Hot,
            situationArrivalElapsed: null, markerBest: Bests((1, Variant.Hot, 10)));
        Assert.Null(p);
    }

    [Fact]
    public void AfterDeathUsesLearnedBestAnchoredAtArrival()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(150), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(140 + 22), p.Finish);   // anchored at arrival, not at elapsed=150
        Assert.False(p.Unlearned);
        Assert.Equal(BestSource.ColdBest, p.Source);
    }

    [Fact]
    public void AfterDeathClampsToElapsedWhenBehindLearnedPace()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(170), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(170), p.Finish);   // max(140+22, 170)
    }

    [Fact]
    public void PreRespawnAnchorsAtElapsedSoEstimateRises()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(145), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: null, markerBest: Bests((1, Variant.Cold, 22)));
        Assert.Equal(S(145 + 22), p.Finish);   // no anchor yet: time is bleeding
    }

    [Fact]
    public void FallsBackToOtherVariantFlaggedUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 1, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(140), markerBest: Bests((1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 18), p.Finish);
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.HotBest, p.Source);
    }

    [Fact]
    public void FallsBackToSegmentGoldWhenNoMarkerData()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: S(30),
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), markerBest: NoBests);
        Assert.Equal(S(140), p.Finish);   // max(90+30, 140) = elapsed
        Assert.True(p.Unlearned);
        Assert.Equal(BestSource.StandardBpt, p.Source);
    }

    [Fact]
    public void NoDataAtAllYieldsNullFinishUnlearned()
    {
        var p = ReckoningCalculator.PredictFinish(
            elapsed: S(140), segmentStartElapsed: S(90), currentSegmentFullBest: null,
            diedThisSegment: true, currentMarker: 0, currentVariant: Variant.Cold,
            situationArrivalElapsed: S(135), markerBest: NoBests);
        Assert.NotNull(p);
        Assert.Null(p.Finish);
        Assert.True(p.Unlearned);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ReckoningCalculatorTests`
Expected: FAIL — compile errors (`PredictFinish` and `SituationPrediction` don't exist yet).

- [ ] **Step 4: Rewrite the calculator**

Replace the body of `ReckoningCalculator.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.Engine;

public static class ReckoningCalculator
{
    /// <summary>Predicts when the current split will finish, given death state.
    /// Returns null while deathless: no adjustment — the stock Run Prediction
    /// formula applies untouched, which makes Sunk exactly zero by construction.</summary>
    public static SituationPrediction PredictFinish(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        bool diedThisSegment,
        int currentMarker,
        Variant currentVariant,
        TimeSpan? situationArrivalElapsed,
        Func<int, Variant, TimeSpan?> markerBest)
    {
        if (!diedThisSegment) return null;

        // The situation's best is anchored at the moment the situation was entered,
        // not at "now" — otherwise the estimate ramps upward during normal play.
        // Before respawn there is no anchor yet: time is genuinely still bleeding,
        // so `elapsed` is the honest anchor and the estimate rises until respawn.
        TimeSpan anchor = situationArrivalElapsed ?? elapsed;
        Variant other = currentVariant == Variant.Cold ? Variant.Hot : Variant.Cold;

        if (markerBest(currentMarker, currentVariant) is TimeSpan preferred)
            return new SituationPrediction(Max(anchor + preferred, elapsed), false, ToSource(currentVariant));

        // Wrong-variant data beats no data; flagged unlearned (spec fallback chain).
        if (markerBest(currentMarker, other) is TimeSpan fallback)
            return new SituationPrediction(Max(anchor + fallback, elapsed), true, ToSource(other));

        // Last rung: the segment gold from split start (can't finish in the past).
        if (currentSegmentFullBest is TimeSpan fb)
            return new SituationPrediction(Max(segmentStartElapsed + fb, elapsed), true, BestSource.StandardBpt);

        return new SituationPrediction(null, true, BestSource.StandardBpt);
    }

    private static BestSource ToSource(Variant v) => v == Variant.Cold ? BestSource.ColdBest : BestSource.HotBest;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;
}
```

- [ ] **Step 5: Update `ReckoningModel.Compute`**

In `ReckoningModel.cs`, replace the `Compute` method:

```csharp
    /// <summary>Death-aware prediction of the current split's finish, or null
    /// while deathless (stock formula applies untouched).</summary>
    public SituationPrediction Compute(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest)
    {
        int segment = CurrentSegmentIndex;
        return ReckoningCalculator.PredictFinish(
            elapsed, segmentStartElapsed, currentSegmentFullBest,
            tracker.DiedThisSegment, tracker.CurrentMarker, tracker.CurrentVariant, tracker.CurrentArrival,
            (marker, variant) => store.TryGetBest(segment, marker, variant, out var b) ? b : null);
    }
```

- [ ] **Step 6: Fix `ReckoningModelTests` call sites**

`ReckoningModelTests.cs` calls `model.Compute(elapsed, segmentStart, fullBest, remainingSum)` in several tests and asserts on `DrBpt`/`Sunk`. Update each call site: drop the fourth argument, and re-express assertions against the new contract — deathless paths assert `Assert.Null(result)`; post-death paths assert on `result.Finish`/`result.Unlearned`/`result.Source` (the expected Finish is the old expected DrBpt minus the old remaining-sum argument). Do not delete any test scenario — every existing scenario keeps an equivalent assertion.

`ReckoningComponent.cs` also no longer compiles (it consumes the old signature). Apply this minimal bridge in `ComputeNow` (Task 5 rewrites it properly) — delete the `remaining` loop and change the return line to:

```csharp
        return model.Compute(elapsed, segmentStart, fullBest);
```

and change the field `lastResult` plus its uses in `Update`/`DrawGeneral` to compile against `SituationPrediction` (temporary: `cache["reckoning"] = lastResult?.Finish?.ToString() ?? "—"; cache["sunk"] = ""; cache["unlearned"] = lastResult?.Unlearned ?? false;` and pass those strings to `DrawRow`). This is scaffolding that Task 5 deletes; it just keeps the build green at the task boundary.

- [ ] **Step 7: Run the full suite; fix only mechanical breaks**

Run: `dotnet test test/LiveSplit.Reckoning.Tests`
Expected: PASS (87-ish tests; count may shift slightly with the rewritten calculator file).

- [ ] **Step 8: Commit**

```bash
git add -A src test
git commit -m "refactor(engine): calculator predicts current-split finish; comparison supplies the rest"
```

---

### Task 2: Engine — `PredictionMath.Compose` (stock Run Prediction formula, pure)

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/PredictionMath.cs`
- Test: `test/LiveSplit.Reckoning.Tests/PredictionMathTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (pure TimeSpans).
- Produces:
  - `public readonly record struct ComposedPrediction(TimeSpan? StockValue, TimeSpan? Value, TimeSpan? Sunk);`
  - `public static ComposedPrediction PredictionMath.Compose(TimeSpan? lastDelta, TimeSpan elapsed, TimeSpan? comparisonAtCurrentSplit, TimeSpan? comparisonFinal, TimeSpan? predictedFinish)`
  - Semantics: `StockValue` is exactly stock Run Prediction's running-phase value; `Value` is the death-aware version (equal to `StockValue` when `predictedFinish` is null); `Sunk = Value - StockValue` (zero when deathless, null when values are null).

- [ ] **Step 1: Write the failing tests**

Create `PredictionMathTests.cs`:

```csharp
using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class PredictionMathTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void DeathlessMatchesStockFormula_AheadOfPace()
    {
        // Stock: delta = max(lastDelta, liveDelta); value = delta + comparisonFinal.
        // liveDelta (100-120=-20) < lastDelta (-5) → locked delta wins.
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(100),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(595), r.StockValue);
        Assert.Equal(S(595), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void DeathlessLiveDeltaOverridesWhenLosingTime()
    {
        // liveDelta (130-120=+10) > lastDelta (-5) → live wins (stock behavior).
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(130),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(610), r.StockValue);
        Assert.Equal(S(610), r.Value);
    }

    [Fact]
    public void NullLastDeltaCoalescesToZero()
    {
        var r = PredictionMath.Compose(
            lastDelta: null, elapsed: S(100),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: null);
        Assert.Equal(S(600), r.StockValue);   // max(0, -20) = 0
    }

    [Fact]
    public void DeathRaisesValueAndSunkIsTheDifference()
    {
        // Death-aware finish 145 vs comparison split 120 → drLiveDelta +25
        // beats both lastDelta (-5) and liveDelta (+10).
        var r = PredictionMath.Compose(
            lastDelta: S(-5), elapsed: S(130),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: S(145));
        Assert.Equal(S(610), r.StockValue);
        Assert.Equal(S(625), r.Value);
        Assert.Equal(S(15), r.Sunk);
    }

    [Fact]
    public void DeathStillAheadOfLockedDeltaShowsNoLoss()
    {
        // Died, but predicted finish (118) still beats the comparison split (120):
        // drLiveDelta (-2) < lastDelta (+3) → locked delta rules both values.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: S(120), comparisonFinal: S(600),
            predictedFinish: S(118));
        Assert.Equal(r.StockValue, r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void NullComparisonFinalYieldsNulls()
    {
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: S(120), comparisonFinal: null,
            predictedFinish: S(118));
        Assert.Null(r.StockValue);
        Assert.Null(r.Value);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void NullComparisonAtCurrentSplitFallsBackToLockedDelta()
    {
        // Stock: liveDelta null → the > comparison is false → locked delta kept.
        // Death-aware side has no split target either → identical to stock.
        var r = PredictionMath.Compose(
            lastDelta: S(3), elapsed: S(110),
            comparisonAtCurrentSplit: null, comparisonFinal: S(600),
            predictedFinish: S(145));
        Assert.Equal(S(603), r.StockValue);
        Assert.Equal(S(603), r.Value);
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter PredictionMathTests`
Expected: FAIL — `PredictionMath` not defined.

- [ ] **Step 3: Implement**

Create `PredictionMath.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.Engine;

public readonly record struct ComposedPrediction(TimeSpan? StockValue, TimeSpan? Value, TimeSpan? Sunk);

/// <summary>The stock Run Prediction running-phase formula, with an optional
/// death-aware substitute for the live delta.
/// Ported from LiveSplit's RunPrediction component (MIT).</summary>
public static class PredictionMath
{
    public static ComposedPrediction Compose(
        TimeSpan? lastDelta,
        TimeSpan elapsed,
        TimeSpan? comparisonAtCurrentSplit,
        TimeSpan? comparisonFinal,
        TimeSpan? predictedFinish)
    {
        TimeSpan locked = lastDelta ?? TimeSpan.Zero;   // stock coalesces null to zero

        TimeSpan? liveDelta = comparisonAtCurrentSplit is TimeSpan c ? elapsed - c : (TimeSpan?)null;
        TimeSpan stockDelta = liveDelta is TimeSpan ld && ld > locked ? ld : locked;
        TimeSpan? stockValue = comparisonFinal is TimeSpan f ? stockDelta + f : (TimeSpan?)null;

        if (predictedFinish is not TimeSpan pf || comparisonAtCurrentSplit is not TimeSpan cc)
            return new ComposedPrediction(stockValue, stockValue,
                stockValue is null ? null : TimeSpan.Zero);

        TimeSpan drLive = pf - cc;
        TimeSpan drDelta = drLive > locked ? drLive : locked;
        TimeSpan? value = comparisonFinal is TimeSpan f2 ? drDelta + f2 : (TimeSpan?)null;
        TimeSpan? sunk = value is TimeSpan v && stockValue is TimeSpan s ? v - s : (TimeSpan?)null;
        return new ComposedPrediction(stockValue, value, sunk);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter PredictionMathTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine/PredictionMath.cs test/LiveSplit.Reckoning.Tests/PredictionMathTests.cs
git commit -m "feat(engine): pure stock Run Prediction formula with death-aware live delta"
```

---

### Task 3: Engine — `DamageHit` state machine + hit formatting

The Sunk row is replaced by a transient overlay: on death a red amount appears, grows during the death animation, freezes at respawn, then fades out. Pure state machine; the shell feeds it events and a monotonic clock.

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/DamageHit.cs`
- Modify: `src/LiveSplit.Reckoning/UI/TimeText.cs` (add `FormatHit`; leave the rest for Task 5's cleanup)
- Test: `test/LiveSplit.Reckoning.Tests/DamageHitTests.cs`
- Test: `test/LiveSplit.Reckoning.Tests/TimeTextTests.cs` (add FormatHit cases)

**Interfaces:**
- Consumes: `Sunk` values from `PredictionMath.Compose` (Task 2).
- Produces:
  - `public sealed class DamageHit` with: `const long FadeDurationMs = 2500`; `void OnDeath(TimeSpan? sunkNow)`; `void OnRespawn(long nowMs)`; `void Update(TimeSpan? sunkNow, long nowMs)`; `void Clear()`; `bool Visible { get; }`; `TimeSpan Amount { get; }`; `int Alpha(long nowMs)` (255 solid → 0 gone).
  - `internal static string TimeText.FormatHit(TimeSpan amount)` → `"-22.4"`, `"-1:02.4"` (fixed tenths; always a leading minus — damage convention).

- [ ] **Step 1: Write the failing tests**

Create `DamageHitTests.cs`:

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
```

Add to `TimeTextTests.cs`:

```csharp
    [Fact]
    public void FormatHitUnderAMinuteIsSecondsWithTenths()
        => Assert.Equal("-22.4", TimeText.FormatHit(TimeSpan.FromSeconds(22.45)));

    [Fact]
    public void FormatHitOverAMinuteIncludesMinutes()
        => Assert.Equal("-1:02.4", TimeText.FormatHit(TimeSpan.FromSeconds(62.45)));

    [Fact]
    public void FormatHitZeroIsStillNegativeByConvention()
        => Assert.Equal("-0.0", TimeText.FormatHit(TimeSpan.Zero));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter "DamageHitTests|TimeTextTests"`
Expected: FAIL — `DamageHit` and `FormatHit` not defined.

- [ ] **Step 3: Implement `DamageHit`**

Create `DamageHit.cs`:

```csharp
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
```

Add to `TimeText.cs` (below `FormatSunk`):

```csharp
    /// <summary>Damage-number format: always a leading minus (time lost), fixed
    /// tenths — one glanceable decimal, like an HP hit.</summary>
    public static string FormatHit(TimeSpan amount)
    {
        var t = amount.Duration();
        int tenths = t.Milliseconds / 100;
        return t.TotalSeconds < 60
            ? $"-{(long)t.TotalSeconds}.{tenths}"
            : $"-{(long)t.TotalMinutes}:{t.Seconds:00}.{tenths}";
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter "DamageHitTests|TimeTextTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine/DamageHit.cs src/LiveSplit.Reckoning/UI/TimeText.cs test/LiveSplit.Reckoning.Tests/DamageHitTests.cs test/LiveSplit.Reckoning.Tests/TimeTextTests.cs
git commit -m "feat(engine): damage-hit state machine and hit formatting"
```

---

### Task 4: UI — `ComparisonNaming` (stock label tables)

**Files:**
- Create: `src/LiveSplit.Reckoning/UI/Components/ComparisonNaming.cs`
- Test: `test/LiveSplit.Reckoning.Tests/ComparisonNamingTests.cs`

**Interfaces:**
- Consumes: `LiveSplit.Model.Comparisons.CompositeComparisons.GetShortComparisonName` (LiveSplit.Core; the test project already references it).
- Produces:
  - `internal static string ComparisonNaming.GetDisplayedName(string comparison)`
  - `internal static string[] ComparisonNaming.GetAbbreviations(string comparison)`

- [ ] **Step 1: Write the failing tests**

Create `ComparisonNamingTests.cs`:

```csharp
using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ComparisonNamingTests
{
    [Theory]
    [InlineData("Current Comparison", "Current Pace")]
    [InlineData("Personal Best", "Current Pace")]
    [InlineData("Best Segments", "Best Possible Time")]
    [InlineData("Worst Segments", "Worst Possible Time")]
    [InlineData("Average Segments", "Predicted Time")]
    public void DisplayedNamesMatchStockRunPrediction(string comparison, string expected)
        => Assert.Equal(expected, ComparisonNaming.GetDisplayedName(comparison));

    [Fact]
    public void CustomComparisonGetsCurrentPaceParenthetical()
        => Assert.Equal("Current Pace (My Comp)", ComparisonNaming.GetDisplayedName("My Comp"));

    [Fact]
    public void BestSegmentsAbbreviationsMatchStock()
        => Assert.Equal(new[] { "Best Poss. Time", "Best Time", "BPT" },
            ComparisonNaming.GetAbbreviations("Best Segments"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ComparisonNamingTests`
Expected: FAIL — `ComparisonNaming` not defined.

- [ ] **Step 3: Implement — port the tables verbatim**

Read `c:\Users\thedo\git\LiveSplit\components\LiveSplit.RunPrediction\src\LiveSplit.RunPrediction\UI\Components\RunPrediction.cs` lines 122-171 (`GetDisplayedName` and `SetAlternateText`) and port both switch tables into:

```csharp
using LiveSplit.Model.Comparisons;

namespace LiveSplit.UI.Components;

/// <summary>Label tables ported from LiveSplit's RunPrediction component (MIT)
/// so Reckoning presents identically for every comparison.</summary>
internal static class ComparisonNaming
{
    public static string GetDisplayedName(string comparison) => comparison switch
    {
        "Current Comparison" => "Current Pace",
        Run.PersonalBestComparisonName => "Current Pace",
        BestSegmentsComparisonGenerator.ComparisonName => "Best Possible Time",
        WorstSegmentsComparisonGenerator.ComparisonName => "Worst Possible Time",
        AverageSegmentsComparisonGenerator.ComparisonName => "Predicted Time",
        _ => $"Current Pace ({CompositeComparisons.GetShortComparisonName(comparison)})",
    };

    public static string[] GetAbbreviations(string comparison) => comparison switch
    {
        BestSegmentsComparisonGenerator.ComparisonName => new[] { "Best Poss. Time", "Best Time", "BPT" },
        WorstSegmentsComparisonGenerator.ComparisonName => new[] { "Worst Poss. Time", "Worst Time" },
        AverageSegmentsComparisonGenerator.ComparisonName => new[] { "Pred. Time" },
        "Current Comparison" => new[] { "Cur. Pace", "Pace" },
        Run.PersonalBestComparisonName => new[] { "Cur. Pace", "Pace" },
        _ => new[] { "Current Pace", "Cur. Pace", "Pace" },
    };
}
```

(`Run` here is `LiveSplit.Model.Run` — add `using LiveSplit.Model;`. Verify the ported strings against the source file; the source is authoritative over this snippet.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ComparisonNamingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/UI/Components/ComparisonNaming.cs test/LiveSplit.Reckoning.Tests/ComparisonNamingTests.cs
git commit -m "feat(ui): stock Run Prediction label tables"
```

---

### Task 5: Settings clone (stock Run Prediction settings + status dot)

Settings come before the component rewrite because the component consumes them. Mirror stock's fields, XML keys, and defaults exactly, plus our `ShowStatusDot`.

**Files:**
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponentSettings.cs` (full rewrite)
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningSettingsTests.cs` (rewrite roundtrip tests)

**Interfaces:**
- Consumes: `LiveSplit.UI.SettingsHelper`, `LiveSplit.TimeFormatters.TimeAccuracy`, `LiveSplit.UI.GradientType`, `LiveSplit.Model.LiveSplitState`.
- Produces (component reads these in Task 6): `string Comparison` (default `"Current Comparison"`), `bool OverrideTextColor` / `Color TextColor`, `bool OverrideTimeColor` / `Color TimeColor`, `Color BackgroundColor` / `Color BackgroundColor2` / `GradientType BackgroundGradient`, `TimeAccuracy Accuracy` (default `Seconds` — stock's default), `bool Display2Rows` (default false), `bool ShowStatusDot` (default true), `LiveSplitState CurrentState { get; set; }`.

- [ ] **Step 1: Rewrite the roundtrip tests**

Replace `ReckoningSettingsTests.cs` contents with a serialization roundtrip pinning the stock XML keys:

```csharp
using System.Drawing;
using System.Xml;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningSettingsTests
{
    private static ReckoningComponentSettings Roundtrip(ReckoningComponentSettings s)
    {
        var doc = new XmlDocument();
        var node = s.GetSettings(doc);
        var fresh = new ReckoningComponentSettings();
        fresh.SetSettings(node);
        return fresh;
    }

    [Fact]
    public void DefaultsMatchStockRunPrediction()
    {
        var s = new ReckoningComponentSettings();
        Assert.Equal("Current Comparison", s.Comparison);
        Assert.False(s.OverrideTextColor);
        Assert.False(s.OverrideTimeColor);
        Assert.Equal(TimeAccuracy.Seconds, s.Accuracy);
        Assert.False(s.Display2Rows);
        Assert.Equal(GradientType.Plain, s.BackgroundGradient);
        Assert.True(s.ShowStatusDot);
    }

    [Fact]
    public void AllFieldsSurviveRoundtrip()
    {
        var s = new ReckoningComponentSettings
        {
            Comparison = "Best Segments",
            OverrideTextColor = true,
            TextColor = Color.FromArgb(1, 2, 3),
            OverrideTimeColor = true,
            TimeColor = Color.FromArgb(4, 5, 6),
            BackgroundColor = Color.FromArgb(7, 8, 9),
            BackgroundColor2 = Color.FromArgb(10, 11, 12),
            BackgroundGradient = GradientType.Vertical,
            Accuracy = TimeAccuracy.Hundredths,
            Display2Rows = true,
            ShowStatusDot = false,
        };
        var r = Roundtrip(s);
        Assert.Equal("Best Segments", r.Comparison);
        Assert.True(r.OverrideTextColor);
        Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), r.TextColor.ToArgb());
        Assert.True(r.OverrideTimeColor);
        Assert.Equal(Color.FromArgb(4, 5, 6).ToArgb(), r.TimeColor.ToArgb());
        Assert.Equal(Color.FromArgb(7, 8, 9).ToArgb(), r.BackgroundColor.ToArgb());
        Assert.Equal(Color.FromArgb(10, 11, 12).ToArgb(), r.BackgroundColor2.ToArgb());
        Assert.Equal(GradientType.Vertical, r.BackgroundGradient);
        Assert.Equal(TimeAccuracy.Hundredths, r.Accuracy);
        Assert.True(r.Display2Rows);
        Assert.False(r.ShowStatusDot);
    }

    [Fact]
    public void StockXmlKeysAreUsed()
    {
        var doc = new XmlDocument();
        var node = new ReckoningComponentSettings().GetSettings(doc);
        foreach (var key in new[] { "Comparison", "OverrideTextColor", "TextColor",
            "OverrideTimeColor", "TimeColor", "BackgroundColor", "BackgroundColor2",
            "BackgroundGradient", "Accuracy", "Display2Rows", "ShowStatusDot" })
            Assert.NotNull(node[key]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter ReckoningSettingsTests`
Expected: FAIL — properties don't exist yet.

- [ ] **Step 3: Rewrite the settings control**

Before writing, Read `c:\Users\thedo\git\LiveSplit\components\LiveSplit.RunPrediction\src\LiveSplit.RunPrediction\UI\Components\RunPredictionSettings.cs` for the property patterns, `SettingsHelper` usage, comparison-combo population (lines 89-95), and accuracy radio logic (lines 137-155). Then rewrite `ReckoningComponentSettings.cs` as a programmatic-layout UserControl (no designer file — keep the repo's existing style):

Properties and serialization (exact shape):

```csharp
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponentSettings : UserControl
{
    public string Comparison { get; set; } = "Current Comparison";
    public bool OverrideTextColor { get; set; }
    public Color TextColor { get; set; } = Color.FromArgb(255, 255, 255);
    public bool OverrideTimeColor { get; set; }
    public Color TimeColor { get; set; } = Color.FromArgb(255, 255, 255);
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public Color BackgroundColor2 { get; set; } = Color.Transparent;
    public GradientType BackgroundGradient { get; set; } = GradientType.Plain;
    public TimeAccuracy Accuracy { get; set; } = TimeAccuracy.Seconds;
    public bool Display2Rows { get; set; }
    public bool ShowStatusDot { get; set; } = true;
    public LiveSplitState CurrentState { get; set; }
    ...
```

UI controls, stacked programmatically like the current file: comparison `ComboBox` (DropDownList; on Load populate with `"Current Comparison"` plus `CurrentState.Run.Comparisons` excluding `BestSplitTimesComparisonGenerator.ComparisonName` and `NoneComparisonGenerator.ComparisonName` — guard `CurrentState != null` for the unit tests), two checkbox+button pairs for text/time color override (wire buttons through `SettingsHelper.ColorButtonClick(button, this)` and enable them only when their checkbox is checked), background color buttons + gradient `ComboBox` over `Enum.GetNames(typeof(GradientType))`, accuracy `ComboBox` with Seconds/Tenths/Hundredths/Milliseconds mapped to `TimeAccuracy`, `Display2Rows` checkbox, `ShowStatusDot` checkbox.

Serialization — mirror stock keys, plus ours; parse with `SettingsHelper`:

```csharp
    public XmlNode GetSettings(XmlDocument document)
    {
        var parent = document.CreateElement("Settings");
        SettingsHelper.CreateSetting(document, parent, "Version", "2");
        SettingsHelper.CreateSetting(document, parent, "Comparison", Comparison);
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor);
        SettingsHelper.CreateSetting(document, parent, "TextColor", TextColor);
        SettingsHelper.CreateSetting(document, parent, "OverrideTimeColor", OverrideTimeColor);
        SettingsHelper.CreateSetting(document, parent, "TimeColor", TimeColor);
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor);
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2);
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient);
        SettingsHelper.CreateSetting(document, parent, "Accuracy", Accuracy);
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows);
        SettingsHelper.CreateSetting(document, parent, "ShowStatusDot", ShowStatusDot);
        return parent;
    }

    public void SetSettings(XmlNode settings)
    {
        Comparison = SettingsHelper.ParseString(settings["Comparison"], "Current Comparison");
        OverrideTextColor = SettingsHelper.ParseBool(settings["OverrideTextColor"], false);
        TextColor = SettingsHelper.ParseColor(settings["TextColor"], Color.FromArgb(255, 255, 255));
        OverrideTimeColor = SettingsHelper.ParseBool(settings["OverrideTimeColor"], false);
        TimeColor = SettingsHelper.ParseColor(settings["TimeColor"], Color.FromArgb(255, 255, 255));
        BackgroundColor = SettingsHelper.ParseColor(settings["BackgroundColor"], Color.Transparent);
        BackgroundColor2 = SettingsHelper.ParseColor(settings["BackgroundColor2"], Color.Transparent);
        BackgroundGradient = SettingsHelper.ParseEnum(settings["BackgroundGradient"], GradientType.Plain);
        Accuracy = SettingsHelper.ParseEnum(settings["Accuracy"], TimeAccuracy.Seconds);
        Display2Rows = SettingsHelper.ParseBool(settings["Display2Rows"], false);
        ShowStatusDot = SettingsHelper.ParseBool(settings["ShowStatusDot"], true);
        if (IsHandleCreated) ModelToView();
    }
```

`GetSettingsHashCode`: fold every property with the existing `* 397 ^` pattern (keep the current file's comment on 397).

Delete the old `ShowSunkRow` property and `RowAccuracy` usage here (`RowAccuracy` itself is deleted in Task 6). Keep the component compiling: in `ReckoningComponent.cs`, temporarily change `Settings.ShowSunkRow` reads to `false` and `Settings.Accuracy` uses to a local `RowAccuracy.Tenths` literal — Task 6 rewrites that file entirely.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/LiveSplit.Reckoning.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src test
git commit -m "feat(settings): clone stock Run Prediction settings surface plus status dot"
```

---

### Task 6: Shell rewrite — component on `InfoTimeComponent` with damage-hit and dot overlays

The big one: `ReckoningComponent` becomes a port of stock `RunPrediction.cs` (comparison resolution, phase handling, formula, naming, `InfoTimeComponent` rendering) with three additions: the death-aware live delta, the damage-hit overlay, and the status dot.

**Files:**
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs` (full rewrite)
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponentFactory.cs` (description text only)
- Modify: `src/LiveSplit.Reckoning/UI/TimeText.cs` (delete now-unused `Format`/`FormatSunk` + `RowAccuracy`; keep `FormatHit`)
- Test: `test/LiveSplit.Reckoning.Tests/TimeTextTests.cs` (delete tests of removed methods)

**Interfaces:**
- Consumes: `SituationPrediction` + `ReckoningModel.Compute(elapsed, segmentStart, fullBest)` (Task 1), `PredictionMath.Compose` (Task 2), `DamageHit` + `TimeText.FormatHit` (Task 3), `ComparisonNaming` (Task 4), settings properties (Task 5). LiveSplit.Core: `InfoTimeComponent`, `SplitTimeFormatter`, `LiveSplitStateHelper.GetLastDelta`.
- Produces: the final `IComponent`. No new public surface for later tasks except: keep `SaveSidecar()` and `ReloadSidecarIfPathChanged()` private methods with their current names (Task 7 rewires them).

- [ ] **Step 1: Read the stock component end to end**

Read `c:\Users\thedo\git\LiveSplit\components\LiveSplit.RunPrediction\src\LiveSplit.RunPrediction\UI\Components\RunPrediction.cs` (all 224 lines). The `Update` method (lines 173-214), `PrepareDraw` (49-61), `DrawBackground` (63-80), and the ctor's `ComparisonRenamed` subscription (36-47) are the port targets.

- [ ] **Step 2: Rewrite `ReckoningComponent.cs`**

Structure (keep: poll timer + `PollCore` + detector/connection wiring, sidecar load/save, timer-event handlers, status-dot drawing; replace: all layout/draw/update code):

```csharp
public class ReckoningComponent : IComponent
{
    private const int PollIntervalMs = 15;          // keep existing comment
    private const float StatusDotSizePx = 5f;       // keep existing comment
    private const float StatusDotLeftPx = 3f;       // keep existing comment
    // Unlearned values render in a fixed dim gray: legible on light and dark
    // layouts where alpha-dimming vanished into dark backgrounds (live-test 1).
    private static readonly Color UnlearnedColor = Color.Gray;
    // Damage red, matching LiveSplit's default "behind, losing time" red so the
    // hit reads instantly as lost time.
    private static readonly Color HitColor = Color.FromArgb(255, 51, 51);
    // Gap between the hit number and the value text; one character-ish at
    // default fonts, so the hit reads as a separate transient, not a prefix.
    private const float HitGapPx = 8f;

    private readonly InfoTimeComponent internalComponent;
    private readonly SplitTimeFormatter formatter;
    private readonly DamageHit hit = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly SimpleLabel hitLabel = new();
    private ComposedPrediction lastComposed;
    private bool lastUnlearned;
    private string previousInformationName;
    ...
```

Ctor: current event wiring, plus `formatter = new SplitTimeFormatter(Settings.Accuracy); internalComponent = new InfoTimeComponent(null, null, formatter);` plus stock's `state.ComparisonRenamed += ...` handler (port lines 36-47: update `Settings.Comparison` when a renamed comparison was selected). Set `Settings.CurrentState = state`.

Size/padding properties: delegate everything to `internalComponent` (`VerticalHeight`, `MinimumHeight`, `HorizontalWidth`, `MinimumWidth`, `PaddingTop/Bottom/Left/Right`) exactly as stock does.

`Update(...)` — port stock's method with the death-aware insertion:

```csharp
    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        ReloadSidecarIfPathChanged();

        // Ported from LiveSplit's RunPrediction component (MIT).
        string comparison = Settings.Comparison == "Current Comparison" ? state.CurrentComparison : Settings.Comparison;
        if (!state.Run.Comparisons.Contains(comparison)) comparison = state.CurrentComparison;

        internalComponent.InformationName = internalComponent.LongestString = ComparisonNaming.GetDisplayedName(comparison);
        if (internalComponent.InformationName != previousInformationName)
        {
            internalComponent.AlternateNameText = ComparisonNaming.GetAbbreviations(comparison);
            previousInformationName = internalComponent.InformationName;
        }

        var method = state.CurrentTimingMethod;
        lastComposed = default;
        lastUnlearned = false;

        if (internalComponent.InformationName.StartsWith("Current Pace") && state.CurrentPhase == TimerPhase.NotRunning)
        {
            internalComponent.TimeValue = null;
        }
        else if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused
                 && state.CurrentSplitIndex >= 0 && state.CurrentSplitIndex < state.Run.Count
                 && state.CurrentTime[method] is TimeSpan elapsed)
        {
            var prediction = model.IsRunning ? ComputePrediction(state, method, elapsed) : null;
            lastUnlearned = prediction?.Unlearned ?? false;
            lastComposed = PredictionMath.Compose(
                LiveSplitStateHelper.GetLastDelta(state, state.CurrentSplitIndex, comparison, method),
                elapsed,
                state.CurrentSplit.Comparisons[comparison][method],
                state.Run.Last().Comparisons[comparison][method],
                prediction?.Finish);
            internalComponent.TimeValue = lastComposed.Value;
        }
        else if (state.CurrentPhase == TimerPhase.Ended)
        {
            internalComponent.TimeValue = state.Run.Last().SplitTime[method];
        }
        else
        {
            internalComponent.TimeValue = state.Run.Last().Comparisons[comparison][method];
        }

        hit.Update(lastComposed.Sunk, clock.ElapsedMilliseconds);

        cache.Restart();
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        cache["unlearned"] = lastUnlearned;
        cache["hitAmount"] = hit.Visible ? hit.Amount.Ticks : 0L;
        // Bucketed so the fade repaints smoothly without invalidating every tick.
        cache["hitAlpha"] = hit.Alpha(clock.ElapsedMilliseconds) / 16;
        if (cache.HasChanged) invalidator?.Invalidate(0, 0, width, height);

        internalComponent.Update(invalidator, state, width, height, mode);
    }
```

`ComputePrediction` (replaces `ComputeNow`; same segment-start scan, no remaining-sum loop):

```csharp
    private SituationPrediction ComputePrediction(LiveSplitState state, TimingMethod method, TimeSpan elapsed)
    {
        int index = state.CurrentSplitIndex;
        // Segment start = last non-null earlier split time (skips leave nulls).
        TimeSpan segmentStart = TimeSpan.Zero;
        for (int i = index - 1; i >= 0; i--)
            if (state.Run[i].SplitTime[method] is TimeSpan st) { segmentStart = st; break; }
        return model.Compute(elapsed, segmentStart, state.Run[index].BestSegmentTime[method]);
    }
```

`PollCore`: unchanged except feed the hit before the model consumes the events:

```csharp
        if (tick.Death) { hit.OnDeath(lastComposed.Sunk); model.OnDeath(); }
        if (tick.Checkpoint) model.OnCheckpoint(elapsed);
        if (tick.Respawn) { hit.OnRespawn(clock.ElapsedMilliseconds); model.OnRespawn(elapsed); }
```

`OnStart`/`OnReset`/`OnUndoSplit` additionally call `hit.Clear()` (a hit must not survive into a state it doesn't describe).

Draw — stock's PrepareDraw + background + our overlays:

```csharp
    private void PrepareDraw(LiveSplitState state, LayoutMode mode)
    {
        // Ported from LiveSplit's RunPrediction component (MIT).
        internalComponent.DisplayTwoRows = Settings.Display2Rows;
        internalComponent.NameLabel.HasShadow = internalComponent.ValueLabel.HasShadow = state.LayoutSettings.DropShadows;
        formatter.Accuracy = Settings.Accuracy;
        internalComponent.NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.TextColor : state.LayoutSettings.TextColor;
        var valueColor = Settings.OverrideTimeColor ? Settings.TimeColor : state.LayoutSettings.TextColor;
        internalComponent.ValueLabel.ForeColor = lastUnlearned ? UnlearnedColor : valueColor;
    }
```

`DrawBackground(Graphics g, LiveSplitState state, float width, float height)`: port stock lines 63-80 verbatim (gradient brush over `BackgroundColor`/`BackgroundColor2`, horizontal vs vertical axis).

`DrawVertical`/`DrawHorizontal`: `DrawBackground(...); PrepareDraw(state, mode); internalComponent.DrawVertical(g, state, width, clipRegion);` (resp. `DrawHorizontal`), then `DrawOverlays(g, state, width, height)`:

```csharp
    private void DrawOverlays(Graphics g, LiveSplitState state, float width, float height)
    {
        int alpha = hit.Alpha(clock.ElapsedMilliseconds);
        if (hit.Visible && alpha > 0)
        {
            float valueWidth = g.MeasureString(internalComponent.InformationValue ?? "", state.LayoutSettings.TimesFont).Width;
            hitLabel.Text = TimeText.FormatHit(hit.Amount);
            hitLabel.Font = state.LayoutSettings.TimesFont;
            hitLabel.ForeColor = Color.FromArgb(alpha, HitColor);
            hitLabel.HasShadow = state.LayoutSettings.DropShadows;
            hitLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
            hitLabel.HorizontalAlignment = StringAlignment.Far;
            hitLabel.VerticalAlignment = StringAlignment.Center;
            hitLabel.X = 0;
            hitLabel.Y = 0;
            hitLabel.Width = width - valueWidth - HitGapPx - 12;   // 12: InfoTextComponent's own value-label right inset
            hitLabel.Height = height;
            hitLabel.Draw(g);
        }

        if (Settings.ShowStatusDot)
        {
            using var dotBrush = new SolidBrush(connection.DotColor);
            g.FillRectangle(dotBrush, StatusDotLeftPx, (height - StatusDotSizePx) / 2f, StatusDotSizePx, StatusDotSizePx);
        }
    }
```

Delete: `nameLabels`/`valueLabels`, `DrawRow`, `DrawGeneral`, the `RowHeightPx`/`UnlearnedValueAlpha`/`HorizontalWidthPx`/`MinimumWidthPx`/`SidePaddingPx` constants, and the old `lastResult` field. Keep `Dispose` (add `state.ComparisonRenamed -= handler`).

In `TimeText.cs`: delete `Format`, `FormatSunk`, and the `RowAccuracy` enum; keep `NoValue` only if still referenced (it won't be — delete it too). `FormatHit` remains the file's only member. Delete the corresponding tests in `TimeTextTests.cs` (keep the three FormatHit tests).

In `ReckoningComponentFactory.cs`: update the `Description` string to `"Death-aware Run Prediction for SMW kaizo: any comparison, with learned post-death recovery paces and a damage-style time-lost hit."` — nothing else changes.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release && dotnet test test/LiveSplit.Reckoning.Tests`
Expected: build succeeds (auto-deploys to LiveSplit), all tests pass.

- [ ] **Step 4: Manual smoke (only if a LiveSplit instance is practical in this session; otherwise state that it was skipped)**

Restart LiveSplit, confirm the component renders with the stock look and that adding a stock Run Prediction component alongside (same comparison) shows the identical value while deathless.

- [ ] **Step 5: Commit**

```bash
git add -A src test
git commit -m "feat(ui): rebuild component on stock Run Prediction semantics with damage-hit overlay"
```

---

### Task 7: Sidecar saves only when the splits file is saved

Learning must shadow the `.lss` exactly like golds: in-memory during play, persisted only when LiveSplit writes the splits file, discarded if the user closes without saving.

**Files:**
- Create: `src/LiveSplit.Reckoning/Persistence/SplitsSaveWatcher.cs`
- Modify: `src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs` (rewire saves)
- Test: `test/LiveSplit.Reckoning.Tests/SplitsSaveWatcherTests.cs`

**Interfaces:**
- Consumes: `SaveSidecar()` / `ReloadSidecarIfPathChanged()` (Task 6 kept these names).
- Produces:
  - `public sealed class SplitsSaveWatcher : IDisposable` — `SplitsSaveWatcher(Action onSplitsSaved)`, `void WatchPath(string lssPath)` (null/empty stops watching; a new path swaps the `FileSystemWatcher`).
  - `public static bool SplitsSaveWatcher.ShouldFire(long lastFireMs, long nowMs)` — debounce predicate, public for tests.

- [ ] **Step 1: Write the failing debounce tests**

Create `SplitsSaveWatcherTests.cs`:

```csharp
using LiveSplit.Reckoning.Persistence;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SplitsSaveWatcherTests
{
    [Fact]
    public void FirstEventFires() => Assert.True(SplitsSaveWatcher.ShouldFire(lastFireMs: long.MinValue, nowMs: 0));

    [Fact]
    public void EventInsideSuppressWindowIsSwallowed()
        => Assert.False(SplitsSaveWatcher.ShouldFire(lastFireMs: 1000, nowMs: 1000 + SplitsSaveWatcher.SuppressWindowMs - 1));

    [Fact]
    public void EventAfterSuppressWindowFires()
        => Assert.True(SplitsSaveWatcher.ShouldFire(lastFireMs: 1000, nowMs: 1000 + SplitsSaveWatcher.SuppressWindowMs));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests --filter SplitsSaveWatcherTests`
Expected: FAIL — class not defined.

- [ ] **Step 3: Implement the watcher**

Create `SplitsSaveWatcher.cs`:

```csharp
using System;
using System.IO;

namespace LiveSplit.Reckoning.Persistence;

/// <summary>Fires a callback when LiveSplit writes the watched .lss file, so
/// the sidecar persists exactly when the user saves splits — learned data
/// shadows the splits file the same way golds do. One save produces several
/// FileSystemWatcher events; a suppress window coalesces them.</summary>
public sealed class SplitsSaveWatcher : IDisposable
{
    // 500 ms: comfortably wider than the event burst from one file write,
    // far narrower than any two deliberate user saves.
    public const long SuppressWindowMs = 500;

    private readonly Action onSplitsSaved;
    private FileSystemWatcher watcher;
    private long lastFireMs = long.MinValue;

    public SplitsSaveWatcher(Action onSplitsSaved) => this.onSplitsSaved = onSplitsSaved;

    public static bool ShouldFire(long lastFireMs, long nowMs) => nowMs - lastFireMs >= SuppressWindowMs;

    public void WatchPath(string lssPath)
    {
        watcher?.Dispose();
        watcher = null;
        if (string.IsNullOrEmpty(lssPath)) return;
        string dir = Path.GetDirectoryName(lssPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        watcher = new FileSystemWatcher(dir, Path.GetFileName(lssPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        // Created/Renamed too: editors and LiveSplit may write via temp+rename.
        watcher.Changed += OnFsEvent;
        watcher.Created += OnFsEvent;
        watcher.Renamed += OnFsEvent;
        watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        long now = Environment.TickCount64;
        if (!ShouldFire(lastFireMs, now)) return;
        lastFireMs = now;
        try { onSplitsSaved(); }
        catch { /* a failed save must never take down the watcher thread */ }
    }

    public void Dispose()
    {
        watcher?.Dispose();
        watcher = null;
    }
}
```

Note: `Environment.TickCount64` requires PolySharp/net481 check — if it doesn't compile on net481, use `unchecked((long)(uint)Environment.TickCount)` wrapped in a small `private static long NowMs()` with a comment, or `System.Diagnostics.Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000)`. Prefer the Stopwatch form; it is monotonic and net481-safe.

- [ ] **Step 4: Rewire the component**

In `ReckoningComponent.cs`:
- Add field: `private readonly SplitsSaveWatcher saveWatcher;` and a lock `private readonly object storeLock = new();`. Ctor: `saveWatcher = new SplitsSaveWatcher(SaveSidecar);`.
- `ReloadSidecarIfPathChanged()`: after loading the store for a new path, call `saveWatcher.WatchPath(lss);`.
- `OnSplit`: delete the `SaveSidecar()` call — splits no longer persist learning.
- `Dispose`: delete the `SaveSidecar()` call (closing without saving splits must discard learning); add `saveWatcher.Dispose();`.
- `SaveSidecar` runs on the watcher's threadpool thread now: wrap its body in `lock (storeLock)`, and also wrap the store-mutating blocks in `OnSplit`/`OnUndoSplit` handlers (`model.OnSplit(...)` / `model.OnUndoSplit(...)` calls) in `lock (storeLock)` — the store must not be serialized mid-mutation.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test test/LiveSplit.Reckoning.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src test
git commit -m "feat(persistence): sidecar saves only when the splits file is saved"
```

---

### Task 8: Docs sync + wrap

**Files:**
- Modify: `docs/TESTING.md`
- Modify: `README.md` (component behavior section)

**Interfaces:** none (docs only).

- [ ] **Step 1: Update TESTING.md**

Rework the "Quick play session" checklist to the new behavior (keep the doc's current structure and length discipline — Andrew asked for short):
- Value must match a stock **Run Prediction** component set to the same comparison, digit-for-digit, while deathless (add stock component alongside to verify). Note stock's label changes with comparison ("Best Segments" → "Best Possible Time").
- Comparison/accuracy/color/two-rows settings all behave exactly like stock Run Prediction, plus the status-dot toggle.
- On death: red damage number (e.g. `-22.4`) appears left of the value, grows during the death animation, freezes at respawn, fades out ~2.5 s. Invisible otherwise — there is no Sunk row anymore.
- Unlearned estimates render gray (not dimmed).
- Learning persists **only when you save splits** (Ctrl+S / save prompt on exit); closing without saving discards the session's learning — verify sidecar mtime only changes on splits save.
- Update the crib-sheet table rows: `PredictionMath.cs` (stock formula + death delta), `DamageHit.cs` (hit lifecycle), `SplitsSaveWatcher.cs` (save-on-splits-save), `ComparisonNaming.cs` (stock labels).
- Update the deferred-follow-ups list: remove items now fixed by this plan, keep the rest.

- [ ] **Step 2: Update README.md**

Adjust the component description: works for any comparison like stock Run Prediction (Best Segments → death-aware BPT), damage-hit display instead of a Sunk row, save-on-splits-save persistence. Keep install/build sections untouched.

- [ ] **Step 3: Full verification**

Run: `dotnet build Reckoning.sln -c Release && dotnet test test/LiveSplit.Reckoning.Tests`
Expected: clean build (auto-deploy fires), all tests green.

- [ ] **Step 4: Commit**

```bash
git add docs/TESTING.md README.md
git commit -m "docs: sync TESTING.md and README with Run Prediction rebase"
```
