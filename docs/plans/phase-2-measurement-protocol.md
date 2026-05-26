# Phase 2 Measurement Protocol

**Status:** Pre-registration document.
**RFC:** [path-2-drop-ids-v5.md](path-2-drop-ids-v5.md) (the RFC) and
        [path-2-drop-ids-v6-implementation.md](path-2-drop-ids-v6-implementation.md) (the implementation plan).
**Authored under:** RFC §10.2 — *"before any Phase 2 implementation code
is written, this document must be committed."*

> **Pre-registration discipline (RFC §10.5):** Once this document is
> merged, it MUST NOT be edited. Any change invalidates the run. If a
> protocol change is required after merge, author
> `phase-2-measurement-protocol-v2.md` and re-run the gate against the
> new protocol.

---

## §1 — Task list with commit hashes

The §10 gate runs against **30 fixtures = 24 existing + 6 new**, per
v6 §4.0. Commit SHAs are filled at gate kickoff, not at PR-0b authoring
time, so the SHAs reflect the actual workspace state at run start.

### 1.1 Existing tasks (24, paths under `tests/E2E/agent-tasks/fixtures/`)

| Fixture | SHA @ gate kickoff |
|---------|--------------------|
| `advanced-calor-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `async-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `basic-calor-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `buggy-contracts/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `collections-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `contracts-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `effects-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `enums-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `generics-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `oop-project/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-contract-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-contract-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-extract-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-extract-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-inline-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-inline-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-move-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-move-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-rename-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-rename-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-signature-calor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-signature-csharp/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactor-target/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `refactoring-impure/` | `<commit-sha-to-fill-at-gate-kickoff>` |

### 1.2 New tasks (6, paths under `tests/E2E/agent-tasks/templates/path-2-gate/`)

| Fixture | SHA @ gate kickoff |
|---------|--------------------|
| `task-01-multi-function-refactor/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `task-02-add-db-effect/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `task-03-privacy-change/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `task-04-add-postcondition/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `task-05-loop-restructure/` | `<commit-sha-to-fill-at-gate-kickoff>` |
| `task-06-error-handling/` | `<commit-sha-to-fill-at-gate-kickoff>` |

The §10 gate runs against `fixtures/` ∪ `templates/path-2-gate/` =
**30 fixtures × 10 runs × 3 arms = 900 runs**, matching RFC §10.1.

---

## §2 — New task authoring artifacts

Each new task in `templates/path-2-gate/task-NN-<theme>/` contains:

- `task.md` — prompt given to the agent
- `setup/` — pre-existing `.calr` files in the workspace at task start
- `expected/` — post-task expected state (deterministic acceptance check)
- `acceptance.sh` — exit-0-on-success script that diffs agent output

Per v6 §5.1, each task must:

- Be **solvable today** by a baseline agent (Arm A) at **50–90%** success
  across 10 seeds (this is the recalibrated baseline range; tasks above
  90% must be made harder, tasks below 50% must be made easier or
  excluded).
- Be **multi-edit**: ≥3 files OR ≥5 distinct locations.
- Touch a **cross-reference**: rename + update callers, or add a contract
  that references an existing function ID, etc.
- Have a **deterministic** acceptance script (no LLM-as-judge).
- Have an **expected median turn count of 10–30** for a baseline agent.
- Be **authored independently per arm**: the prompt does not reference
  structural IDs in any arm; the difference between arms is the
  *workspace state*, not the *task instructions*.

---

## §3 — Implementation flag (arm SHAs frozen at gate kickoff)

Per v6 §4.3, arm SHAs are recorded **at gate kickoff**, not relative to
PR events that may complete mid-experiment. `scripts/run_phase_2_gate.py`
records all three SHAs at start and writes them here before any run
executes. Any change to any SHA mid-run invalidates the run per RFC §10.5.

```
Arm A (today/baseline):     main branch @ <SHA recorded at gate kickoff>
                            (latest 0.x pre-release tag's commit)
Arm B (Phase 1 only):       release/0.x+1 @ <SHA recorded at gate kickoff>
                            (commit of the most recent Phase 1 merge;
                            Phase 2 PRs may or may not be merged at gate
                            kickoff, but Arm B does NOT include them)
Arm C (Phase 1 + Phase 2):  release/0.x+1 @ <SHA recorded at gate kickoff>
                            (commit of the most recent Phase 2 merge)
```

