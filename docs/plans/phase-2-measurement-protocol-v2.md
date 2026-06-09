# Phase 2 Measurement Protocol — v2

**Status:** Pre-registration document — supersedes
[`phase-2-measurement-protocol.md`](phase-2-measurement-protocol.md).
**RFC:** [path-2-drop-ids-v5.md](path-2-drop-ids-v5.md) (the RFC) and
        [path-2-drop-ids-v6-implementation.md](path-2-drop-ids-v6-implementation.md) (the implementation plan).
**Authored under:** RFC §10.5 — amendment authority. v1 was the original
pre-registration; v2 is required because v1's substrate enumeration
included 24 workspace fixtures that lacked a runnable task contract
(empirical: 72 of 90 dry-run records carried `harness_error="no_task_contract"`,
80% noise). Running v1 as-pre-registered would have collapsed statistical
power. See [`phase-2-validation-criteria.md`](phase-2-validation-criteria.md)
§8.2 for the empirical evidence.

> **Pre-registration discipline (RFC §10.5):** Once this document is
> merged, it MUST NOT be edited. Any change invalidates the run. If a
> further protocol change is required after merge, author
> `phase-2-measurement-protocol-v3.md` and re-run the gate against the
> new protocol.

> **Supersession:** v1's §1 fixture list is replaced by the trial
> manifest at `tests/E2E/agent-tasks/phase-2-gate-tasks.txt` (see §1
> below). All other sections of v1 carry forward by reference unless
> explicitly overridden here. Where v1 and v2 disagree, v2 governs.

---

## §1 — Pre-registered trial manifest

The §10 gate runs against **30 trials**, enumerated in the manifest
file at `tests/E2E/agent-tasks/phase-2-gate-tasks.txt`. The manifest
is the immutable record of which work the agent is asked to do.

Each trial has one of two kinds:

- **`task`** — a row of the form `task:<category>/<task_dir>`. The
  driver reads `tests/E2E/agent-tasks/tasks/<category>/<task_dir>/task.json`,
  uses its `prompt` field as the agent instructions, and materialises
  the workspace by copying the fixture named in `task.json`'s `fixture`
  field. Compilation success is the canonical pass signal
  (`verification.compilation.mustSucceed`).
- **`template`** — a row of the form `template:<template_dir>`. The
  driver uses `tests/E2E/agent-tasks/templates/path-2-gate/<template_dir>/`
  which is a self-contained task with `task.md`, `setup/`, `expected/`,
  and `acceptance.sh`. The acceptance script's exit code is the
  canonical pass signal.

### 1.1 The 30 trials

24 stratified `task:` trials covering 17 of the 19 task categories in
`tests/E2E/agent-tasks/tasks/`, plus 6 purpose-built `template:` trials
from path-2-gate authoring:

| # | Trial id | Kind | Workspace |
|---|----------|------|-----------|
| 1 | `task:type-system/01_option_some_return` | task | `basic-calor-project` |
| 2 | `task:type-system/04_result_err_return` | task | `basic-calor-project` |
| 3 | `task:string-operations/01_length_upper_lower` | task | `basic-calor-project` |
| 4 | `task:string-operations/04_regex_operations` | task | `basic-calor-project` |
| 5 | `task:refactoring-benchmark/refactor-extract-effects-calor` | task | `refactor-extract-calor` |
| 6 | `task:refactoring-benchmark/refactor-extract-effects-csharp` | task | `refactor-extract-csharp` |
| 7 | `task:refactoring-benchmark/refactor-rename-shadow-calor` | task | `refactor-rename-calor` |
| 8 | `task:refactoring-benchmark/refactor-rename-shadow-csharp` | task | `refactor-rename-csharp` |
| 9 | `task:refactoring/01_extract_pure_function` | task | `refactoring-impure` |
| 10 | `task:refactoring/06_fix_contract_violation` | task | `buggy-contracts` |
| 11 | `task:pattern-matching/02_relational_patterns` | task | `basic-calor-project` |
| 12 | `task:pattern-matching/04_option_some_matching` | task | `basic-calor-project` |
| 13 | `task:oop-features/01_class_definition` | task | `oop-project` |
| 14 | `task:oop-features/04_property` | task | `oop-project` |
| 15 | `task:logic-implementation/01_factorial` | task | `basic-calor-project` |
| 16 | `task:logic-implementation/02_clamp` | task | `basic-calor-project` |
| 17 | `task:lambdas-delegates/02_block_lambda` | task | `basic-calor-project` |
| 18 | `task:generics/02_generic_class` | task | `generics-project` |
| 19 | `task:enums/01_enum_definition` | task | `enums-project` |
| 20 | `task:effects-system/02_console_read` | task | `effects-project` |
| 21 | `task:effects-system/06_multiple_effects` | task | `effects-project` |
| 22 | `task:contract-writing/02_postcondition` | task | `basic-calor-project` |
| 23 | `task:contract-writing/05_loop_invariant` | task | `basic-calor-project` |
| 24 | `task:collections/01_list_creation` | task | `collections-project` |
| 25 | `template:task-01-multi-function-refactor` | template | (own) |
| 26 | `template:task-02-add-db-effect` | template | (own) |
| 27 | `template:task-03-privacy-change` | template | (own) |
| 28 | `template:task-04-add-postcondition` | template | (own) |
| 29 | `template:task-05-loop-restructure` | template | (own) |
| 30 | `template:task-06-error-handling` | template | (own) |

### 1.2 Stratification rationale

