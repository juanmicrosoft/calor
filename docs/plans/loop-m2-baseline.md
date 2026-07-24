# Loop plan M2 — baseline, telemetry, and the Call 1 record

**Status:** M2 execution record (loop plan v0.9, Draft v2.1 — PR #747).
**Date:** 2026-07-23.

## D4.3a — baseline build archived

Annotated tag **`loop-baseline-ws1`** → `4f235cdc` (post-#757 main), pushed to
origin. This is the WS1-only pre-improvement build: envelope schema v1.1 +
ProofOutcome choke point + M-E1 = 100 % sweep complete; no WS2/WS3 code.
Pins recorded in the tag message: version 0.8.0, .NET SDK 10.0.100
(rollForward latestMinor), Z3 4.15.7, Roslyn 5.3.0.

Per the plan's measurement protocol this build is **arm A at M5, run
simultaneously** with the treatment build — never compared longitudinally.
The M2 threshold epoch runs on it for shakedown + threshold-freezing only.

## Call 1 input — E1 verdict already exists: H1 KILLED

`machine-zone.md` §13 records E1a's outcome: the in-context exemplar arm
collapsed the green-field edit-tax ratio by **55 % pooled** — far past the
pre-registered 30 % kill rule. H1 (a text-serialization tax that structured
edits would eliminate) is dead.

Consequences that were **pre-committed** in the loop plan (§2 scope gate,
§6.1) and therefore apply without renegotiation:

- **WS2 descopes before it is built**: D2.2 (`calor_get_node`) and D2.3
  (`calor_edit_apply` node-addressed) are dropped. D2.4 (whole-file
  transactional check-then-apply) and D2.5 (write-path robustness:
  fault-tolerant parsing + canonical-formatter auto-heal) proceed.
- **PP-L3 never runs**; its miss-path outcome (node addressing demoted to
  navigation-only) is adopted at zero measurement cost.
- M3's calendar box shrinks accordingly (the 6–8 wk sizing was dominated by
  D2.1–D2.3; D2.1 project sessions are still required for D2.4 and WS3).
- The D4.2 arm-constraint capability loses its PP-L3 consumer; it is kept
  (cheaply) because the mcp-file-vs-raw split remains observationally useful
  and M-L2 splits on edit mechanism.

Call 1's remaining half is the **D4.5 feasibility dry-run** (below): whether
the PP-L4/PP-L5 `[P]` thresholds are decidable at authorized spend. Call 1
closes when the dry-run numbers are in and the thresholds are frozen via the
gates-doc annex (D4.4).

## D4.1 / D4.2 — telemetry schema and harness integration

Schema: `bench/phase0-agent-native/loop-telemetry-schema.md` (+ JSON Schema
`loop-telemetry.schema.json`). Extends the existing `journal.jsonl` shim
stream additively (v2 discriminated on `schema`); reserved-null fields
(`apply_verdict`, `rejected_edit`) exist so the baseline and treatment arms
emit identical shapes — the schema freezes **before** the treatment build
exists. Integration is in `run-pair.sh` (the `dotnet` PATH shim), with the
arm-constraint flag following the `--exemplar` label pattern and the
PreToolUse-hook enforcement precedent.

## D4.5 — feasibility dry-run design (prepared; runs need authorized spend)

**Question:** are the provisional thresholds decidable at authorized spend?
PP-L5's `[P]` is "≥ 15 % fewer median iterations-to-green". Iterations-to-green
is small-integer and censored at 11; machine-zone's measured ledger has
modification tasks at ~1.0× parity, so plausible baseline medians are 2–4 —
at a median of 3, 15 % is 0.45 iterations: **sub-integer**. The dry-run
decides whether the threshold, the task set, or N moves (before freezing —
never after).

**Design** (pattern: `epochs/feasibility-dry-001`, which died mid-flight at a
session limit — rollup.json is empty; rerun under the invalid-run retry logic
that was added since):

- Epoch id: `loop-feasibility-dry-002`. Baseline build (`loop-baseline-ws1`)
  only — this is a variance estimate, not a comparison.
- ≥ 5 gate-eligible pairs × ≥ 5 runs, calor arm, telemetry schema v2 on.
- Outputs: per-pair iterations-to-green mean/SD, pooled distribution,
  censored fraction, per-pair M-L3 numerator/denominator counts (does the
  actionability metric even have enough failing-iteration events to gate on?),
  and a cluster-bootstrap minimal-detectable-effect at the gates-doc §6.1
  rule (one-sided α = 0.05 over pairs).
- Decision rules, pre-stated:
  - MDE ≤ 15 % relative → PP-L5 threshold freezes as written.
  - MDE > 15 % → move to tokens-to-green as the primary PP-L5 measure
    (continuous, not integer-censored) or raise N; re-estimate; freeze.
  - M-L3 denominator < 20 failing iterations across the epoch → PP-L4 is
    reported-not-adjudicated at M5 (mirrors the D4.6 20-reject floor).

**Spend numbers template** (per `phase-2-spend-authorisation.md` conventions —
to be filled from a 3-run calibration before any epoch is authorized):

| Item | Value |
|:-----|:------|
| Calibration trials (cheap/medium/expensive) | $__ / $__ / $__ |
| Median per-run cost | $__ |
| Runs this epoch (pairs × runs) | ≥ 25 |
| Projected epoch cost | $__ |
| Ceiling accepted | $__ (gates doc §6 names $1,500/epoch for Phase-0-style feasibility) |
| Model pin | __ |

No epoch is run until these numbers are entered and authorized — the plan
requires numbers before M2 kickoff of any spend, and this document is the
template, not the authorization.