---

## §4 — Run protocol

### 4.1 Agent harness invocation

```
./tests/E2E/agent-tasks/run.sh \
    --task <fixture-id> \
    --arm <A|B|C> \
    --seed <n> \
    --model <pinned-string>
```

### 4.2 Model version — pinned

```
Primary:    <model-name> @ <version-string> as of <YYYY-MM-DD>
Fallback A: <model-name> @ <version-string> as of <YYYY-MM-DD>
Fallback B: <model-name> @ <version-string> as of <YYYY-MM-DD>
```

Per v6 §4.4: if the pinned (primary) model becomes unavailable during
the gate run (deprecation, API outage, rate-limit lock, account limit),
the run halts. The next pinned model is selected from the fallback list
above (authored alongside this document, frozen at gate kickoff, NOT
edited mid-run).

A model switch **invalidates any in-flight runs** (run counter resets
to 0 for the new model). Sign-off on a model switch follows v6 §9.c
solo-mode rule for retry sign-off.

### 4.3 Random seeds

Seeds `1..10` per `(task, arm)` combination. Recorded in the per-run
log. The harness MUST accept and respect the seed.

### 4.4 Concurrency

Runs are sequential within a `(task, arm)` tuple; parallelism is allowed
across tuples. Concurrency limit is set per provider rate limits at gate
kickoff.

---

## §5 — Data schema (recorded per run)

Each run produces one JSON record:

```json
{
  "task_id": "advanced-calor-project",
  "arm": "A",
  "seed": 1,
  "model": "<pinned-string>",
  "started_at": "2026-MM-DDTHH:MM:SSZ",
  "ended_at": "2026-MM-DDTHH:MM:SSZ",
  "success": true,
  "turn_count": 17,
  "total_output_tokens": 8421,
  "identity_preservation_errors": 0,
  "edit_correctness_errors": 0,
  "harness_crash": false,
  "raw_log_path": "results/<arm>/<task_id>/seed-<n>/log.jsonl"
}
```

Aggregate file: `results/runs.jsonl` (one line per run).

---

## §6 — Analysis pipeline

- **Library versions (pinned at gate kickoff):**
  ```
  scipy==<version-to-fill>
  pandas==<version-to-fill>
  numpy==<version-to-fill>
  python==<version-to-fill, ≥3.11>
  ```
- **Analysis seed:** `42` (only matters for bootstrap CI computation).
- **Code path:** `scripts/analyze_gate_results.py` (committed alongside
  this document).
- **Outputs:** `docs/plans/phase-2-measurement-results.md` summarizing
  all 4 kill criteria per RFC §10.3.

---

## §7 — Pass/fail thresholds (verbatim from RFC §10.3)

All four kill criteria must be **green** for Phase 2 to ship. Bonferroni
correction: α' = 0.05 / 4 = **0.0125**.

| # | Criterion | Pass condition |
|---|-----------|----------------|
| 1 | Success-rate non-inferiority | McNemar **p > 0.0125** comparing Arm C vs Arm A on per-task success; Arm C success rate is not statistically worse than Arm A. |
| 2 | Identity-preservation non-regression | Wilcoxon **p > 0.0125** on per-run identity-preservation error counts; Arm C does not show more identity errors than Arm A. |
| 3 | Material agent benefit on turn-count OR token-count | (Turn-count median reduction ≥ 10% **AND** Wilcoxon **p < 0.0125** **AND** **\|Cliff's δ\| ≥ 0.33**) **OR** (Token-count median reduction ≥ 15% **AND** Wilcoxon **p < 0.0125** **AND** **\|Cliff's δ\| ≥ 0.33**). |
| 4 | Phase 2 distinguishable from Phase 1 alone | On criterion (3)'s metric, Arm C vs Arm B Wilcoxon **p < 0.0125**. Phase 2 contributes additional benefit beyond Phase 1's structural-ID drop. |

A **single red criterion** → Phase 2 reverted; ship Phase 1 alone per
RFC §11.

---

## §8 — Reporting commitment (RFC §16.E item 8)

`docs/plans/phase-2-measurement-results.md` WILL be written and committed
**regardless of pass or fail**. The doc reports all four criteria with
their p-values, effect sizes, sample sizes, and ship/no-ship decision.
This commitment is non-negotiable; suppression of negative results is
the failure mode pre-registration exists to prevent.

