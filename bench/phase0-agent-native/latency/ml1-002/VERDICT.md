# M-L1 run ml1-002 — PP-L1 verdict: PASS (adjudicating record)

**Date:** 2026-07-27 · **Commit:** d3e823f3 (this PR's branch = merged main
`047cdb6b` + the driver/record commit; tree hash
`0276792f6c5972295fcef424ac158045c5a642b2`) · **Build:** Release calor.dll ·
**dotnet SDK:** 10.0.302 · **Machine:** repo owner's dev machine
(darwin/arm64) — M-L1 is agentless and model-free; no pins apply (plan §4).

This is the re-run committed to in ml1-001's re-run policy: executed after
#801/#802/#806(#803)/#804 merged — including every adversarial-review fix
commit — so the adjudicating record's provenance is the merged toolchain,
not a pre-merge integration branch. ml1-001 is kept as the pre-merge
record; the two agree within 1 ms on every percentile.

## Adjudicating surface: MCP write path (warm session)

All 230 committed timed edits (200 safe, 30 breaking) through
`calor_file_write` in one warm session; every verdict matched the edit
script's expectation (200 applied, 30 rejected); no heals.

| Metric | Value | PP-L1 threshold | Result |
|---|---|---|---|
| P50 | **2 ms** | ≤ 300 ms | pass |
| P99 | **8.84 ms** | ≤ 1 000 ms | pass |
| P90 / max | 3 ms / 23 ms | — | — |
| session open (cold, 106 files) | 112 ms | — | — |

**PP-L1: PASS.** Per plan §5, the direct-to-IL backend proposal is
permanently retired; the latency argument is closed with data.

## Reported surface: watch rebuild (incremental, raw edits)

| Metric | Value |
|---|---|
| P50 / P90 / P99 | 27 ms / 30 ms / 33 ms |
| initial (cold) compile, 106 files | 382 ms |
| debounce (excluded, recorded) | 200 ms |
| rebuild errors | none |

## Provenance

- Fixture + edit script: the merged D3.3 bytes (`../fixture-10k`,
  `../edits/edit-script.jsonl`, seed 4537) — byte-identical to what ml1-001
  ran on (verified across the #804 review-fix commit).
- Streams: `mcp-writes.jsonl` (230) + `watch-rebuilds.jsonl` (201), all 431
  valid under `validate-telemetry.sh` (and 230/230 under
  `--expect mcp-write/2` stream purity).
- Full per-edit rows in `result.json` (carries `treeHash` and `dotnetSdk`
  per the hardened driver's provenance stamps).
