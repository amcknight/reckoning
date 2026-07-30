# reckoning — project instructions

Death-aware Best Possible Time: a LiveSplit layout component for SMW kaizo
that computes what finish time is *actually* still possible given where death
left you. Start here: `docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md`
is the approved design spec. v1 is implemented; the plan (with a final-review
"Amendments" section documenting three deliberate deviations from the spec's
literal formulas) is `docs/superpowers/plans/2026-07-30-death-aware-bpt.md`,
and the live-testing guide is `docs/TESTING.md`.

## Sibling repos (read-only reference — never edit from here)

- `../snes_offsets` — owns everything below the WRAM seam via `SNES.dll`:
  emulator attach, liveness, ROM identity, raw reads. Consume it as an
  unpinned sibling reference, like kaizosplits does.
- `../kaizosplits` — `Kaizo.asl` and the SMW watcher layer are the proven
  patterns for death/checkpoint/exit detection (`midway`, `CPEntrance`,
  `levelNum`/`roomNum`).
- `../SMWCounters` — the template for repo/build layout
  (`Directory.Build.props`, `src/`, `test/`, `artifacts/`, `scripts/`,
  release zip bundling the component DLL + `SNES.dll`) and for the
  status-pixel connection-health pattern.
- `../spinlab` — source of the hot/cold concept (hot = checkpoint reached
  alive, cold = respawned there after death). Its modeling rules apply
  here: no magic numbers, no fudge factors, thresholds must be earned.
- Upstream friction becomes a `TODO(snes_offsets):` / `TODO(kaizosplits):`
  marker or a written request for Andrew — never an edit to a sibling.

## Conventions

- C# LiveSplit component, project name `LiveSplit.Reckoning`, output
  `Reckoning.dll`.
- Red-green TDD. The calc engine (marker model, hot/cold bests, DR-BPT
  math) stays pure — no LiveSplit or WRAM types — and fully unit-tested.
- Work on a feature branch; Andrew reviews the diff against main and merges
  himself.
