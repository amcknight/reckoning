# Reckoning — live testing & iteration guide

Written at v1 wrap-up (2026-07-30) so a future session can test and iterate
with zero prior context. The unit suite (87 tests) covers the calc engine,
watcher semantics, persistence, and formatting; **what has never happened yet
is a live run against a real emulator inside LiveSplit** — that is the first
thing to do together.

## Deploy for testing

Fast inner loop (auto-deploy on every build):

1. Create `src/LiveSplit.Reckoning/Reckoning.local.props` (git-ignored):

   ```xml
   <Project><PropertyGroup>
     <ComponentsPath>C:\Apps\LiveSplit\Components</ComponentsPath>
   </PropertyGroup></Project>
   ```

2. `pwsh -File scripts/fetch-livesplit-core.ps1` (once), then
   `dotnet build Reckoning.sln -c Release`. The post-build target copies
   `Reckoning.dll` + `SNES.dll` into the Components dir (warns instead of
   failing if LiveSplit has them locked — restart LiveSplit to pick up a
   rebuild).
3. In LiveSplit: Edit Layout → add "Reckoning" (Information category).
   Settings: Show Sunk row / Show status dot / Accuracy (Seconds, Tenths,
   Hundredths).

Prereqs: a supported emulator (`snes9x`, `snes9x-x64`, `bsnes`, `retroarch`,
`higan`, `snes9x-rr`, `mesen`, `emuhawk`, `ares`, `mednafen`), an SMW kaizo
ROM (ideally a retry-hack with multiple checkpoints AND a vanilla-midway
hack to cover both detection paths), and a splits file saved to disk (the
sidecar needs `state.Run.FilePath`; unsaved splits = learning stays
in-memory only).

## Live smoke checklist

Work through in order; each line says what to do and exactly what to expect.

**Connection (no timer running):**
- [ ] Emulator closed → dot red (Detached). Launch emulator, no ROM → dot
      moves through yellow (Searching/Discovering) to orange (NoContent) or
      gray (retry cooldown). Load SMW ROM → blue (Resolved) within ~seconds.
- [ ] Pause emulator a while → purple (Held) is acceptable; reads resume
      instantly on unpause.
- [ ] Kill the emulator mid-session → dot red, LiveSplit does NOT freeze or
      error-dialog (Poll backstop + HasExited guards). Relaunch → reattaches
      by itself within ~1s.

**Deathless behavior (start a run):**
- [ ] Both rows show em-dash until best-segment data exists for the run
      (fresh splits ⇒ standard BPT undefined ⇒ expected `—`).
- [ ] With best segments present: Reckoning == LiveSplit's own Best Possible
      Time row (add the stock BPT component alongside to compare) and
      Sunk == 0:00.0 the whole time you stay deathless. Any nonzero Sunk
      while deathless is a bug.

**Death mechanics (the core):**
- [ ] Die before any checkpoint → Sunk starts growing during the death
      animation. First-ever death at a marker = no cold best learned yet →
      the value renders at half opacity (unlearned flag, falls back
      hot → standard).
- [ ] Touch a checkpoint, die, respawn, finish the segment, split. Open
      `<splits>.lss.reckoning.json` (next to the splits file): expect that
      segment to have marker 0 hot, marker 1 hot, AND marker 1 cold entries
      with plausible `bestMs`/`attempts`.
- [ ] Second run through the same segment: die at the same checkpoint →
      Reckoning now uses the learned cold best (full opacity) and — key
      behavior — **holds steady after respawn** instead of ticking up
      1s/s (the anchored-term amendment). It only starts climbing again if
      you fall behind the learned pace (`max(arrival + best, elapsed)`).
- [ ] After a death, reach the NEXT checkpoint alive → pricing flips back to
      hot at that marker (variant-follows-situation amendment); Sunk keeps
      the earlier cost.
- [ ] Multi-checkpoint retry hack: each checkpoint entrance change fires a
      new marker (watch sidecar marker indices grow 0,1,2…). Vanilla midway
      tape also fires (different WRAM path — test both hacks).

**Timer operations:**
- [ ] Accidental split + undo + finish segment → sidecar must NOT contain an
      absurdly fast marker-0 best for that segment (undo poisoning fix), and
      the undone split's records are reverted.
- [ ] Skip a split → nothing recorded for the skipped-into segment's
      marker 0.
