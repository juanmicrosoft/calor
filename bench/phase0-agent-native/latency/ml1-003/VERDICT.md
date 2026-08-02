# M-L1 run ml1-003 — PP-L1 verdict: PASS (adjudicating record, re-baselined fixture)

**Date:** 2026-08-01 · **Commit:** c1662210 (branch
`chore/latency-fixture-rebaseline` = main `a326fd5e` + the fixture
re-baseline commit; tree hash `1b2857c0ddac6417039a68c29985fa6b6a346259`) ·
**Build:** Release calor.dll · **dotnet SDK:** 10.0.302 · **Machine:** repo
owner's dev machine (darwin/arm64) — M-L1 is agentless and model-free; no
pins apply (plan §4).

## Fixture lineage change (this run's reason to exist)

This run **re-baselines M-L1 on the regenerated D3.3 fixture** (seed
**6089**, generated 2026-08-01), per the fixture README's recorded policy —
both regeneration triggers had fired (the #807 verifier fix landed in
v0.10, and the compiler minor version bumped 0.9→0.10). The fixture's
contracts now include result-referencing postconditions and immutable
§B-chain bodies (the post-#807/G4 proven surface); all 1,988 contracts
verify plain **Proven** (0 assumed / 0 unsupported / 0 refuted) at
generation. **ml1-001/ml1-002 are records for the old lineage (seed 4537,
pre-#807 forms) and are not comparable to these bytes**; this is the
adjudicating record for the new lineage. For reference only: ml1-002's
percentiles on the old fixture were P50 2 ms / P99 8.84 ms — the new
fixture's numbers land in the same band, so the richer contract forms did
not move edit→envelope latency.

## Adjudicating surface: MCP write path (warm session)

All 230 committed timed edits (200 safe, 30 breaking) through
`calor_file_write` in one warm session; every verdict matched the edit
script's expectation (200 applied, 30 rejected); no heals.

| Metric | Value | PP-L1 threshold (gates Annex A.2, frozen) | Result |
|---|---|---|---|
| P50 | **2 ms** | ≤ 300 ms | pass |
| P99 | **7.42 ms** | ≤ 1 000 ms | pass |
| P90 / max | 3 ms / 24 ms | — | — |
| session open (cold, 106 files) | 104 ms | — | — |

**PP-L1: PASS** on the re-baselined fixture.

## Reported surface: watch rebuild — NOT MEASURED (recorded deviation)

The watch surface could not produce a valid record on the current
toolchain: the driver's integrity check ("every rebuild is a genuine 1-file
incremental") fails because **every rebuild recompiles 105 of 106 files**.
Root cause, diagnosed not massaged: the W1 Slice 1 postcondition-lowering
stopgap emits **`Calor1001`** warnings for §S-bearing bodies with
early/branch returns, and `CompilationDriver` deliberately caches only
diagnostic-clean files (a skipped file emits no diagnostics, so caching a
warning-carrying file would silently drop its warnings from warm builds) —
so the 105 layer modules are never cache-skippable. **This is pre-existing
toolchain behavior, not an artifact of the regeneration**: the old
(seed-4537) fixture's modules produce Calor1001 under the same binary as
well (verified: old `l1m01.calr` → 9 warnings, new → 3). The watch surface
becomes measurable again when the structural lowering (#764) retires
Calor1001. Watch is the *reported* surface only; PP-L1 adjudicates on MCP
(plan §5), so the verdict above is unaffected. The driver was not modified.

## Provenance

- Fixture + edit script: the committed re-baselined D3.3 bytes
  (`../fixture-10k`, `../edits/edit-script.jsonl`, seed 6089) — the driver
  refused-on-dirty-tree gate confirms measurement against the committed
  state.
- Stream: `mcp-writes.jsonl` (230), all valid under `validate-telemetry.sh
  --expect mcp-write/2` (230/230, stream-pure). No `watch-rebuilds.jsonl`
  (surface not measured, above).
- Full per-edit rows in `result.json` (carries `treeHash` and `dotnetSdk`
  per the driver's provenance stamps).
