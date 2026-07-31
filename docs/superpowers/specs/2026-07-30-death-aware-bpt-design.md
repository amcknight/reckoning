# Reckoning — Death-aware Best Possible Time (design)

## Overview

**Reckoning** is a LiveSplit layout component (C#) for SMW kaizo runs that
shows a **death-aware run prediction** for any LiveSplit comparison, with
time lost to deaths surfaced as a transient damage number. The name is from
*dead reckoning*: computing your position from a known past point — here,
reckoning your best possible finish from where death actually left you.

LiveSplit's standard Best Possible Time assumes the current segment can still
be completed in best-segment time. That's wrong the moment you die: you're
back at a checkpoint, and the portion of the segment you must replay can no
longer be compressed below your best from that checkpoint. Reckoning tracks
deaths and checkpoint touches via emulator WRAM and computes what is
*actually* still possible.

## Core calculation

```
DR-BPT  = run elapsed
        + best(current marker → segment exit)   [cold after a death]
        + Σ best of remaining full segments      [LiveSplit best segments]

Sunk    = DR-BPT − standard BPT
```

> **Shipped deviation:** the marker→exit term is anchored at the moment the
> (marker, variant) situation was entered — `max(arrival + best, elapsed)` —
> not re-evaluated against "now", so the estimate holds steady during
> post-respawn play instead of ramping. See Amendment 1 in
> `docs/superpowers/plans/2026-07-30-death-aware-bpt.md`.

> **Shipped deviation (Run Prediction rebase, 2026-07-31):** the
> `Σ remaining full segments` term is gone. Reckoning now composes stock
> Run Prediction's formula for the *selected comparison* —
> `max(lastDelta, liveDelta) + comparisonFinal` — and substitutes a
> death-aware live delta built from the current split's predicted finish.
> The comparison supplies everything beyond the current split, so this
> generalizes past Best Segments to any comparison. `Sunk` is
> correspondingly the death-aware value minus the stock value (still
> exactly zero while deathless) and is no longer displayed as a row — it
> drives the damage-hit overlay. See
> `docs/superpowers/plans/2026-07-30-run-prediction-rebase.md`.

- `Sunk` is what deaths this segment have irrevocably cost versus the naive
  optimistic calculation. Zero while deathless.
- Time basis follows the run's current timing method (RTA for kaizo,
  game time if the layout uses it).
- Best remaining full segments come from LiveSplit's existing best-segment
  data — Reckoning does not re-derive them.

## Marker model

Within a segment, checkpoint touches are **ordered progress markers**:

- Marker 0 = segment start. Each checkpoint touch increments the marker
  index (marker 1, 2, 3…).
- Handles multi-checkpoint kaizo retry hacks; the vanilla midway is simply
  the 1-marker case.
- Markers are identified by order-within-segment, not by level/screen
  coordinates — robust across hacks without per-hack configuration.
- On death, the runner is assumed to respawn at the current (last touched)
  marker; on segment split, marker state resets to 0 for the new segment.
- Undo split / skip split / timer reset: marker state and in-flight
  observations for the affected segment are discarded (never recorded as
  bests).

## Hot/cold (from spinlab)

Borrowed from spinlab's save-state variant concept: **hot** = you passed the
checkpoint alive and in flow (resources, momentum, Yoshi, powerup, timer
state); **cold** = you respawned there after a death. Marker→exit times
differ between the two, so bests are recorded **separately per variant**:

- After a death, DR-BPT prices from the **variant matching the tracked
  situation** at the current marker (cold right after respawn; hot again
  once a later checkpoint is reached alive) — that is the situation the
  runner is actually in. See Amendment 2 in
  `docs/superpowers/plans/2026-07-30-death-aware-bpt.md`.
- Marker 0 has both variants too: hot = entered the segment normally,
  cold = respawned at segment/level start after a death before any
  checkpoint.
- Fallback chain when data is unlearned: cold → hot → **gold prior** →
  full best segment from segment start, with the value visually flagged as
  unlearned (see Display). The gold prior (shipped 2026-07-31) prices the
  future as `segment gold − (this run's hot arrival at the marker −
  segment start)`, clamped at zero and anchored at the situation entry;
  marker 0's hot arrival is the segment start, so it collapses to "replay
  the segment from the respawn". The final rung is reached only when the
  hot arrival is unknowable (unanchored resume after undo/skip).

