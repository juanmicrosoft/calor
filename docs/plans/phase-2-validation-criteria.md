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

| # | Prerequisite (protocol §10.1.a) | Status | Owner | Detail |
|---|---------------------------------|--------|-------|--------|
| 1 | Pre-registration doc merged to `main` | 🔴 Blocker | Maintainer | Protocol + this doc currently live only on `feature/compact-ids-v6` (commit `b45a3c6`). RFC §10.5 immutability requires `main`. Open a docs-only PR (no implementation) to land both files. |
| 2 | Working tree clean on Arm C checkout | 🟡 Operator step | Operator | Verified procedurally in §10.1.a; not an artifact gate. |
| 3 | §1 (mechanical) checks green | 🟢 Done | — | Tier 1 `[11/11] OK`, all 5,427 tests pass, both round-trip harnesses byte-perfect on the corpus. |
| 4 | 30 fixtures resolve to readable dirs | 🟡 Resolves, but see §8.2 | — | 24 + 6 directories exist; 24 of them lack a runnable task contract. |
| 5 | Three **distinct** arm refs (A ≠ B ≠ C) | 🟢 Local only | Maintainer (push) | `release/phase-1-only` cut locally at `82301fe` (3 cherry-picks + 1 stub commit; builds + all tests green). Not yet pushed; needs maintainer review before publishing. |
| 6 | Agent harness wired to driver | 🟢 Done | — | `tests/E2E/agent-tasks/run.sh` adapter authored this session. `scripts/run_phase_2_gate.py` patched to (a) resolve git-bash on Windows for local dev and (b) propagate the adapter's `harness_error` field into `runs.jsonl` (was silently dropped). Verified end-to-end: 90 records via `CALOR_GATE_DRY_RUN=1`, all `harness_crash=false`. Smoke test (`scripts/smoke_phase_2_gate_dryrun.py`) remains green. |
| 7 | Version-pinned model id + API credentials + budget | 🔴 Blocker | Maintainer | `claude.exe` is on `PATH`; no API spend authorization captured. Budget ~$2–4k per RFC §10.1. |
| 8 | 4-hour monitoring tick scheduled | 🔴 Blocker | Operator | Cron / Task Scheduler / external timer not configured. |
| 9 | v6 §9.c solo-mode sign-off ritual | 🔴 Blocker | Maintainer | 24h cool-off issue not opened. |

Legend: 🟢 ready, 🟡 conditional, 🔴 blocker.

### 8.2 — Substrate gap (criterion-level impact)

The gate driver in `scripts/run_phase_2_gate.py:collect_fixtures` iterates
both `tests/E2E/agent-tasks/fixtures/` (24 directories) and
`tests/E2E/agent-tasks/templates/path-2-gate/` (6 directories). Only the
6 templates have a runnable task contract (`task.md` + `setup/` +
`acceptance.sh`). The 24 fixtures are workspace skeletons referenced by
`tests/E2E/agent-tasks/tasks/<category>/<task>/task.json` files via the
`fixture` field — they are *workspaces*, not *tasks*.

Demonstrated empirically (90 records produced by `CALOR_GATE_DRY_RUN=1`
on this branch):

| Substrate class | Fixtures | Records (1 seed × 3 arms) | Adapter `harness_error` |
|-----------------|----------|----------------------------|-------------------------|
| Templates with task contract | 6 | 18 | `dry_run` (would be real LLM runs) |
| Fixtures with no contract | 24 | 72 | `no_task_contract` |

In a full 900-run gate (10 seeds), **720 / 900 (80%)** of records would
be `no_task_contract` — providing zero criterion-1 signal differentiating
arms because no agent ever runs against them. The remaining 180 useful
records is well below the sample size the protocol's statistical power
analysis assumed (RFC §10.1).

Two paths forward, both require a protocol amendment per RFC §10.5
(`phase-2-measurement-protocol-v2.md`):

1. **Author 24 new task contracts**, one per existing fixture. Each
   contract needs a `task.md` prompt, optional `setup/` overlay,
   `expected/` reference output, and `acceptance.sh`. Estimated effort:
   1–2 weeks of careful task design. Preserves the 30-fixture × 10-seed
   × 3-arm = 900-run plan.

2. **Refactor `run_phase_2_gate.py:collect_fixtures` to iterate
   `tests/E2E/agent-tasks/tasks/<cat>/<task>/task.json`**, joining each
   `task.json` with its declared `fixture`. Many tasks share the same
   fixture, so the trial count would shift to N(tasks) × 10 × 3. With
   ~40 existing tasks plus the 6 templates, that's 46 × 30 = 1,380 runs,
   which over-provisions statistical power but inflates the budget by
   ~50%. Re-pre-register sample size and Bonferroni α accordingly.

A third "shortcut" — running the gate with the current substrate and
filtering out `no_task_contract` records in `analyze_gate_results.py` —
is **not acceptable** because it silently changes the planned sample
size, violating the pre-registration discipline of RFC §10.5.

### 8.3 — What is concretely runnable today

If the operator hand-waves blockers 1, 7, 8, and 9 (i.e. authorises
spend and accepts an off-protocol run for engineering signal only,
NOT for the merge decision), the system can drive the **6 templates × 10
seeds × 3 arms = 180 trials** through the now-wired adapter against
real LLM endpoints. This would produce a runs.jsonl with real
`success` / `turn_count` / `total_output_tokens` data for the 18 /
trial-slice per task, giving early signal but with sample size too
small for the protocol's pre-registered power. Treat any such run as
exploratory.

### 8.4 — Recommended next steps before kickoff

In dependency order:

1. **Push `release/phase-1-only`** so Arm B has a public, stable ref.
   (Locally at `82301fe`; one `git push -u origin release/phase-1-only`.)
2. **Open the docs-only PR** that merges this document and
   `phase-2-measurement-protocol.md` to `main`. This freezes the
   pre-registration without touching implementation.
3. **Decide path 1 vs path 2 of §8.2** and author
   `phase-2-measurement-protocol-v2.md` if needed (with the new sample
   size and re-derived statistical power).
4. **Authorise LLM spend, pick a pinned model id, configure the
   4-hour monitoring tick, complete the §9.c sign-off.**
5. **Then** execute the kickoff command in protocol §10.1.a.

Steps 1–3 are technical/editorial and can proceed without spend.
Step 4 is the irreversible commitment.