- [ ] Finish the whole run → no crash/stall after the final split (Ended
      bounds guard); layout keeps repainting.
- [ ] Reset mid-segment → in-flight observations discarded (sidecar
      unchanged for that segment).
- [ ] Close LiveSplit → sidecar written on shutdown (check file mtime).

**Failure injection:**
- [ ] Corrupt the sidecar (truncate it) → component loads as unlearned,
      never crashes; next split rewrites a valid file.
- [ ] Delete the sidecar → same: relearns from scratch.

## Reading the sidecar (ground truth while testing)

`<splits>.lss.reckoning.json` — `segments[].markers[]` of
`{marker, variant: hot|cold, bestMs, attempts}`. `attempts` counts completed
observations (real splits only). It is the single best debugging surface:
if the display looks wrong, check whether the learned data or the math is at
fault.

## Where things live (iteration crib sheet)

| Concern | File |
|---|---|
| DR-BPT / Sunk math, fallback chain, anchoring | `src/LiveSplit.Reckoning/Engine/ReckoningCalculator.cs` |
| Marker/variant state, observations | `Engine/SegmentTracker.cs` |
| Split/undo/skip/reset orchestration, undo journal | `Engine/ReckoningModel.cs` |
| Learned bests store | `Engine/BestsStore.cs` |
| WRAM addresses & value constants | `Watchers/SmwAddresses.cs` |
| Death/checkpoint/respawn detection (kaizosplits port) | `Watchers/SmwEventDetector.cs` |
| Emulator attach/status (SNES.dll bridge) | `Snes/SnesConnection.cs`, `Snes/EmulatorProcessFinder.cs`, `Snes/StatusDot.cs` |
| Sidecar JSON | `Persistence/SidecarStore.cs` |
| LiveSplit wiring, rendering, settings | `UI/Components/ReckoningComponent.cs`, `…Settings.cs`, `…Factory.cs`; `UI/TimeText.cs` |

Design lineage: spec `docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md`
(carries shipped-deviation notes) → plan
`docs/superpowers/plans/2026-07-30-death-aware-bpt.md` (its **"Amendments
(final review)"** section explains the three deliberate deviations:
anchored post-death term, variant-follows-situation, unanchored undo/skip
resume). Engine stays pure — no LiveSplit/WRAM types — so calc changes are
always unit-testable first: `dotnet test test/LiveSplit.Reckoning.Tests`.

## Known deferred follow-ups (from the v1 review ledger)

Real but non-blocking; candidates for the first iteration pass:

1. **Elapsed-null at split desyncs the model** — if `CurrentTime[method]` is
   null when a split fires (game-time method, no game time), `OnSplit` is
   skipped and `model.CurrentSegmentIndex` drifts from LiveSplit's. Rare for
   RTA kaizo; fix = always advance the model.
2. **Save As mid-run kills the run's tracking** — sidecar reload on
   `FilePath` change replaces the model mid-run (`IsRunning` goes false).
   Defer reload while running.
3. **Timing-method switch mid-run** mixes RTA/game-time anchors in open
   observations; cheap fix = discard the in-flight segment on method change.
4. **Factory hardcodes `Version.Parse("0.1.0")`** — will drift from tagged
   releases; read from assembly instead.
5. **`ComputeNow`'s segment-start scan + remaining-sum loops** contain real
   logic in the untested shell — extract into a testable static helper.
6. **Spec's "run name/category fallback identity"** for the sidecar is
   written but never used for validation on load — conscious v1 cut.
7. Test polish: strengthen `DetachDropsEdgesAndLatches`'s final assert
   (gameMode edge is consumed by the reattach baseline); `Load` ignores the
   sidecar `version` field (matters only when v2 changes field meanings).
8. **`.github/workflows/release.yml` is finished but untracked** — harness
   policy blocks Claude committing CI workflows; Andrew:
   `git add .github && git commit`. Also: no `origin` remote, no LICENSE
   yet (deliberate).

## v2 ideas (spec's out-of-scope list, sidecar schema already supports)

- Feed the `segments` probabilistic model / spinlab (attempts counts are the
  export surface).
- Expected-deaths / probabilistic BPT rather than strict sunk-time.
- Marker-set reconciliation for routes that intentionally skip a checkpoint
  on some attempts (today they just learn under different marker indices and
  degrade gracefully via the fallback chain).