## Detection seam

- **WRAM access:** `SNES.dll` from the sibling repo `../snes_offsets` owns
  everything below the WRAM seam — emulator attach, liveness, ROM identity,
  raw reads. Consumed as an unpinned sibling reference, same contract as
  kaizosplits and SMWCounters. Never reimplement or fork that layer.
- **Death & checkpoint semantics:** modeled on kaizosplits' `Kaizo.asl`
  watchers (`midway`, `CPEntrance`, `levelNum`/`roomNum`) and SMWCounters'
  death counting. Reckoning owns its own watcher layer; sibling repos are
  read-only reference — friction becomes a `TODO(snes_offsets):` /
  `TODO(kaizosplits):` marker, never an edit over there.
- **LiveSplit integration:** a layout component like SMWCounters
  (`LiveSplit.Reckoning`), reading run state (current split, elapsed time,
  best segments, splits file path) from the LiveSplit state object.

## Persistence

- Learn live; persist to a **sidecar JSON** next to the splits file
  (`<splits>.reckoning.json`), keyed by the `.lss` path with run
  name/category as fallback identity.
- Schema: per segment → per marker index → per variant (hot/cold) →
  `{ bestMs, attempts }`. Attempt counts feed the unlearned indicator and
  are the future export surface for the `segments` probabilistic model.
- Bests only update on a *completed* marker→exit observation ending in a
  real split (not skip/undo/reset).
- File is written atomically (write-temp-then-rename) whenever LiveSplit
  writes the splits file, so learned data shadows the `.lss` exactly like
  golds do — splitting alone persists nothing, and closing without saving
  splits discards the session's learning. (Shipped deviation, 2026-07-31:
  the spec originally said "on split and on shutdown". Shutdown still
  saves, but only when the `.lss` was rewritten after the component
  loaded, to cover LiveSplit's exit-save race.) Corrupt/missing sidecar
  degrades to unlearned, never crashes the component.

## Display

One row plus overlays:

One stock-styled Run Prediction row: the comparison's own label (following
stock's naming) and the death-aware predicted finish. Time lost to deaths
is not a second row — it appears as a transient red damage number left of
the value, which grows during the death animation, freezes on the first
frame after respawn, and fades over ~2.5 s.

An unlearned marker (fallback in effect) renders the value in a fixed dim
gray — the original "dimmed value" treatment vanished into dark layouts in
live testing. Connection health reuses SMWCounters' status-pixel pattern,
drawn in a reserved left gutter.

## Architecture

- New repo `reckoning`, mirroring SMWCounters' build layout
  (`Directory.Build.props`, `src/`, `test/`, `artifacts/`, `scripts/`,
  release zip containing `Reckoning.dll` + `SNES.dll`).
- Three units with clear seams:
  1. **Calc engine** — pure C#: marker model, hot/cold bests, DR-BPT math.
     No LiveSplit, no WRAM. Fully unit-testable.
  2. **Watcher layer** — WRAM polling via SNES.dll → death/checkpoint/exit
     events. Contract-pinned tests like kaizosplits.
  3. **Component shell** — LiveSplit lifecycle, settings UI, rendering,
     sidecar IO.

## Testing

- Red-green TDD (house style). Calc engine gets exhaustive unit tests
  (marker ordering, hot/cold selection, fallback chain, undo/skip/reset
  discards).
- Watcher layer gets contract tests against recorded WRAM traces where
  feasible (pattern exists in kaizosplits/SMWCounters).
- No fudge factors / magic numbers — spinlab's modeling rules apply.

## Out of scope (v1)

- Feeding the `segments` probabilistic model or spinlab (the sidecar schema
  is designed to allow it later).
- Expected-deaths / probabilistic BPT (this v1 is strictly "sunk time";
  the segments model is the future home for probabilistic estimates).
- Non-SMW games; manual/hotkey death input.
- Editing or generalizing sibling repos.

## Open questions for planning

- Exact WRAM signals for death vs. respawn vs. checkpoint touch in retry
  hacks (mine Kaizo.asl and SMWCounters for the proven patterns).
- Whether marker identity by order-within-segment survives routes that
  intentionally skip a checkpoint on some attempts (may need
  marker-set reconciliation or a per-segment expected-marker count).
- Settings surface: which toggles are worth exposing in v1 (sunk line
  on/off, unlearned flag style, status pixel).
