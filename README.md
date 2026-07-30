# Reckoning

**Death-aware Best Possible Time** for [LiveSplit](https://livesplit.org/) —
a layout component for SMW kaizo runs.

LiveSplit's standard Best Possible Time assumes the current segment can still
be completed in your best-segment time. That assumption breaks the moment you
die: you're back at a checkpoint, and the part of the segment you still have
to replay can't be compressed below your best *from that checkpoint*.
Reckoning watches deaths and checkpoint touches via emulator WRAM (through
[snes_offsets](https://github.com/amcknight/snes_offsets)'s `SNES.dll`) and
recomputes what finish time is actually still reachable from where death left
you. The name is from *dead reckoning*: navigating forward from a known past
position.

## Display

Reckoning draws two rows in your layout:

- **Reckoning** — the death-aware BPT: run elapsed so far, plus the best
  known time from your current position (checkpoint or segment start) to the
  segment exit, plus the best known time for every full segment still ahead.
  After a death this uses your *cold* best from the last checkpoint touched;
  before any death in the segment it's identical to standard BPT.
- **Sunk** — how much time this segment's deaths have irrevocably cost,
  i.e. Reckoning minus standard BPT. Zero while the segment stays deathless.

An unlearned row (no recorded time yet for the current marker/variant, so
Reckoning had to fall back toward a coarser estimate) is flagged visually
rather than silently shown as if it were solid data.

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
from a checkpoint are typically slower than a clean pass. The sidecar is
safe to delete at any time — Reckoning relearns from scratch on the next run
and never crashes if it's missing or corrupt.

## Status dot

The connection-health dot uses the same color legend as SMWCounters:

| Color  | Meaning |
|--------|---------|
| Blue   | Resolved — attached and reading a known ROM |
| Yellow | Searching for an emulator/ROM |
| Red    | Detached — no emulator process found |
| Purple | Held — connection paused (e.g. LiveSplit not running) |
| Green  | Degraded — attached but working around a partial read |
| Orange | Attached with no recognizable content loaded |
| Gray   | Cooldown after a failed connection attempt, before retrying |

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
