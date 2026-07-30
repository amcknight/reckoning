# Reckoning

**Death-aware Best Possible Time** for [LiveSplit](https://livesplit.org/) —
a layout component for SMW kaizo runs.

LiveSplit's Best Possible Time assumes you can still hit your best on the
current segment. The moment you die, that's a lie: you're back at a
checkpoint, and the part of the segment you must replay can't be compressed
below your best *from that checkpoint*. Reckoning watches deaths and
checkpoint touches via emulator WRAM (through
[snes_offsets](https://github.com/amcknight/snes_offsets)' `SNES.dll`) and
shows:

- **Reckoning** — the best finish time actually still reachable from where
  death left you, and
- **Sunk** — how much this segment's deaths have irrevocably cost.

The name is from *dead reckoning*: navigating from a known past position.

Status: design phase. See
[`docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md`](docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md).
