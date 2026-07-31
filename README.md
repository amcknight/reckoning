# Reckoning

**Death-aware run prediction** for [LiveSplit](https://livesplit.org/) —
a layout component for SMW kaizo runs. Point it at Best Segments and it is
a death-aware Best Possible Time; every other comparison works too.

LiveSplit's run predictions assume the current segment can still be
completed in its comparison time — for Best Possible Time, your
best-segment time. That assumption breaks the moment you die: you're back
at a checkpoint, and the part of the segment you still have to replay
can't be compressed below your best *from that checkpoint*.
Reckoning watches deaths and checkpoint touches via emulator WRAM (through
[snes_offsets](https://github.com/amcknight/snes_offsets)'s `SNES.dll`) and
recomputes what finish time is actually still reachable from where death left
you. The name is from *dead reckoning*: navigating forward from a known past
position.

## Display

Reckoning works like LiveSplit's stock **Run Prediction** component for any
comparison you pick — the label follows the comparison the way stock does
("Best Segments" → "Best Possible Time", "Current Comparison"/PB →
"Current Pace", "Average Segments" → "Predicted Time", "Worst Segments" →
"Worst Possible Time"), except that any other comparison shows its own name
rather than stock's longer "Current Pace (name)". Deathless, the *value*
matches a stock Run Prediction component set to the same comparison
digit-for-digit.

Death-awareness is layered on top as a live delta: after a death, the
predicted finish for the *current split* is repriced from your learned
recovery pace (hot or cold) at the checkpoint or segment start you're
recovering from, which can raise the prediction above what stock alone would
show. Reach a further checkpoint alive and pricing flips back to hot bests.
With nothing learned for that checkpoint yet, Reckoning still prices the
death: it charges you the part of your segment gold you have to replay
from where you respawned, shown in gray as provisional. While the segment
stays deathless the death-aware term is zero by construction, so there's
nothing to distinguish from stock.

There's no separate "Sunk" row. Instead, a death shows as a transient red
damage number to the left of the value (e.g. `-22.4`) that grows while the
death animation plays, freezes on the first frame after you respawn — that
frame is where the re-anchored estimate lands, so the number reflects the
death's real cost — then fades out over about 2.5 seconds. Deathless,
nothing is drawn there at all, and a death the prediction fully absorbs
(amount zero) draws nothing either rather than a meaningless `-0.0`.

An unlearned estimate (no recorded time yet for the current checkpoint/
variant, so Reckoning had to fall back toward a coarser estimate) renders in
a fixed gray rather than the normal text color, so it reads as provisional
rather than as solid data.

## Settings

Configure via the component's settings in LiveSplit's layout editor — the
surface is a full clone of stock Run Prediction's, plus one Reckoning-only
toggle:

- **Comparison** — which comparison to predict against (default: Current
  Comparison). Value and formula follow this pick exactly like stock Run
  Prediction; the label does too, except that unmapped comparisons show
  their own name instead of "Current Pace (name)".
- **Override text color / time color**, **Background** (color + gradient) —
  same as stock.
- **Accuracy** — Seconds, Tenths, Hundredths, or Milliseconds (default:
  Seconds).
- **Display 2 rows** — stock's compact/expanded layout toggle.
- **Show connection status dot** — Reckoning-only; toggles the status dot
  (default: on).

## Install

Download the release zip and copy **both** `Reckoning.dll` and `SNES.dll`
into your `LiveSplit/Components/` folder, then add "Reckoning" (Information
category) to your layout in LiveSplit's layout editor.

## Supported emulators

Reckoning attaches automatically to a running emulator process — no
per-version configuration. Recognized process names: `snes9x`,
`snes9x-x64`, `bsnes`, `retroarch`, `higan`, `snes9x-rr`, `mesen`,
`emuhawk`, `ares`, `mednafen`. The status dot (see below) always shows
current connection health.

## Learned data (sidecar file)

Reckoning learns checkpoint-to-exit times live as you run and persists them
to a sidecar JSON file next to your splits file: `<splits>.lss.reckoning.json`.
Bests are tracked per splits file, per segment, per marker (checkpoint index
within the segment), and separately for the **hot** (reached alive) and
**cold** (reached after respawning there) variants, since post-death times
from a checkpoint are typically slower than a clean pass.

The sidecar is written only when you save your splits file (Ctrl+S, or the
save prompt on exit) — the same way LiveSplit only keeps golds you've saved.
Splitting alone does not persist anything; closing LiveSplit without saving
splits discards that session's learning. The sidecar is safe to delete at
any time — Reckoning relearns from scratch on the next run and never crashes
if it's missing or corrupt.

## Status dot

The connection-health dot reuses SMWCounters' status-pixel pattern and
snes_offsets' `SNES.dll` connection states:

| Color  | Meaning |
|--------|---------|
| Blue   | Resolved — memory base confirmed, uncontested; reads are trustworthy |
| Yellow | Searching/Discovering — attached, still locating the game in memory |
| Red    | Detached — no emulator process found, or it exited |
| Purple | Held — base was confirmed but activity has gone quiet past the dwell window (e.g. paused/menu); kept so resume is instant |
| Green  | Degraded — base confirmed but contested by a rival candidate; reads still valid |
| Orange | No content — attached, but nothing in memory is changing (no ROM loaded, or fully paused) |
| Gray   | Cooldown after a failed connection attempt, shown in place of yellow/orange while waiting to retry |

## Building from source

```
pwsh -File scripts/fetch-livesplit-core.ps1
dotnet build Reckoning.sln -c Release
```

The fetch script pulls `LiveSplit.Core.dll` and `UpdateManager.dll` into
`lib/` (not checked in). `dotnet test test/LiveSplit.Reckoning.Tests` runs
the test suite; the calc engine under `src/LiveSplit.Reckoning/Engine/` is
pure C# with no LiveSplit or WRAM dependency and is fully unit-tested.

## License

No `LICENSE` file exists yet in this repo; licensing is left to Andrew to
decide rather than being invented here. The release zip currently ships
without one.