---

## §9 — Retry policy (RFC §10.5)

- **First failure (protocol violation, not criterion failure):** one
  retry is covered by reserve budget. Sign-off authority: maintainer.
  See v6 §9.c for the solo-mode sign-off ritual (written justification,
  external read, ≥24h wait).
- **Second failure or further retries:** require re-pre-registration as
  `phase-2-measurement-protocol-v2.md`. The new protocol document may
  reduce sample size, change model, or change kill criteria — but must
  itself be merged before any run executes against it.
- **Criterion failure (gate FAIL):** is NOT a retry trigger. Per
  RFC §10.3, a red criterion → Phase 2 reverted; do not retry to "see
  if it passes next time."

---

## §10 — Monitoring during the run (v6 §3.3)

### 10.1 Pre-flight check

Before `scripts/run_phase_2_gate.py` invokes the first run, it verifies:

- The 4-hour monitoring tick is scheduled.
- The pinned model is currently available (1-prompt sanity ping; if
  fails, halt and invoke §10.5).
- Disk space, network connectivity, run-log directory writable.
- All 30 fixtures resolve to readable directories.
- The three arm SHAs resolve to checkoutable commits.

### 10.1.a Real-run kickoff command

The exact command for the real gate run (after the pre-registration is
locked and the maintainer is ready to spend the LLM budget):

```bash
python3 scripts/run_phase_2_gate.py \
    --arm-a-ref main \
    --arm-b-ref release/0.x+1 \
    --arm-c-ref release/0.x+1 \
    --model <pinned-string-required> \
    --output-dir results/gate-$(date -u +%Y%m%dT%H%M%SZ) \
    --seeds 1,2,3,4,5,6,7,8,9,10
# ... 4–5 day run window, ~900 LLM invocations ...
python3 scripts/analyze_gate_results.py \
    --runs results/gate-<timestamp>/runs.jsonl \
    --output docs/plans/phase-2-measurement-results.md
echo "gate decision: exit code $? (0 = PASS, 1 = FAIL)"
```

### 10.1.b Dry-run pipeline verification (CI-cheap)

The full collect → dispatch → JSONL → analyze → markdown → exit-code
pipeline can be exercised without LLM spend via:

```bash
python3 scripts/smoke_phase_2_gate_dryrun.py
```

This runs `--dry-run --simulate gate-pass` and asserts the analyzer
exits 0, then `--simulate gate-fail` and asserts exit 1. It uses
seeded deterministic synthetic records (see ``_synth_record`` in
``scripts/run_phase_2_gate.py``) so two CI invocations produce
identical reports. Total wall time: a few seconds.

This is **not** a substitute for the real run; it is a regression test
that the gate machinery itself is plumbed correctly.

### 10.2 Scheduled polling

Every **4 hours** during the 4–5 day run window, the agent (or
maintainer) inspects the run log for:

- Protocol violations (harness crash, fixture mutation, log gaps).
- Model API errors.
- Throughput drift (runs/hour < expected).

On any violation, halt and trigger §10.5 retry per §9.

### 10.3 Webhook alerting (optional addition)

`scripts/run_phase_2_gate.py` MAY additionally post to a webhook on
protocol violation for faster than 4-hour response. Webhook alerting is
an **addition**, not a substitute (webhook outages would otherwise
silently lose alerts).

### 10.4 Solo-mode (v6 §9.a)

If the agent and maintainer are the same person, Tier 2 red triage uses
the maintainer-hat ritual:

1. Open a GitHub issue `Tier 2 triage: <phase> <date>`.
2. Paste output and write triage reasoning **before** resolving.
3. Wait ≥ 4 hours (or 1 ScheduleWakeup tick).
4. Act, close issue with action-taken comment.

---

## §11 — What this document does NOT cover

- Compiler internals — see RFC §5–§9.
- Per-PR self-verification — see v6 §3 (Tier 1 + Tier 2 harness).
- Tier 4 post-ship monitoring — see RFC §10.6 and v6 §9.b.
- Code review process — see [`docs/process/rfc-review-checklist.md`](../process/rfc-review-checklist.md).
- Rollback playbook — see v6 §6.
