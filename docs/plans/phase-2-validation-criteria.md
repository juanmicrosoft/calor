# Phase 2 — Validation Criteria for the §10 Gate

**Audience:** the maintainer (or coding agent) deciding whether to merge
`feature/compact-ids-v6` to `main` and ship Phase 2 to users.
**Companion docs:**
- [path-2-drop-ids-v6-implementation.md](path-2-drop-ids-v6-implementation.md) — overall plan
- [phase-2-measurement-protocol.md](phase-2-measurement-protocol.md) — pre-registration (arm SHAs, fixtures, model pinning, kill criteria source-of-truth)
- [path-2-drop-ids-v5-implementation.md](path-2-drop-ids-v5-implementation.md) — original RFC referenced by v6 calibration deltas

This document operationalises **"the change is a positive for the
language"** into a small set of mechanical, falsifiable checks. Each
check is wired to a script in `scripts/` whose exit code (0/1) is the
canonical signal. No section here describes work that is not also
encoded in code or tests on this branch.

The four §10.3 kill criteria are the authoritative gate. Tiers 1, 2,
and 4 are *necessary preconditions* — they screen out builds whose
basic invariants are broken, so the (expensive) Tier 3 / §10 run can
trust its substrate.

---

## §1 — Mechanical checks (cheap, run before every merge to `main`)

| # | Check | Command | Pass when |
|---|-------|---------|-----------|
| 1 | Build is green on Debug + Release | `dotnet build -c Release` | Exit 0; zero warnings (TWaE is on) |
| 2 | Full xUnit suite is green | `dotnet test --nologo` | All tests pass; only the 2 known-skipped (`SolutionInitializationIntegrationTests`) are skipped |
| 3 | Tier 1 harness is green | `python scripts/verify_phase1.py` | Exit 0; 0 `ast_roundtrip` failures |
| 4 | Round-trip identity on a known corpus | `python scripts/migrator_revert_roundtrip.py` | Original ⇄ Phase 2 mapping round-trips byte-for-byte |
| 5 | Byte preservation on the structural-ID dropper | `python scripts/byte_preservation_check.py` | Every file matches its expected `(N_removed × bytes_per_id)` delta |
| 6 | AST round-trip after compact-id rewrite | `python scripts/ast_roundtrip_check.py` | AST equality (modulo ID payloads) pre/post `calor fix --compact-ids` |

Failing any §1 check **blocks merge** independently of Tier 3 result.

These do not validate the user-visible improvement claim. They validate
that the implementation does not silently corrupt source.

### 1.x — Pre-existing Tier 1 failures (resolved on this branch)

Earlier on this branch `scripts/verify_phase1.py`'s `ast_roundtrip` step
failed on 4 samples that pre-dated `feature/compact-ids-v6` and used
deprecated section markers the current parser does not accept:

