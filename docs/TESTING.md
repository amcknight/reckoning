# Reckoning — live testing & iteration guide

Written at v1 wrap-up (2026-07-30), updated for the Run Prediction rebase.
The unit suite (98 tests) covers the calc engine, watcher semantics,
persistence, and formatting; **what has never happened yet is a live run
against a real emulator inside LiveSplit** — that is the first thing to do
together.

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
3. In LiveSplit: Edit Layout → add "Reckoning" (Information category). Its
   settings are a full clone of stock Run Prediction's (Comparison, text/time
   color overrides, background + gradient, Accuracy, Display 2 rows) plus a
   Reckoning-only "Show connection status dot" toggle. For comparison, also
   add a stock "Run Prediction" component set to the same Comparison.

Prereqs: a supported emulator (`snes9x`, `snes9x-x64`, `bsnes`, `retroarch`,
`higan`, `snes9x-rr`, `mesen`, `emuhawk`, `ares`, `mednafen`), an SMW kaizo
ROM (ideally a retry-hack with multiple checkpoints AND a vanilla-midway
hack to cover both detection paths), and a splits file saved to disk (the
sidecar needs `state.Run.FilePath`; unsaved splits = learning stays
in-memory only and is discarded on close).

## Quick play session

Work through in order, stock Run Prediction alongside Reckoning on the same
comparison.

1. **Deathless == stock.** With best segments present, Reckoning's value
   matches the stock component digit-for-digit the whole run. Switch
   Comparison on both (Best Segments / Current Comparison / Average / Worst)
   — the name label relabels identically on both ("Best Possible Time",
   "Current Pace", "Predicted Time", "Worst Possible Time") and the values
   stay equal.
2. **Settings clone stock.** Flip each of Comparison, override text/time
   color, background + gradient, Accuracy (Seconds/Tenths/Hundredths/
   Milliseconds), Display 2 rows — each should behave exactly like the same
   setting on stock Run Prediction. "Show connection status dot" is the one
   Reckoning-only extra.
3. **First death, unlearned.** Die before touching any checkpoint: a red
   damage number (e.g. `-3.2`) appears left of the value, grows while the
   death animation plays, freezes the instant you respawn, then fades to
   invisible over ~2.5 s. Because nothing is learned yet for this
   checkpoint, the value itself renders gray (not dimmed).
4. **Learn it, then reuse it.** Touch a checkpoint, die, respawn, finish the
   segment, split, then **save the splits (Ctrl+S)**. Open
   `<splits>.lss.reckoning.json`: expect a hot AND a cold entry for that
   marker with plausible `bestMs`/`attempts`. Die at the same checkpoint
   again: the value now uses the learned cold best (no longer gray) and
   **holds steady after respawn** instead of ticking up 1s/s.
5. **Persistence is save-gated.** Split a few more times WITHOUT saving,
   note the sidecar's file mtime, then close LiveSplit without saving splits
   and relaunch. Confirm the mtime is unchanged and the newer learning is
   gone (discarded, like an unsaved gold). Repeat and actually save this
   time — mtime updates.
6. **Deathless stays deathless.** Play an entire deathless segment: no
   damage number ever appears, and the value tracks stock exactly the whole
   time.
7. **Connection dot still behaves.** Close/open/pause the emulator and watch
   the dot cycle red/yellow/blue/purple/orange/gray as expected — detection
   is unchanged by this branch, this is just a regression check.

## Reading the sidecar

`<splits>.lss.reckoning.json` (next to the splits file) — `segments[].markers[]`
of `{marker, variant: hot|cold, bestMs, attempts}`. `attempts` counts
completed observations (real splits only). Best debugging surface: if the
display looks wrong, check whether the learned data or the math is at
fault. Only written when you save splits (see step 5 above).

## Where things live (iteration crib sheet)

| Concern | File |
|---|---|
| Stock Run Prediction formula + death-aware live delta | `Engine/PredictionMath.cs` |
| Per-situation death prediction (current split's finish only) | `Engine/ReckoningCalculator.cs` |
| Marker/variant state, observations | `Engine/SegmentTracker.cs` |
| Split/undo/skip/reset orchestration, undo journal | `Engine/ReckoningModel.cs` |
| Learned bests store | `Engine/BestsStore.cs` |
| Damage-hit lifecycle (grow/freeze/fade) | `Engine/DamageHit.cs` |
| WRAM addresses & value constants | `Watchers/SmwAddresses.cs` |
| Death/checkpoint/respawn detection (kaizosplits port) | `Watchers/SmwEventDetector.cs` |
| Emulator attach/status (SNES.dll bridge) | `Snes/SnesConnection.cs`, `Snes/EmulatorProcessFinder.cs`, `Snes/StatusDot.cs` |
| Sidecar JSON | `Persistence/SidecarStore.cs` |
| Save-on-splits-save watcher | `Persistence/SplitsSaveWatcher.cs` |
| Stock comparison labels ("Best Possible Time" etc.) | `UI/Components/ComparisonNaming.cs` |
| Damage-number text formatting | `UI/TimeText.cs` |
| LiveSplit wiring, rendering, settings | `UI/Components/ReckoningComponent.cs`, `…Settings.cs`, `…Factory.cs` |

Design lineage: spec `docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md`
→ v1 plan `docs/superpowers/plans/2026-07-30-death-aware-bpt.md` → rebase plan
`docs/superpowers/plans/2026-07-30-run-prediction-rebase.md` (this branch:
stock Run Prediction semantics for any comparison, death-awareness as a live
delta, damage-hit overlay replacing the Sunk row, save-on-splits-save
persistence). Engine stays pure — no LiveSplit/WRAM types — so calc changes
are always unit-testable first: `dotnet test test/LiveSplit.Reckoning.Tests`.

## Known deferred follow-ups

Real but non-blocking; candidates for the first iteration pass:

1. **Never seen live**: the Task 6 dot-gutter layout (status dot no longer
   overlapping the name label) and the damage-hit rendering (growth/freeze/
   fade, positioning) — both need eyes on a real layout before trusting them.
2. **Elapsed-null at split desyncs the model** — if `CurrentTime[method]` is
   null when a split fires (game-time method, no game time), `OnSplit` is
   skipped and `model.CurrentSegmentIndex` drifts from LiveSplit's. Rare for
   RTA kaizo; fix = always advance the model.
3. **Save As mid-run kills the run's tracking** — sidecar reload on
   `FilePath` change replaces the model mid-run (`IsRunning` goes false).
   Defer reload while running.
4. **Timing-method switch mid-run** mixes RTA/game-time anchors in open
   observations; cheap fix = discard the in-flight segment on method change.
5. **Factory hardcodes `Version.Parse("0.1.0")`** — will drift from tagged
   releases; read from assembly instead.
6. **Spec's "run name/category fallback identity"** for the sidecar is
   written but never used for validation on load — conscious cut.
7. Test polish: strengthen `DetachDropsEdgesAndLatches`'s final assert
   (gameMode edge is consumed by the reattach baseline); `Load` ignores the
   sidecar `version` field (matters only when a future schema bump changes
   field meanings).
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
