# M-L1 run ml1-001 — PP-L1 verdict: PASS (pre-merge record; superseded by ml1-002)

**The re-run policy below has been fulfilled:** `../ml1-002/` is the
adjudicating record, run on merged main after all review-fix commits; it
agrees with this run within 1 ms on every percentile.

**Date:** 2026-07-27 · **Commit:** c94b51be (loop-m4-ws3 integration branch:
D3.1 #802 + D3.2 #803 + D3.3 #804 + driver) · **Build:** Release calor.dll ·
**Machine:** repo owner's dev machine (darwin/arm64) — M-L1 is agentless and
model-free; no pins apply (plan §4).

## Adjudicating surface: MCP write path (warm session)

All 230 committed timed edits (200 safe, 30 breaking) applied through
`calor_file_write` in one warm session over the D3.3 fixture; every verdict
matched the edit script's expectation (200 applied, 30 rejected). Latency is
the tool's own `mcp-write/2` `latencyMs` (tool-call receipt → verdict
record, covering session refresh, gate, heal, parse, check set, apply).

| Metric | Value | PP-L1 threshold | Result |
|---|---|---|---|
| P50 | **2 ms** | ≤ 300 ms | pass |
| P99 | **9 ms** | ≤ 1 000 ms | pass |
| P90 / max | 4 ms / 23 ms | — | — |
| session open (cold, 106 files) | 109 ms | — | — |
| mean client RTT overhead | ~1 ms | — | — |

**PP-L1: PASS.** Per plan §5, the direct-to-IL backend proposal is
permanently retired and the latency argument is closed with data.

## Reported surface: watch rebuild (incremental, raw edits)

200 safe edits as raw file writes under `calor watch --format json`;
latency is `watch-rebuild/1` `latencyMs` (rebuild start → envelope
written). The configured 200 ms debounce window is excluded by definition
and recorded in every record.

| Metric | Value |
|---|---|
| P50 / P90 / P99 | 27 ms / 31 ms / 32 ms |
| initial (cold) compile, 106 files | 392 ms |
| rebuild errors | none |

## Provenance

- Fixture + edit script: committed D3.3 state (`../fixture-10k`,
  `../edits/edit-script.jsonl`, seed 4537).
- Run commit `c94b51be`, tree hash `046eb1ea25269e6cc28930824f962a047badc67a`
  — the tree hash is the durable content anchor: the run commit sits on a
  pre-merge integration branch and may be orphaned by the post-merge
  rebase.
- **Re-run policy:** this record is valid for the exact integrated bytes it
  ran on. After #802–#804 merge (including any review-fix commits), the
  measurement is re-run against merged main as `ml1-002` stamped with the
  final SHA before this PR undrafts; ml1-001 is kept as the pre-merge
  record. Given the ~30–150× threshold headroom, a verdict flip is not
  expected — but the re-run makes the adjudicating record's provenance a
  merged commit, not an orphaned one.
- Streams: `mcp-writes.jsonl` (230 records) + `watch-rebuilds.jsonl`
  (201 records incl. initial), all 431 valid under `validate-telemetry.sh`.
- Full per-edit rows in `result.json`.