| File | Last touched before fix | Failing markers (then) | Replacement (applied) |
|------|--------------------------|-------------------------|-----------------------|
| `samples/TypeSystem/typesystem.calr` | commit `2629449` (Add Z3 static contract verification, #108) | `§SOME`, `§NONE{type=…}` | `§SM`, `§NN{…}` |
| `samples/Verification/mixed-contracts.calr` | commit `2629449` | `§DOC{…}`, `§ELSE{id}`, `§/IF{id}` | strip `§DOC`; `§EL`; `§/I{id}` |
| `samples/Verification/proven-contracts.calr` | commit `2629449` | `§DOC{…}`, `§ELSE{id}`, `§/IF{id}` | strip `§DOC`; `§EL`; `§/I{id}` |
| `samples/Verification/violation-detected.calr` | commit `2629449` | `§DOC{…}` | strip `§DOC` |

All 4 files now compile cleanly and pass `ast_roundtrip` (verified:
Tier 1 reports `[11/11] OK`, total 29.4 s, well under the 60 s budget).
`samples/TypeSystem/typesystem.g.cs` was regenerated against the fixed
source.

**Lexer ↔ suggestion table drift remains.** `SectionMarkerSuggestions.cs`
still lists `DOC` (and emits `Did you mean '§DOC' (Documentation)?` for
unknown `§DOC` input) even though the lexer's keyword dictionary has no
entry for it. The misleading suggestion `§/I (Input parameter)` for
`§/IF` is also wrong (the correct close is `§/I{id}` as the IF terminator).
Fixing the suggestion table is out of scope for v6 ID work and tracked
separately.

**Tier 1 contract on this branch:** the harness must report 0
`ast_roundtrip` failures. Any failure in `byte_preservation`,
`ast_roundtrip`, `migrator_corpus_dryrun`, or `token_delta_spot` is a
regression caused by this branch and blocks merge.

---

## §2 — Corpus checks (Tier 2; minutes, run on PRs to `phase-*`/`release/*`)

Token-delta and corpus-wide invariants per v6 §3.2 / §3.3:

| # | Check | Command | Pass when |
|---|-------|---------|-----------|
| 1 | Migrator dry-run over the full corpus | `python scripts/migrator_corpus_dryrun.py` | Every file produces a deterministic mapping; no warnings other than gap-C string-literal matches (documented & accepted) |
| 2 | Token delta within recalibrated band | `python scripts/token_delta_corpus.py` | Aggregate Δtokens within `9.67 × N_ids ± (band recalibrated on a green build per v6 §3.2)` |
| 3 | Post-ship monitor sanity (no regressions in shipped Phase 1) | `python scripts/phase1_post_ship_monitor.py` | Reports no Phase 1 regression in the rolling corpus |

These also do not validate "language improvement." They validate that
the corpus migration is internally consistent and that Phase 1 has not
regressed during the Phase 2 window.

---

## §3 — The §10 gate (Tier 3) — the only check that validates improvement

The §10 gate is the **single source of truth** for "is this change a
positive for the language." Pre-registration lives in
`docs/plans/phase-2-measurement-protocol.md`. The harness is
`scripts/run_phase_2_gate.py`; the analyser is
`scripts/analyze_gate_results.py`.

Per the pre-registration (§7), four kill criteria must all be **PASS**.
Bonferroni-corrected α′ = 0.05 / 4 = **0.0125**.

### 3.1 The four criteria — machine-readable form

The analyser computes each criterion and surfaces a `passes` bool in
its JSON internal representation. The CLI exit code is 0 only when all
four pass. Operationally:

```bash
python scripts/run_phase_2_gate.py        # writes results/runs.jsonl
python scripts/analyze_gate_results.py    # writes phase-2-measurement-results.md
                                          # exit 0 → SHIP; exit 1 → DO NOT SHIP
echo $?  # canonical pass/fail signal
```

| # | Criterion | Pass condition (verbatim from protocol §7) | Failure mode |
|---|-----------|---------------------------------------------|--------------|
| 1 | Success-rate non-inferiority | McNemar **p > 0.0125** comparing Arm C vs Arm A; Arm C success rate is not statistically worse than Arm A | Phase 2 hurts agents; revert |
| 2 | Identity-preservation non-regression | Wilcoxon **p > 0.0125** on per-run identity errors; Arm C does not regress | Phase 2 corrupts more often; revert |
| 3 | Material agent benefit on turn-count OR token-count | Median reduction ≥ 10% (turns) or ≥ 15% (tokens), Wilcoxon **p < 0.0125**, and **\|Cliff's δ\| ≥ 0.33** on the same metric | Phase 2 delivers no measurable user benefit; revert |
| 4 | Phase 2 distinguishable from Phase 1 alone | On criterion (3)'s metric, Arm C vs Arm B Wilcoxon **p < 0.0125** | Phase 1's structural-ID drop is doing all the work; Phase 2 is redundant; revert |

A **single red criterion** → ship Phase 1 alone per RFC §11. This is
already encoded in `analyze_gate_results.py:434` (`all_pass = c1["passes"] and c2["passes"] and c3["passes"] and c4["passes"]`).

### 3.2 Pre-flight checks (protocol §10.1 and §10.1.a)

Before the first run executes, the harness verifies (encoded in
`run_phase_2_gate.py` and the protocol's §10.1; the operator
checklist that wraps these is in protocol §10.1.a):

1. Monitoring tick scheduled (v6 §3.3.b).
2. Pinned model available (1-prompt ping).
3. Disk space, network, log dir writable.
4. All 30 fixtures resolve to readable dirs.
5. Three arm SHAs resolve to checkoutable commits.

Protocol §10.1.a additionally requires (operator-side, before the
script is invoked): pre-registration document merged to `main`, clean
working tree, §1 mechanical checks all green, a fresh `--output-dir`,
distinct Arm A/B/C refs, the smoke-test path exercised, and (in
solo-mode) the v6 §9.c sign-off ritual completed.

If any pre-flight check fails, the gate halts with non-zero exit
before spending real run budget.

### 3.3 What "validates improvement" means

> *"Improvement"* = criteria 3 AND 4 both PASS, while 1 and 2 do not
> regress (PASS). Criterion 3 captures user-visible benefit
> (turn/token reduction) at a pre-registered effect size; criterion 4
> ensures that benefit is attributable to Phase 2 specifically, not
> already delivered by Phase 1.

No other definition of "improvement" is accepted for the merge
decision. Subjective impressions ("looks nicer," "agents seem
happier") are not sufficient and do not override the analyser's exit
code. This is the discipline the §10 gate exists to enforce.

---

## §4 — Decision matrix at merge time

Let:

- **M** = §1 (mechanical) checks pass
- **C** = §2 (corpus) checks pass
- **G** = §3 (gate) all four criteria pass

| M | C | G | Action |
|---|---|---|--------|
| ✗ | — | — | **DO NOT MERGE.** Fix the build/tests/round-trip before anything else. |
| ✓ | ✗ | — | **DO NOT MERGE.** Corpus invariants broken; investigate before spending gate budget. |
| ✓ | ✓ | ✗ | **DO NOT MERGE Phase 2.** Revert Phase 2 commits; consider shipping Phase 1 alone per RFC §11. |
| ✓ | ✓ | ✓ | **MERGE.** Phase 2 has cleared all four kill criteria. Ship. |

There is no `(✓, ✓, untested)` row by design. A merge to `main` of
Phase 2 changes requires Tier 3 to have run on the actual arm SHAs
that will land. Code-only changes (e.g. fixing a typo in a comment)
on already-validated material may merge without a re-run, at maintainer
discretion.

---

## §5 — When the gate has not yet been run

This is the state of `feature/compact-ids-v6` at the time of writing.
All §1 and §2 checks must be green; §3 is pending. The branch may
remain open and continue receiving fixes; it may **not** be merged to
`main` until §3 reports PASS on the SHAs that will land.

If the maintainer ships an interim release that includes Phase 1 only
(no Phase 2 user-visible behavior), that ships from a different branch
(`release/0.x+1` per the v6 plan), not from `feature/compact-ids-v6`.

### 5.1 — Empirical evidence accumulated so far (no Tier 3 yet)

The §3 gate is the only validator that licenses the merge decision.
Cheaper checks accumulated on this branch *cannot* substitute, but
they do offer early signal on whether the Tier 3 spend is justified.
Current readings:

- **Build & xUnit suite** (§1.1, §1.2): green. 5,427 tests pass on
  the branch tip; 2 known-skipped. +12 PR-2e tests on top of the
  PR-2a..2d baseline.
- **Migrator dry-run** (§2.1, both modes): green on the full
  `tests/` corpus (1,541 files). No file mutates under `--dry-run`
  for `drop-structural-ids` or `compact-ids`.
- **Migrator round-trip** (§1.4, both modes): byte-perfect on 1,541
  files. `original → forward → revert` reproduces every byte for
  both Phase 1 and Phase 2 paths.
- **Token-delta sanity** (§2.2): RFC §16.F predicted **9.67
  tokens/ID** (N=100 RNG-generated IDs, 95% CI 9.30–10.04). The
  corpus has **16,669 ID blocks** across 1,541 files; predicted
  aggregate Δ = 9.67 × 16669 = **161,159 tokens**; measured
  aggregate Δ = **161,189 tokens** — within **0.02%** of the
  prediction. The per-ID estimate generalises to the corpus.

That last reading is the most useful piece of evidence available
without spending Tier 3 budget: it confirms that Phase 1's mechanical
rewrite removes the predicted volume of tokens at the predicted
per-ID rate. It does **not** validate that this reduction translates
into a user-visible benefit — that requires criterion 3 from §3.1.
But it does materially reduce the risk that Tier 3 fails for
"implementation didn't deliver the expected delta" reasons.

The Tier 1 `ast_roundtrip` step now reports `[11/11] OK` (the 4
pre-existing failures listed in §1.x were fixed on this branch); no
additional failures on the branch tip.

---

## §6 — Provenance and auditability

Per RFC §16.E item 8 (encoded in `analyze_gate_results.py:12-13`), the
analyser writes `docs/plans/phase-2-measurement-results.md`
**regardless of pass/fail**. If the gate fails, the results document
records the failure and the decision to revert. The branch's history,
the results document, and the analyser's exit code together form the
auditable record for the ship/no-ship decision.

---

## §7 — Cross-reference to status today

The work to get to a runnable §10 gate is tracked in the v6 plan's PR
breakdown (§2). At the time `feature/compact-ids-v6` is ready for the
gate:

- PR-0a..0e (Phase 0 prerequisites): all merged on this branch.
- PR-1a..1h (Phase 1 implementation): all merged on this branch
  (Phase 1 is independently shippable per v6 §3.5).
- PR-2a..2f (Phase 2 implementation): all merged on this branch.
- §1 + §2 of this document: all green on the branch tip before the
  gate kicks off.

When the maintainer is ready to spend the Tier 3 budget, the kickoff
command is in protocol §10.1.a.

---

## §8 — Gate readiness snapshot (live audit, updated 2026-05-27)

This section captures the live readiness state of the §10 gate. It is
maintained as the answer to "can we run the gate today?" and updated
whenever a blocker is resolved or a new one is discovered. **Until §8.1
shows all green, the kickoff command in protocol §10.1.a will produce
either an invalid run (no statistical power) or no run at all.**

### 8.1 — Prerequisite readiness matrix

| # | Prerequisite (protocol v2 §10.1.a-b) | Status | Owner | Detail |
|---|---------------------------------|--------|-------|--------|
| 1 | Pre-registration doc merged to `main` | 🟡 Pending PR | Maintainer | Protocol v1 + v2 + this doc live on `docs/phase-2-pre-registration`; PR [#621](https://github.com/juanmicrosoft/calor/pull/621) open against `main`. |
| 2 | Working tree clean on Arm C checkout | 🟡 Operator step | Operator | Verified procedurally in §10.1.a; not an artifact gate. |
| 3 | §1 (mechanical) checks green | 🟢 Done | — | Tier 1 `[11/11] OK`, all 5,427 tests pass, both round-trip harnesses byte-perfect on the corpus. |
| 4 | 30 trials resolve to runnable contracts | 🟢 Done | — | Substrate gap resolved by protocol v2 (see §8.2). Driver reads `tests/E2E/agent-tasks/phase-2-gate-tasks.txt` and resolves 24 `task:` + 6 `template:` = 30 trials, all with `task.json`/`task.md` + workspace. |
| 5 | Three **distinct** arm refs (A ≠ B ≠ C) | 🟢 Done | — | `release/phase-1-only` cut and pushed to origin at `82301fe` (3 cherry-picks + 1 stub commit; builds + all tests green). |
| 6 | Agent harness wired to driver | 🟢 Done | — | `tests/E2E/agent-tasks/run.sh` adapter accepts new `--trial-id/--kind/--task-dir [--fixture-dir]` contract; supports both `task:` and `template:` kinds. `scripts/run_phase_2_gate.py` reads manifest, propagates adapter's `harness_error`. End-to-end verified: 90 records via `CALOR_GATE_DRY_RUN=1`, all `harness_crash=false`, all `harness_error="dry_run"` (no substrate noise). |
| 7 | Version-pinned model id + API credentials + budget | 🟡 Config staged, unsigned | Maintainer | `phase-2-gate-config.json` pins `claude-sonnet-4-6` (verified callable, $0.048 for a 'say hi' trial via Claude Code 2.1.144 on 2026-05-27). `phase-2-spend-authorisation.md` template authored; awaits per-trial calibration (§2 of that doc) + operator signature (§6). |
| 8 | 4-hour monitoring tick scheduled | 🟡 Script staged, scheduler not registered | Operator | `scripts/monitor_phase_2_gate.py` authored + verified on stub data (exit 0 green / exit 1 tripped / exit 2 missing); `phase-2-monitoring-tick.md` documents Windows Task Scheduler + cron setup. Operator must register the task on the host that will run kickoff. |
| 9 | Solo-mode kickoff cool-off (§9.a pattern applied to kickoff) | 🟡 Procedure documented, issue not opened | Maintainer | v6 §9.c formally covers *retry* sign-off only, not first-run. `phase-2-spend-authorisation.md` §5 prescribes a §9.a-style 4-hour cool-off issue at kickoff. Issue MUST be opened (and 4h MUST elapse) before signing `phase-2-spend-authorisation.md` §6. |
| 10 | Trial manifest at SHA used for Arm C kickoff (v2 §10.1.b new) | 🟡 Pending PR | Maintainer | Manifest is part of PR [#621](https://github.com/juanmicrosoft/calor/pull/621). |

Legend: 🟢 ready, 🟡 conditional, 🔴 blocker.

### 8.1.a — Operator artifacts authored in this commit (2026-05-27)

These four files together close §8.1 rows 7, 8, and 9 to 🟡 (staged
but not yet signed/scheduled/opened). They are operator deliverables,
not part of the immutable pre-registration; revising them does NOT
trigger the v6 §9.c retry-sign-off discipline.

- [`phase-2-gate-config.json`](phase-2-gate-config.json) — pinned
  `claude-sonnet-4-6`, budget envelope ($5k ceiling), abort triggers,
  exact kickoff CLI template.
- [`phase-2-spend-authorisation.md`](phase-2-spend-authorisation.md) —
  signature artifact; §2 prescribes per-trial calibration, §5
  prescribes the §9.a-style kickoff cool-off, §6 is the signature
  block the maintainer commits.
- [`phase-2-monitoring-tick.md`](phase-2-monitoring-tick.md) — Windows
  Task Scheduler + POSIX cron setup for the 4-hour monitor.
- `scripts/monitor_phase_2_gate.py` — the monitor itself; verified
  green / tripped / missing-file paths against stub data; exits 0 /
  1 / 2 respectively.

### 8.2 — Substrate gap (RESOLVED in protocol v2)

**Original problem (v1):** The gate driver in
`scripts/run_phase_2_gate.py:collect_fixtures` iterated both
`tests/E2E/agent-tasks/fixtures/` (24 directories) and
`tests/E2E/agent-tasks/templates/path-2-gate/` (6 directories). Only
the 6 templates had a runnable task contract; the 24 fixtures were
workspace skeletons referenced by `tests/E2E/agent-tasks/tasks/<category>/<task>/task.json`
files via the `fixture` field. Demonstrated empirically by a 90-record
dry-run: **72 / 90 (80%)** had `harness_error="no_task_contract"`.

**Resolution (v2):** Protocol v2 introduces an explicit trial
manifest (`tests/E2E/agent-tasks/phase-2-gate-tasks.txt`) that
enumerates 24 `task:<cat>/<id>` rows plus 6 `template:<dir>` rows.
The driver iterates the manifest, resolves each trial to its
contract + workspace, and dispatches the adapter with both
`--task-dir` and `--fixture-dir`. The adapter supports both kinds:
for `task:` it copies the fixture, extracts `prompt` from `task.json`,
runs the agent, and verifies via `calor build`; for `template:` it
copies `setup/`, uses `task.md` as the prompt, and runs the
template's `acceptance.sh`.

Verified empirically by a 90-record dry-run on the refactored
substrate: **0 / 90 substrate-noise records**. All records carry
`harness_error="dry_run"` (the expected dry-run tag), zero
`no_task_contract`. The full 900-trial gate would now spend its
budget on genuine agent measurements rather than substrate scaffolding.

The original two "paths forward" considered in v1 §8.2:

1. ~~Author 24 new task contracts~~ — not chosen; 1–2 weeks of effort
   for marginal benefit over reusing the 129 existing `task.json` files.
2. **Refactor `run_phase_2_gate.py` to iterate `tasks/`** — chosen.
   Stratified 24-task subset preserves the v1 sample size (no power
   re-derivation needed) and reuses vetted task contracts.

### 8.3 — What is concretely runnable today

All technical plumbing is in place. The gate driver can drive 900
real LLM trials end-to-end. The remaining gating items (§8.1 rows
1, 7, 8, 9, 10) are operational and require maintainer action, not
engineering work.

If the operator hand-waves the operational blockers and accepts an
off-protocol run for engineering signal only (NOT for the merge
decision), the system is ready to spend LLM budget *today*.

### 8.4 — Recommended next steps before kickoff

In dependency order:

1. ✅ ~~Push `release/phase-1-only`~~ (done; pushed to origin at `82301fe`).
2. ✅ ~~Pick substrate path and author protocol v2~~ (done; path 2 chosen;
   `phase-2-measurement-protocol-v2.md` + manifest committed).
3. ✅ ~~Open docs-only PR~~ (done; PR [#621](https://github.com/juanmicrosoft/calor/pull/621)
   open against `main`; awaits review/merge).
4. ✅ ~~Author operator artifacts~~ (done 2026-05-27): pinned model
   config, spend-authorisation template, monitoring tick + Windows/POSIX
   scheduling docs. See §8.1.a.
5. **Operator: complete the four signature/scheduling actions.** In
   any order:
   - Run per-trial calibration (`phase-2-spend-authorisation.md` §2)
     and update the table with real numbers.
   - Register the monitoring tick on the host that will run kickoff
     (`phase-2-monitoring-tick.md` §2 for Windows / §3 for POSIX).
   - Open the §9.a-style kickoff cool-off issue
     (`phase-2-spend-authorisation.md` §5).
   - After ≥ 4 hours have elapsed since the cool-off issue body was
     committed, sign `phase-2-spend-authorisation.md` §6.
6. **Then** execute the kickoff command in protocol v2 §10.1.a.

Steps 1–4 are technical/editorial and proceed without spend.
Step 5 is signature work — irreversible commitment only at the moment
the kickoff command is executed in step 6.
