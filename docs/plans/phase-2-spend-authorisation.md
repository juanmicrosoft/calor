# Phase 2 §10 Gate — Spend Authorisation

**Status: DRAFT — UNSIGNED.** The gate kickoff command (protocol v2 §10.1.a)
MUST NOT be executed until this document carries the maintainer's explicit
authorisation in §6 below, captured as a signed git commit (see §6 for the
exact discipline).

This file is part of the immutable pre-registration. Once signed, do not
edit. To revise the budget, abort thresholds, or per-trial estimate after
signing, follow the v6 §9.c retry-sign-off procedure: write
`docs/plans/phase-2-measurement-protocol-retry-rationale.md` and start a
fresh 24-hour cool-off.

## §1 — What this document authorises

Spending up to the budget ceiling in §3 below on Anthropic API calls,
through the operator's Claude Code CLI subscription, to drive the 900-run
§10 gate measurement specified in
[`phase-2-measurement-protocol-v2.md`](phase-2-measurement-protocol-v2.md).

The spend is irreversible. Anthropic charges per API call regardless of
whether the gate produces interpretable results, regardless of whether
the maintainer subsequently decides not to merge Phase 2, and regardless
of whether a protocol violation forces a retry under v6 §9.c.

## §2 — Per-trial calibration (run BEFORE signing §6)

Before authorising the full 900-run gate, the operator MUST measure
actual cost on three representative trials and update this section
with the observed numbers. This calibration defends against an order-
of-magnitude estimation error.

**Procedure:**

```bash
# Pick three trials spanning the difficulty spectrum:
#   - one cheap (template:test-utility-conversion)
#   - one medium (task:simple-cs-to-calor/basic-class)
#   - one expensive (task:integration-or-e2e equivalent)
#
# For each, run a single arm × single seed in dry-run-mode=false
# (i.e., real money) and read the per-trial cost from runs.jsonl.

python3 scripts/run_phase_2_gate.py \
    --output-dir results/calibration-<DATE> \
    --pre-reg docs/plans/phase-2-measurement-protocol-v2.md \
    --tasks-manifest /tmp/calibration-tasks.txt \
    --arm-a-ref main --arm-b-ref main --arm-c-ref main \
    --model claude-sonnet-4-6 \
    --seeds 0

# Inspect each trial's $ cost by parsing the agent.stdout JSON's
# .total_cost_usd field (Claude Code CLI emits this in
# --output-format=json mode).
```

**Calibration results — 2026-05-27 (calor@`9b7db39`, claude-sonnet-4-6):**

Observed three representative tasks × three arms × one seed = 9 real LLM
invocations (all three arms pinned to `main@8e72278f`, which is the
same SHA pre/post-sign — variance here is pure LLM stochasticity, not
treatment effect). All raw artefacts live under
`results/calibration-2026-05-27/runs/`. The driver was invoked with
the `--calibration` flag added by `fffc446` so the discovered manifest
is 3 trials, not the immutable 30 of the real Tier 3 protocol.

| Trial id                                                | arm |   $ cost | turns | output tokens | result |
|---------------------------------------------------------|----:|---------:|------:|--------------:|:------:|
| `task:type-system/01_option_some_return`                |  A  | $0.1099  |   4   |         2,497 | fail*  |
| `task:type-system/01_option_some_return`                |  B  | $0.1064  |   4   |         2,634 | pass   |
| `task:type-system/01_option_some_return`                |  C  | $0.0996  |   4   |         1,673 | fail*  |
| `task:string-operations/01_length_upper_lower`          |  A  | $0.1054  |   4   |         2,162 | fail*  |
| `task:string-operations/01_length_upper_lower`          |  B  | $0.1028  |   4   |         1,627 | pass   |
| `task:string-operations/01_length_upper_lower`          |  C  | $0.0921  |   4   |         1,459 | pass   |
| `task:refactoring/01_extract_pure_function`             |  A  | $0.0852  |   4   |         1,579 | pass   |
| `task:refactoring/01_extract_pure_function`             |  B  | $0.1418  |   6   |         3,387 | pass   |
| `task:refactoring/01_extract_pure_function`             |  C  | $0.0954  |   4   |         2,023 | pass   |
| **Mean per-trial cost**                                 |  —  | **$0.106** |  —  |             — |   —    |
| **Median per-trial cost**                               |  —  | **$0.103** |  —  |             — |   —    |
| **Projected 900-trial total (mean × 900)**              |  —  |  **$95** |   —   |             — |   —    |
| **Projected 900-trial total (95th-percentile × 900)**   |  —  | **$128** |   —   |             — |   —    |

\* The three `fail*` outcomes are agent-side: the LLM emitted invalid
Calor (e.g. `Calor0100: Expected EndIf but found OpenParen`,
`Calor0106: Unknown operator 'some'`) and the compiler correctly
rejected it. This is the legitimate variance the §10 gate is designed
to measure, not an apparatus bug. The earlier calibration attempt
(`results/calibration-2026-05-27/runs.jsonl.broken-verify`-style logs,
before commit `9b7db39`) returned `success=False` for all 9 trials
because the adapter invoked `calor build` (a nonexistent subcommand);
that bug was found by this calibration and fixed, which is exactly the
defence the §2 calibration exists to provide.

**Projection vs. budget envelope:**

The mean per-trial cost is $0.106, **38× lower than the $4.00
placeholder** in `phase-2-gate-config.json`. The 95th-percentile per-trial
cost ($0.142) projects to $128 over 900 trials, **39× under the $5,000
hard ceiling** and **23× under the $3,000 soft target**. Even with a
10× safety factor for harder integration-tier tasks not represented
in this calibration sample, the projection ($1,280) sits comfortably
below the soft target.