The 24 `task:` rows were selected to span Calor's language surface as
broadly as possible while staying within the original 30-trial sample
size (v1 §1.1 + §1.2 = 24 + 6). Two `task:` rows from each of the most
populated categories (`type-system`, `string-operations`,
`refactoring-benchmark` × calor/csharp pairs, `refactoring`,
`pattern-matching`, `oop-features`, `logic-implementation`,
`effects-system`, `contract-writing`); one `task:` row each from the
smaller categories.

Excluded with explicit rationale:

- `github-projects/` (4 tasks) — network-dependent; would make the gate
  non-reproducible if upstream repos shift.
- `calor-idioms/` (2 tasks) — surface is covered by
  `refactoring/01_extract_pure_function` and the `Option`/`Result` tasks
  in `type-system/`.
- `async-functions/`, `control-flow/`, `advanced-contracts/`,
  `basic-syntax/`, `lambdas-delegates/` 1, 3, 4 — categories well
  represented by the 6 path-2-gate templates and by adjacent task
  selections.

### 1.3 Arm SHAs (filled at gate kickoff)

Per v6 §4.3, arm SHAs are frozen at kickoff, not at protocol-author
time. The driver writes the resolved SHAs to the bottom of this file
in the `Arm SHAs frozen at gate kickoff:` block when the gate starts.

---

## §2 — Sample size and statistical power

**Unchanged from v1:** 30 trials × 10 seeds × 3 arms = **900 trials**.

Because the trial set is the same size, the v1 power analysis (RFC §10.1)
carries forward unchanged. Bonferroni-corrected α for the planned
pairwise contrasts (B vs A, C vs A, C vs B) remains at v1's value.

The substantive change is that all 900 trials now actually produce
agent-driven measurements (rather than ~80% being substrate-error
records that the analyser must filter out). v1's effective sample size
was ~180 useful records; v2's is the full 900.

---

## §3 — Metrics and acceptance criteria

**Unchanged from v1** except for one clarification:

### 3.1 `success` (criterion 1)

For `task:` trials, `success=true` iff `calor build` exits 0 in the
post-agent workspace. For `template:` trials, `success=true` iff the
template's `acceptance.sh <work_dir>` exits 0. Both signals are
captured by the harness adapter at `tests/E2E/agent-tasks/run.sh`.

### 3.2 `identity_preservation_errors` (criterion 4)

The v1 metric was a single count: files that exist in the post-agent
workspace but did not exist in `setup/`. v2 strengthens this to: files
**added, modified, or removed** between the pre-agent and post-agent
workspace snapshots, *excluding* the documented edit target. Because
the documented edit target is currently not extractable as structured
data from task.md / task.json prose, v2 §3.2 is an **upper bound** on
identity churn (i.e. it over-reports). The analyser treats this as a
regression signal weighted accordingly, not a binary failure.

A v3 of this metric should consume an explicit `targets:` field added
to task.json / task.md front-matter and compute true churn. That work
is deferred to a future iteration and does not block this gate.

### 3.3 All other criteria

Inherit unchanged from v1 §3.

---

## §4 — Arm definitions and SHA freezing

**Unchanged from v1 §4** except that Arm B is fixed at
`release/phase-1-only`, branched from `main` at v1-authoring time and
pinned to the 4 commits implementing PR-1a..1h with a Phase-2-stub
commit so the build is green without Phase 2 types. The branch is
pushed to `origin/release/phase-1-only`.

---

## §§5–9 — Pre-flight, dispatch, polling, recovery, reporting

**Inherit unchanged from v1.** The operational scripts referenced in
those sections (`scripts/run_phase_2_gate.py`,
`scripts/analyze_gate_results.py`) work with the v2 trial-based
substrate transparently — the driver was refactored to read the
manifest in v2's §1, and the analyser is metric-driven (it does not
care whether records came from `task:` or `template:` substrate).

---

## §10 — Kickoff procedure (v2 supersedes v1 §10.1.a CLI)

### 10.1.a Kickoff command

Replace the v1 §10.1.a kickoff CLI with:

```bash
python3 scripts/run_phase_2_gate.py \
    --pre-reg docs/plans/phase-2-measurement-protocol-v2.md \
    --output-dir results/$(date -u +%Y%m%dT%H%MZ)-gate/ \
    --arm-a-ref main \
    --arm-b-ref release/phase-1-only \
    --arm-c-ref release/0.x+1 \
    --model "<pinned-string>"
```

All other §10 items (prerequisites, operator walkthrough, post-run
analysis) carry forward from v1.

### 10.1.b Hard prerequisites (delta from v1)

In addition to v1's 9 prerequisites, v2 requires:

10. **Trial manifest committed at the SHA used for Arm C kickoff.**
    `tests/E2E/agent-tasks/phase-2-gate-tasks.txt` is part of the
    pre-registration; if it differs between Arm A SHA and Arm C SHA the
    gate is invalid (driver reads from CWD, not from a per-arm
    checkout). Resolution: this manifest is a docs-equivalent artifact
    and must land on `main` together with this document.

11. **Adapter contract** `tests/E2E/agent-tasks/run.sh` accepts the
    `--trial-id / --kind / --task-dir [--fixture-dir]` argument shape.
    Driver and adapter are version-pinned together; any change to
    either after kickoff invalidates the run.

---

## §11 — Validation snapshot (informational)

As of authoring time, every blocker in
`phase-2-validation-criteria.md` §8.1 that the engineering side can
resolve has been resolved (substrate gap, adapter wiring, Arm B
branch, driver propagation of `harness_error`). The remaining items
are operator decisions: spend authorisation, model pin, monitoring
cadence, solo-mode sign-off.
