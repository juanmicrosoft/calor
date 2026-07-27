# WS2 Exit Epoch — ws2-exit-e2e-001

**Question:** loop plan §2 WS2 exit criterion (as descoped by Call 1;
`docs/plans/loop-m3-ws2.md` §1): does an agent complete a multi-edit E2E task
exclusively through the MCP file tools, with M-L2 and M-L4 measured?

**Verdict: exit criterion MET.** 5/5 valid runs completed their task
exclusively through `calor_file_write`, M-L2(mcp-file) = 5/5 = 100 % (both
heal-accounting forms identical — no heals occurred), M-L4 reported-only at
0 rejects. The multi-edit evidence is W3-004: one transactional write
carrying 5 (run 1) and 4 (run 2) declaration changes.

## Design

- Pairs: `W2-005-scheduler`, `W3-004-sync-folder` (hardest W-tier authorable
  fixtures); **calor arm only**, `--edit-mechanism mcp-file`, 3 runs/pair;
  model pin `claude-opus-4-8` (the M2 baseline pin); pins in `pins.json`.
- Enforcement two-sided: PreToolUse hook blocks raw `Edit`/`Write` on
  `.calr`; prompt states the constraint. Server registered per run via
  `--mcp-config` + `--strict-mcp-config`, write root pinned with
  `calor mcp --root $ws/src` (review of #799 item 1).
- This is a **capability/instrument epoch** (WS2 exit), not a comparison:
  no raw-arm counterpart ran, and nothing here feeds PP-L5 — the comparison
  remains M5's simultaneous epoch per gates-doc Annex A.

## Results

| Pair | Run | Valid | Success | itg | MCP writes (attempts/applied/healed/rejected) | Declarations changed |
|---|---|---|---|---|---|---|
| W2-005 | 1 | yes | yes | 1 | 1/1/0/0 | f013 |
| W2-005 | 2 | yes | yes | 1 | 1/1/0/0 | f013 |
| W2-005 | 3 | yes | yes | 1 | 1/1/0/0 | f013 |
| W3-004 | 1 | yes | yes | 1 | 1/1/0/0 | f011–f015 (5) |
| W3-004 | 2 | yes | yes | 1 | 1/1/0/0 | f011–f014 (4) |
| W3-004 | 3 | **invalid** | counted fail | 11 (censored) | — (no writes attempted) | — |

Every valid run: exactly one `mcp-write/1` record, verdict `safe`, applied,
`healApplied: false`; journal `edit_mechanism: "mcp-file"` on every edited
iteration; 0 escaped bugs. Rollup (`rollup.json`): successRate 5/6 = 0.833
with the invalid slot counted as failure per gates doc §0.2.

## Metrics

- **M-L2(mcp-file)** = applied/attempts = **5/5 = 100 %**;
  appliedUnhealed/attempts = 5/5 (identical — no heals). Annex A-1.1 dual
  reporting satisfied trivially this epoch.
- **M-L4** = **reported-only, 0 rejects** (< 20 floor, pre-committed at
  Annex A freeze). The D4.6 reject-replay harness therefore remains
  unexercised on live data.

## Incident + watchdog notes

- **First attempt (2026-07-25) aborted** — Anthropic API incident: run 1's
  agent hung 105 min against a 900 s budget because the then-current bash
  watchdog's one-shot pgid kill failed silently; jq block buffering also hid
  every result line from the driver log. Both fixed (PR #800:
  `kill_agent_tree` verify-and-escalate, deadline poll in parent,
  `jq --unbuffered`). Aborted epoch deleted; its driver log preserved in the
  PR #800 discussion.
- **This rerun executed under the hardened watchdog and verified it live**:
  W3-004 run 3's attempts 1–2 hung (incident tail), the watchdog fired at
  600 s both times (`watchdog: agent exceeded 600s; killing pid … and
  descendants` in the driver log), killed the full tree (zero surviving
  processes verified post-epoch), and the invalid-run retry cap then counted
  the run as failure. The spend guarantee held under real conditions — the
  evidence the #800 review listed as outstanding.
- Three other transient `"api error"` attempts (W2-005 runs 2–3, W3-004 run
  3 attempt 0) were absorbed by the invalid-run retry and completed valid on
  a later attempt.

## Honest limits

- The whole-file mechanism delivers N logical edits as one write, so
  write-attempt counts sit at 1/iteration by design; "multi-edit" is
  substantiated by per-write declaration counts (`edit_target_ids`), not
  attempt counts.
- 0 rejects and 0 heals mean the reject and heal paths were not exercised by
  live agent traffic (they are unit/NDJSON-tested); M-L2 = 100 % is a
  ceiling observation at authorable-fixture difficulty, consistent with the
  M2 dry-run's floor-bound itg finding, and carries no comparative claim.
- W3-004 run 3's invalidity is attributable to the API incident, not the
  write path (no MCP write was attempted in the hung sessions); it still
  counts as task failure in the rollup per the pre-registered rule.