Operator MAY proceed to §6 signing on this projection.

Operator MAY update `phase-2-gate-config.json` `per_trial_estimate_usd`
from `4.00` to `0.15` (mean × ~1.4 safety factor) so the monitoring
tick `cost_burn_rate_per_trial_usd` abort trigger fires earlier on real
cost runaway. Doing so is a config-only change and does NOT require
v6 §9.c retry-sign-off (per `phase-2-gate-config.json` `_meta`).

Calibration spend so far: **$1.44** ($0.48 from the broken-verify
discovery run + $0.96 from the post-fix verification run). Both are
sunk costs that materially de-risked the Tier 3 spend by uncovering
the `calor build` apparatus bug.

## §3 — Budget envelope

The numbers below match `phase-2-gate-config.json`.

| Parameter                                   | Value      |
|---------------------------------------------|-----------:|
| Soft target (planning estimate, RFC §10.1)  | $3,000     |
| Hard ceiling (driver halts above this)      | $5,000     |
| Per-trial estimate (placeholder, see §2)    | $4.00      |
| Projected 900-trial total at placeholder    | $3,600     |

## §4 — Abort triggers (the monitor enforces these)

The 4-hour monitoring tick (`scripts/monitor_phase_2_gate.ps1` and
`.sh`) parses `results/<run>/runs.jsonl` against the thresholds in
`phase-2-gate-config.json` `abort_triggers`. If any threshold trips,
the monitor writes a halt-recommended record to `monitor.log` and
exits non-zero, which the operator's scheduler surfaces as an alert.

The operator MUST honour halt-recommendations within one monitoring
cycle (4 hours). If the operator overrides a halt-recommendation and
continues the gate, the override decision MUST be recorded as a
comment on the §9.a kickoff issue (created in §5) with the operator's
reasoning. This creates an audit trail equivalent to v6 §6.1's
"investigate first; do not auto-revert" discipline applied to spend.

## §5 — Kickoff cool-off (§9.a pattern applied to Tier 3 kickoff)

Per v6 §9, solo-mode operators defend against motivated reasoning by
mirroring §9.a's 4-hour written-before-action ritual at any irreversible
commitment point. The §10 gate kickoff is the largest irreversible
commitment in the entire Phase 2 plan; v6 §9.c formally applies only
to *retry* sign-off, but its spirit applies here as well.

**Procedure (operator runs ONCE before signing §6):**

1. Open a GitHub issue titled
   `Phase 2 §10 gate kickoff: solo-mode cool-off — <YYYY-MM-DD>`.
2. In the issue body, paste:
   - The calibration table from §2 (filled in).
   - The arm SHAs that will be passed to the driver.
   - A 200-word written justification answering: *what would prove
     Phase 2 was a mistake to ship, and would the §10 gate detect it?*
3. Wait **at least 4 hours** (one ScheduleWakeup tick in v6 §3.3
   terminology) after the issue body is committed.
4. Re-read the justification with maintainer-hat on. If still convinced,
   add a comment to the issue: `cool-off complete; proceeding to sign
   spend-authorisation.md §6`. Then sign §6 below.
5. After the gate completes (whether it terminates normally or via
   abort trigger), close the issue with a comment summarising what
   was learned.

## §6 — Operator signature

**Sign by replacing the bracketed values below and committing the file
on `feature/compact-ids-v6` (NOT directly to main) with the commit
message `chore(phase-2): sign spend-authorisation`. Then push and
include the commit SHA in the §5 kickoff issue.**

```
Authorised by:              juanmicrosoft
Authorisation timestamp:    2026-05-27T23:04:00Z
Calibration commit SHA:     94ec5ad
Kickoff issue URL:          https://github.com/juanmicrosoft/calor/issues/623
Cool-off start timestamp:   2026-05-27T22:36:15Z
Cool-off end timestamp:     2026-05-27T23:04:00Z (WAIVED — see issue #623 comment, maintainer override)
Budget ceiling accepted:    $5,000 (matches §3)
Model id accepted:          claude-sonnet-4-6 (matches phase-2-gate-config.json)
```

By signing the operator affirms:

- The calibration in §2 has been performed and the projected total
  is at or below the §3 hard ceiling.
- The Anthropic billing account has sufficient credit / payment
  method for the projected total.
- The monitoring tick (§4) is scheduled and verified by running it
  once manually against a stub `runs.jsonl`.
- The §5 cool-off was actually observed (timestamps are real, not
  back-dated).
- The maintainer accepts that this spend may produce a null result
  (no clear winner) and that a null result is still a valid outcome
  for the merge decision in `phase-2-validation-criteria.md` §4.

## §7 — Cross-references

- [`phase-2-measurement-protocol-v2.md`](phase-2-measurement-protocol-v2.md) — the
  pre-registered experimental design this spend funds.
- [`phase-2-validation-criteria.md`](phase-2-validation-criteria.md) §8.1 — the
  readiness matrix that gates kickoff; this document fills row 7.
- [`phase-2-gate-config.json`](phase-2-gate-config.json) — machine-readable
  twin of §3 and §4 used by the monitoring tick.
- [`phase-2-monitoring-tick.md`](phase-2-monitoring-tick.md) — how to schedule
  the §4 monitor on Windows and POSIX.
- [`path-2-drop-ids-v6-implementation.md`](path-2-drop-ids-v6-implementation.md)
  §9.a and §9.c — solo-mode disciplines this document inherits.
