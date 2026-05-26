# v6 Implementation Plan — Compact Stable Identifiers

**Status:** Approved for implementation (calibration revision of v5-implementation)
**Supersedes:** [path-2-drop-ids-v5-implementation.md](path-2-drop-ids-v5-implementation.md)
**Reviewed against:**
  - [path-2-drop-ids-v5-implementation-critique.md](path-2-drop-ids-v5-implementation-critique.md) (designer voice)
  - [path-2-drop-ids-v5-implementation-devils-advocate.md](path-2-drop-ids-v5-implementation-devils-advocate.md) (auditor voice)
**Type:** Calibration revision (not structural rewrite)
**Note on the §0.1 rule:** the grep-verify rule v5-implementation codified in §7 caught a false claim made by one of v6's own reviewers (see §0.2 below). The rule that was authored to discipline RFC authors disciplines RFC reviewers too. v6 augments §7's content accordingly.

> **Reader's note.** v6 preserves v5-implementation's structure, PR breakdown, tier model, and rollback playbook unchanged. It applies 12 targeted calibrations identified by the v5-implementation review round. For any section not listed in §0 below, **the v5-implementation text stands as written**. This document records only the deltas.

---

## §0 — Changes from v5-implementation

Both reviewers concluded "approve, ship the prerequisite PRs in week 0, begin Phase 1." The designer critique listed 10 calibration items; the devil's advocate raised 1 false-blocking item (§0.2 below) and 9 substantive items. v6 folds in 12 items net (some overlap; the false-blocker is documented but requires no plan change).

| # | Section | Source | Change |
|---|---------|--------|--------|
| 1 | Header | DC §1 | **WORDING:** "Companions:" → "Companion files (to be authored by PR-0a/b/c per §2)". Removes the false impression that the three listed files already exist. |
| 2 | §3.2 (Tier 2 token-delta row) | DC §2 | **CITATION:** Cite `~9.67` tokens/ID to v5 RFC §16.F (N=100 RNG-generated, 95% CI 9.30–10.04). Mark `±20%` tolerance band as **provisional**: recalibrate from the first real corpus measurement on a known-green build, replace with empirical sd or revisit. |
| 3 | §1.2 | DC §3 | **STRUCTURE:** Split §1.2 into two tables: (a) "Hard prerequisites for Phase 1" (PR-0a..0d), (b) "Parallel ship items" (PR-0e). PR-0e is independent and ships in a 0.x patch; v5-implementation listed it in the prerequisites table by mistake. |
| 4 | §9 (NEW) | DC §4 | **NEW SECTION** "Solo-mode adjustments": names three failure modes that arise when agent and maintainer are the same person — (a) maintainer-hat self-review ritual (written triage in a committed issue *before* resolving), (b) Tier 4 scheduling (GitHub Actions scheduled workflow or personal-project-contract monthly checkpoint), (c) Tier 3 retry sign-off (written justification or external read before triggering second $2k–$4k run). |
| 5 | §3.1 (Tier 1) | DC §5 | **ACCEPTANCE:** PR-0d's acceptance criteria adds: measured wall-clock benchmark of Tier 1 on the existing corpus, captured in the PR description. If Tier 1 > 60s, §3.6 remediation (split into default+extended) executes in PR-0d, not at PR-1a discovery time. |
| 6 | §4.3 | DC §6 | **PRE-REG INTEGRITY:** Arm SHAs frozen at gate kickoff, not "after PR-X merges." `scripts/run-phase-2-gate.*` records each arm's SHA at start and writes them to the pre-registration's "Implementation flag" section before any run executes. |
| 7 | §3.3 | DC §7 | **MONITORING MECHANISM:** v6 picks **scheduled polling via `ScheduleWakeup` every 4 hours during the run window**, plus a pre-flight check that the next-monitoring-tick is scheduled before the gate starts. Webhook-based alerting is allowed as an addition but not a substitute. For solo-mode operation this is cross-referenced from §9. |
| 8 | §3.5 | DC §8 | **TABLE ROW ADDED:** "Tier 1 + Tier 2 green, Tier 3 pending" → "Phase 1 is shippable per its merge gate. Phase 2 code may be merged to the release branch but MUST NOT ship to users until Tier 3 passes." This is the operational state of `release/0.x+1` between Phase 2 merge and gate kickoff. |
| 9 | §2.3 (PR-2c) | DA §3 | **SCOPE EXPANSION:** PR-2c's file list expands from `IdAssigner.cs` (2 sites) to `IdAssigner.cs`, `IdChecker.cs`, `IdValidator.cs`, and `IdGenerator.cs` (7 grep-verified call sites total). Estimate revises from 0.5 day to 1.0–1.5 days. Acceptance criteria: "all `IdGenerator.*` callers either switched to the compact path or explicitly documented as legacy-only with a comment referencing the migrator's reverse-path." `ExtractUlid` specifically is renamed `ExtractIdPortion` (or kept legacy + a new format-aware extractor added). |
| 10 | §4.1 | DA §4 | **PRE-REG INTEGRITY:** §4.1 fixture paths use the actual 24 directory names enumerated under `tests/E2E/agent-tasks/fixtures/` (verified by `Get-ChildItem`), not the fictional `task-001..024` pattern. v6 also adds a §4.0 disambiguation: `fixtures/` (workspace fixtures, 24 dirs) vs `tasks/` (task files, ~258) vs `templates/path-2-gate/` (the 6 new templates in PR-0c). The §10 gate runs against `fixtures/` ∪ `templates/path-2-gate/`. |
| 11 | §5.1 | DA §5 | **METHODOLOGY:** Baseline success range changes from "**≥80%**" to "**50–90%**". Adds language: "tasks above 90% baseline should be made harder until they fall in range; tasks below 50% should be made easier or excluded." Removes the ceiling effect that would suppress small-effect detection on success-rate kill criterion (1) at week 4. |
| 12 | §7 (file content) + Appendix A | DA §6 | **CROSS-PLATFORM:** All new harness scripts authored in **Python** (matches existing `scripts/compare-benchmarks.py` precedent). Two exceptions: thin shell entrypoints `scripts/verify-phase1.{sh,ps1}` and `scripts/verify-corpus.{sh,ps1}` follow the existing `test-enforcement.{sh,ps1}` dual pattern as `python` invocation wrappers, so Windows developers can run from PowerShell and Linux/macOS developers from bash. Appendix A's script glossary updated to reflect `.py` extensions for the underlying scripts. |
| 13 | §3.1 + CI workflow design | DA §7.1 | **CI CONSOLIDATION:** PR-0d does NOT add a new `.github/workflows/tier1.yml` that duplicates `test.yml`. Instead, PR-0d extends `test.yml` with two new steps: (a) `scripts/verify-phase1.{sh,ps1}` invocation, (b) Tier 1 harness wall-clock report. The existing `dotnet build && dotnet test -c Release` runs once. Tier 2 (`scripts/verify-corpus.{sh,ps1}`) runs as a separate `tier2.yml` workflow triggered only on PRs targeting `phase-1`, `phase-2`, or `release/0.x+1` branches. |
| 14 | §3.1 (Tier 1 enforcement) | DA §7.2 | **DROP** "agent must paste full output into PR description." The CI required-check status on `scripts/verify-phase1` is the canonical enforcement; pasting output is performative duplication that drifts when CI re-runs. v6 keeps the discipline ("Tier 1 must be green to mark a PR ready for review") but enforces it through GitHub's required-status mechanism, not through prose pasted into PR descriptions. |
| 15 | §2.2 (PR-1d) | DA §7.3 | **PR SCOPE RE-EXAMINED:** PR-1d (byte-preservation verifier) flagged as **potentially collapsible into PR-1c tests**. v5 RFC §5.7.3 establishes byte-preservation as a *by-construction* property of the text-edit migrator. A separate verifier asserts what the migrator already provides. v6 keeps PR-1d as a separate PR for now (the verifier is reusable post-Phase-1 as a regression test) but explicitly marks it as "may collapse into PR-1c if the maintainer judges the verifier adds no testing surface beyond what PR-1c's unit tests cover." Decision recorded at PR-1d kickoff, not in v6. |
| 16 | §4.4 | DA §7.4 | **MODEL PINNING FALLBACK:** §4.4 adds: "If the pinned model becomes unavailable during the gate run (deprecation, API outage, account limit), the run halts and §10.5 retry policy is invoked. The next pinned model is selected from a maintainer-curated fallback list authored alongside the pre-registration document; switching pinned models invalidates any in-flight runs and resets the run counter. Sign-off on a model switch follows the §9 solo-mode rule for retry sign-off." |
| 17 | §7 + new §0.2 | DA §2 (FALSE) | **NO PLAN CHANGE** for the DA's "§8 is missing / file truncates at line 427" claim — verified false: `rg "^## §8" path-2-drop-ids-v5-implementation.md` returns line 483, file is 561 lines, ends at Appendix B. **HOWEVER:** the meta-pattern (a reviewer claims something about an artifact without grep-verifying) is the exact failure mode §7's grep-verify rule was authored to prevent. v6 adds §0.2 (below) documenting this as the v5-implementation→v6 instance of the architectural-fiction pattern, and amends §7's reviewer rules in §7-bis (this file) to explicitly cover *artifact-state claims*, not just architectural assertions. |

That's 17 deltas covering 12 substantive items (item 17 introduces §0.2 only; items 13 and 14 are sub-deltas of the same CI restructure; items 1, 3 are one-line headers/labels; items 9, 11, 12 are the largest substantive changes).

---

## §0.2 — Institutional observation: the §7 rule applies to reviewers too

v6's devil's-advocate review opens with a "blocking" claim: *"the document is incomplete. §7 truncates mid-sentence at line 427; §8 — 'Definition of high confidence — per-PR, per-phase, per-release' — is promised in §1.1 but does not exist."*

The claim was grep-verified against the v5-implementation file:

```
$ rg "^## §" docs/plans/path-2-drop-ids-v5-implementation.md
14:## §1 — Scope and prerequisites
51:## §2 — PR breakdown
136:## §3 — Coding-agent self-verification harness
222:## §4 — The Phase 2 pre-registration document
312:## §5 — The 6 new benchmark task templates
354:## §6 — Rollback playbook
410:## §7 — docs/process/rfc-review-checklist.md
483:## §8 — Definition of done
529:## Appendix A — Glossary of harness scripts
548:## Appendix B — Calendar overlay on RFC §11

$ wc -l docs/plans/path-2-drop-ids-v5-implementation.md
561 lines
```

§8 exists with §8.1 through §8.5 (lines 483–525). The DA's claim is false.

The likely cause: the DA's review tool truncated its read at 50 KB or some byte threshold, the reviewer concluded the file ended where their read ended, and the claim landed in the review document without a `rg`/`Get-Content -Tail` cross-check.

**This is the exact failure mode §7's grep-verify rule was authored to prevent.** The trajectory pattern:

| Round | Fictional claim | Subject of claim | Caught by |
|-------|-----------------|------------------|-----------|
| v2→v3 (RFC) | "9-char compact IDs are collision-safe" | source-code behavior | math re-derivation |
| v3→v4 (RFC) | "migrator caches results" | source-code behavior | grep showed no cache existed |
| v4→v5 (RFC) | "verifier compares trivia anchors" | source-code/AST behavior | grep `Lexer.cs` showed no trivia infra |
| v5-impl→v6 | "§8 doesn't exist in the file" | artifact-state | grep `"^## §8"` showed it exists |

The §7 rule originally targeted **authors making claims about source code without grep**. v6's lesson: the rule generalizes to **anyone making any architectural or artifact claim** — authors *and* reviewers. v6's §7-bis (below) updates the checklist content for PR-0a accordingly.

This observation does not change the plan. The DA's substantive critiques (PR-2c scope, fictional fixture paths, cross-platform script policy, ≥80% baseline ceiling, the §7.x smaller items) are correct and folded in. Only the "blocking" claim is wrong, and it is documented here rather than carried forward as a fix.

---

## §1 (header revision) — Companion files

The v5-implementation header lists three "Companions." v6 changes the label to make their status unambiguous:

> **Companion files (to be authored by PR-0a/b/c per §2):**
>  - `docs/process/rfc-review-checklist.md` — authored as PR-0a; content drafted in §7 + §7-bis
>  - `docs/plans/phase-2-measurement-protocol.md` — authored as PR-0b; content drafted in §4
>  - `tests/E2E/agent-tasks/templates/path-2-gate/` — 6 new task templates authored as PR-0c per §5

Adds 4 words; removes the false impression that these files already exist.

---

## §1.2 (split) — Hard prerequisites vs. parallel ship items

v5-implementation conflated PR-0e (independent, ships in 0.x patch) with PR-0a..0d (block Phase 1). v6 splits the table:

### 1.2.a Hard prerequisites for Phase 1 (must merge before Phase 1 starts)

| Prerequisite | Why | PR |
|--------------|-----|----|
| `docs/process/rfc-review-checklist.md` committed | Codifies the §0.1 grep-verify rule + §0.2 reviewer-rule extension before authors touch RFC-adjacent work | PR-0a |
| `docs/plans/phase-2-measurement-protocol.md` committed | RFC §10.2 hard prerequisite for Phase 2; lands early to allow review time | PR-0b |
| 6 new benchmark task templates committed | RFC §10.1 corpus is 24 existing + 6 new; the new 6 must exist before Phase 1 so the corpus is stable | PR-0c |
| `scripts/verify-phase1.{sh,ps1}` and supporting Python scripts committed, CI-integrated, **wall-clock measured ≤ 60s** | Agent needs Tier 1 to exist *and demonstrably hit its budget* before writing Phase 1 PRs | PR-0d |

### 1.2.b Parallel ship items (independent of Phase 1, can ship in week 0 on the 0.x cadence)

| Item | Why | PR |
|------|-----|----|
| §8.3 standalone diagnostic addressing | RFC §11: ships first (week 0), in days, on the 0.x patch release cadence, independent of Phase 1 | PR-0e |

**Phase 1 implementation PRs (PR-1*) cannot start until PR-0a through PR-0d are merged.** PR-0e is independent.

---

## §2.3 (PR-2c revised) — IdGenerator surface audit

v5-implementation's PR-2c targeted only `IdAssigner.cs:175,180` (the 2 `Generate()` call sites). The DA verified that `grep "IdGenerator\." src/Calor.Compiler/Ids` returns 7 call sites across 3 files. v6 expands PR-2c accordingly:

| PR | Title | Files touched | Acceptance criteria | Tier | Estimate |
|----|-------|---------------|---------------------|------|----------|
| **PR-2c (v6)** | `IdGenerator.*` caller audit + migration to compact format | `src/Calor.Compiler/Ids/IdAssigner.cs:175,180`; `src/Calor.Compiler/Ids/IdChecker.cs:156`; `src/Calor.Compiler/Ids/IdValidator.cs:56,64,163,167`; `src/Calor.Compiler/Ids/IdGenerator.cs` (method renames) | All 7 grep-verified call sites either switched to the compact path or explicitly documented as legacy-only with a comment referencing the migrator's reverse-path. `ExtractUlid` either renamed `ExtractIdPortion` (format-agnostic) or kept legacy + new format-aware extractor added (PR author's call; either acceptable). Tests green; format-validation tests cover both ULID (legacy) and 12-char compact (new) formats. | Tier 1 | **1.0–1.5 days** |

The PR description must list the 7 call sites by file:line so the reviewer can confirm coverage. This is the §7-rule discipline applied to PR-2c itself.

---

## §3.1 (Tier 1 revised) — Measured budget, no paste requirement

v6 replaces v5-implementation's §3.1 acceptance rule with:

### 3.1.a Tier 1 wall-clock budget — measured, not asserted

PR-0d's acceptance criteria includes:

- Run `scripts/verify-phase1.{sh,ps1}` against the corpus on a clean checkout of `main`.
- Record wall-clock time in the PR description.
- If wall-clock > 60s, execute the §3.6 remediation order (profile → cache → split into default+extended) in PR-0d, not at PR-1a discovery time.
- Final wall-clock measurement (post-remediation if needed) must be < 60s on a reference developer machine spec documented in the PR.

### 3.1.b Tier 1 enforcement — CI required-check, not pasted prose

v5-implementation required the agent to "paste full output into the PR description." v6 drops this. The enforcement mechanism is:

- `scripts/verify-phase1` runs as a required status check on every PR (configured in PR-0d's `test.yml` extension per §3.1.c).
- The agent may not mark a PR "ready for review" unless this required check is green.
- The CI run is the canonical record. Pasting output is not required and not recommended (it drifts when CI re-runs and pads PR descriptions).

### 3.1.c CI workflow consolidation

PR-0d does NOT add a separate `.github/workflows/tier1.yml`. Instead, PR-0d extends the existing `test.yml` with two new steps:

1. Invoke `scripts/verify-phase1.{sh,ps1}` (cross-platform wrapper per §7-cross-platform).
2. Report Tier 1 wall-clock time as a build artifact.

The existing `dotnet build && dotnet test -c Release` continues to run once per PR. Unit tests are not duplicated.

Tier 2 (`scripts/verify-corpus.{sh,ps1}`) runs as a separate `tier2.yml` workflow triggered only on PRs targeting `phase-1`, `phase-2`, or `release/0.x+1` branches — Tier 2's 30-min runtime should not pay on every feature PR.

---

## §3.2 (Tier 2 revised) — Cited token-delta, provisional tolerance band

v5-implementation's §3.2 token-delta row read: *"Aggregate Δtokens across whole corpus is within expected range (Phase 1: ~9.67 × N_structural_IDs ± 20%; Phase 2: additional ~9.67 × N_symbol_IDs ± 20%)."*

v6 replaces with:

> **Token-delta corpus check.** Aggregate Δtokens across whole corpus is within expected range:
>   - **Phase 1 expected:** `9.67 × N_structural_IDs ± (provisional tolerance)`
>   - **Phase 2 expected:** additional `9.67 × N_symbol_IDs ± (provisional tolerance)`
>   - **Source of 9.67:** v5 RFC §16.F, N=100 RNG-generated production-format IDs, 95% CI (9.30, 10.04) per ID. The CI half-width is 0.37 tokens; aggregate variance scales as N⁻¹/² so the corpus-level expected band is materially tighter than per-file.
>   - **Tolerance band:** **provisional ±20%** at PR-0d authoring time. **Recalibrated against the first real corpus measurement on a known-green build** before PR-1c lands. If the empirical sd justifies a tighter band, narrow it; if wider, widen it and document why in the PR description.
>   - **Failure mode:** Tier 2 red if outside the (recalibrated) range. A measurement well outside range indicates either a measurement bug or unexpected migrator behavior — both of which warrant investigation before merge.

This satisfies the §7-rule citation requirement and removes the unsourced 20% magic number.

---

## §3.3 (Tier 3 monitoring) — Polling mechanism specified

v5-implementation's §3.3 said the agent "watches for protocol violations." v6 specifies the mechanism:

### 3.3.a Pre-flight check

Before `scripts/run-phase-2-gate.{sh,ps1}` invokes the first run:

- Verify the monitoring tick is scheduled (see 3.3.b).
- Verify the pinned model is currently available (a 1-prompt sanity ping; if it fails, halt and invoke §4.4 model-pinning fallback).
- Verify disk space, network connectivity, and run-log directory writable.

### 3.3.b Scheduled polling via `ScheduleWakeup`

During the 4–5 day run window:

- The agent (or maintainer) sets a `ScheduleWakeup` for every 4 hours.
- At each wake, the agent inspects the run log for: protocol violations (harness crash, fixture mutation, log gaps), model API errors, throughput drift (runs/hour < expected).
- On any violation, halt and trigger §10.5 retry per §4.9.
- On all-green, continue and reschedule the next wake.

### 3.3.c Webhook-based alerting (optional addition)

`scripts/run-phase-2-gate` may additionally post to a webhook on protocol violation for faster than 4-hour response. Webhook alerting is an addition to the 4-hour polling, not a substitute (webhook outages would otherwise silently lose alerts).

### 3.3.d Solo-mode cross-reference

For solo-project operation (agent and maintainer are the same person), see §9.b for the operational pattern.

---

## §3.5 (revised confidence calculus) — Added row

v5-implementation's §3.5 table has 4 rows. v6 adds the row covering the most operationally common state during weeks 4–4.5:

| Combination | What you can claim | What you can NOT claim |
|-------------|--------------------|------------------------|
| Tier 1 green | "This PR did not break the build or local invariants." | "This PR is safe to merge into a release branch." |
| Tier 1 + Tier 2 green | "This PR preserves system invariants across the whole corpus; aggregate token deltas are in expected range." | "Agents will actually benefit from this change." |
| **Tier 1 + Tier 2 green, Tier 3 pending** *(v6 NEW)* | **"Phase 1 is shippable per its merge gate. Phase 2 code may be merged to `release/0.x+1` but MUST NOT ship to users until Tier 3 passes. If the maintainer must ship in the gate window, ship Phase 1 alone."** | Anything about Phase 2's agent benefit; anything about the gate outcome. |
| Tier 1 + Tier 2 + Tier 3 green | "Agents materially benefit (per RFC §10.3 medium-effect threshold), with statistical significance, and no regressions." | "Long-term codebase rot is unaffected" (RFC §10.7). |
| Tier 4 (post-ship) green at day 30 | "Phase 1 in production has not introduced a turn-count regression detectable at the small-effect level." | Anything about Phase 2 — Phase 2 has its own gate (Tier 3). |

The new row is the ship-anxiety state of `release/0.x+1` between Phase 2 merge and gate kickoff (probably 1–2 days, but possibly longer if the gate slips into week 5 contingency). It must be explicit so the maintainer does not feel forced to ship Phase 2 just because Phase 2 code is merged.

---

## §4.0 (NEW) — Agent-task directory disambiguation

`tests/E2E/agent-tasks/` contains three sub-populations that v5-implementation conflated. v6 disambiguates:

| Directory | Population | Used by §10 gate? |
|-----------|------------|-------------------|
| `tests/E2E/agent-tasks/fixtures/` | 24 workspace fixtures (directories with `.calr` files, setup state, and acceptance scripts) | **Yes.** Per RFC §10.1, the existing 24-task corpus. |
| `tests/E2E/agent-tasks/tasks/` | ~258 individual task files (different population, used by existing harness for unit-style agent tests) | **No.** Not part of the §10 gate corpus. |
| `tests/E2E/agent-tasks/templates/path-2-gate/` (NEW, PR-0c) | 6 new workspace fixtures matching the `fixtures/` shape | **Yes.** The 6 new tasks added to the corpus per RFC §10.1. |

The §10 gate runs against `fixtures/` ∪ `templates/path-2-gate/` = **30 fixtures × 10 runs × 3 arms = 900 runs**, matching RFC §10.1.

---

## §4.1 (revised) — Actual fixture directory names

v5-implementation's §4.1 listed `task-001..024` placeholders that do not match any actual directory. v6 uses the verified directory names (enumerated by `Get-ChildItem -Directory tests/E2E/agent-tasks/fixtures/`):

```
Existing tasks (24, paths under tests/E2E/agent-tasks/fixtures/):

  advanced-calor-project/   @ <commit-sha-to-fill-at-PR-0b>
  async-project/            @ <commit-sha-to-fill-at-PR-0b>
  basic-calor-project/      @ <commit-sha-to-fill-at-PR-0b>
  buggy-contracts/          @ <commit-sha-to-fill-at-PR-0b>
  collections-project/      @ <commit-sha-to-fill-at-PR-0b>
  contracts-project/        @ <commit-sha-to-fill-at-PR-0b>
  effects-project/          @ <commit-sha-to-fill-at-PR-0b>
  enums-project/            @ <commit-sha-to-fill-at-PR-0b>
  generics-project/         @ <commit-sha-to-fill-at-PR-0b>
  oop-project/              @ <commit-sha-to-fill-at-PR-0b>
  refactor-contract-calor/  @ <commit-sha-to-fill-at-PR-0b>
  refactor-contract-csharp/ @ <commit-sha-to-fill-at-PR-0b>
  refactor-extract-calor/   @ <commit-sha-to-fill-at-PR-0b>
  refactor-extract-csharp/  @ <commit-sha-to-fill-at-PR-0b>
  refactor-inline-calor/    @ <commit-sha-to-fill-at-PR-0b>
  refactor-inline-csharp/   @ <commit-sha-to-fill-at-PR-0b>
  refactor-move-calor/      @ <commit-sha-to-fill-at-PR-0b>
  refactor-move-csharp/     @ <commit-sha-to-fill-at-PR-0b>
  refactor-rename-calor/    @ <commit-sha-to-fill-at-PR-0b>
  refactor-rename-csharp/   @ <commit-sha-to-fill-at-PR-0b>
  refactor-signature-calor/ @ <commit-sha-to-fill-at-PR-0b>
  refactor-signature-csharp/@ <commit-sha-to-fill-at-PR-0b>
  refactor-target/          @ <commit-sha-to-fill-at-PR-0b>
  refactoring-impure/       @ <commit-sha-to-fill-at-PR-0b>

New tasks (6, paths under tests/E2E/agent-tasks/templates/path-2-gate/):

  task-01-multi-function-refactor/   @ <commit-sha-to-fill>
  task-02-add-db-effect/             @ <commit-sha-to-fill>
  task-03-privacy-change/            @ <commit-sha-to-fill>
  task-04-add-postcondition/         @ <commit-sha-to-fill>
  task-05-loop-restructure/          @ <commit-sha-to-fill>
  task-06-error-handling/            @ <commit-sha-to-fill>
```

The new-task directory names use `task-NN-<theme>/` to make the §5.2 theme mapping unambiguous and to avoid the v5-implementation conflict with the actual fixture naming convention.

---

## §4.3 (revised) — Arm SHAs frozen at gate kickoff

v5-implementation's §4.3 described arm SHAs as "after PR-1h merges" / "after PR-2f merges." For pre-registration honesty, the SHAs must be **frozen at the moment of gate kickoff**, not described relative to PR events that may complete mid-experiment.

v6 §4.3 reads:

```
Arm A (today/baseline):    main branch at <SHA recorded at gate kickoff>
                           (latest 0.x pre-release tag's commit)
Arm B (Phase 1 only):      release/0.x+1 at <SHA recorded at gate kickoff>
                           (commit of the most recent Phase 1 merge; Phase 2
                           PRs may or may not be merged at gate kickoff,
                           but Arm B does not include them)
Arm C (Phase 1 + Phase 2): release/0.x+1 at <SHA recorded at gate kickoff>
                           (commit of the most recent Phase 2 merge)
```

`scripts/run-phase-2-gate.{sh,ps1}` records all three SHAs at start and writes them to the pre-registration document's "Implementation flag" section before any run executes. Any change to any SHA mid-run invalidates the run per RFC §10.5.

---

## §4.4 (revised) — Model-pinning fallback

v5-implementation's §4.4 pinned the model but did not specify what happens if the pinned model becomes unavailable mid-run. v6 adds:

> **Model unavailability during the gate run.** If the pinned model becomes unavailable (provider deprecation, account outage, rate-limit lock, API change), the run halts and the agent invokes the §10.5 retry policy. The next pinned model is selected from a **fallback list maintained alongside the pre-registration document**:
>
>   1. Primary: `<model-name>` at `<version>` as of `<date>`.
>   2. Fallback A: `<model-name>` at `<version>` as of `<date>`.
>   3. Fallback B: `<model-name>` at `<version>` as of `<date>`.
>
> A model switch invalidates any in-flight runs (the run counter resets to 0 for the new model). Sign-off on a model switch follows the §9.c solo-mode rule for retry sign-off: a written justification committed before the switch happens, and (in solo mode) an external read of the justification before triggering the switch.

The fallback list is authored at PR-0b time, frozen at gate kickoff, and is not edited mid-run.

---

## §5.1 (revised) — Baseline success range 50–90%

v5-implementation's §5.1 rubric required "baseline agent achieves ≥80% success across 10 seeds." The DA's math: at the 80% lower bound, Arms B/C can show at most 20 percentage points of improvement before hitting the ceiling — and with N=60 binary observations per arm, McNemar power for a realistic 5–10 percentage-point effect is poor.

v6 replaces with:

> **Baseline solvability range: 50–90% across 10 seeds.**
>
>   - Tasks **above 90%** baseline must be made harder (add a sub-edit, add a cross-file dependency, raise the acceptance bar) until they fall in range.
>   - Tasks **below 50%** baseline must be made easier or excluded.
>   - Tasks in the **50–90% range** provide the most distinguishing power for non-trivial effect sizes on RFC §10.3 kill criterion (1) (success-rate non-inferiority) and criterion (3) (turn-count or token-count improvement at medium effect).
>
> The threshold of 50% is set well above chance (which is ~0% for tasks with deterministic acceptance scripts) to ensure the baseline can solve the task at least half the time — i.e., the task is genuinely solvable by the current generation of agents, not a coin flip.

This avoids the ceiling effect at the 100% line and preserves the existing rubric's intent (exclude tasks that are unsolvable today and therefore signal-free).

---

## §7-cross-platform — Script language and packaging policy

The DA identified that v5-implementation's 10 new `.sh` scripts break on Windows developer machines (`scripts/` contains a mix of `.sh`, `.ps1`, `.py`, `.js`, with `test-enforcement.{sh,ps1}` as the dual-platform precedent). v6 fixes this:

### 7.cp.a Core logic in Python

All Tier 1/2/3/4 verification logic is implemented in Python. The precedent is `scripts/compare-benchmarks.py`. Reasons:

- Cross-platform without a dual-script burden on every script.
- The analysis pipeline (`scripts/analyze-gate-results.py`) is already Python; reusing the runtime keeps dependencies aligned.
- Both Windows and Linux CI runners have Python available without additional setup.

### 7.cp.b Thin shell entrypoints

For the entrypoints the agent invokes most often, v6 provides `.sh` + `.ps1` wrappers that delegate to the Python implementation:

```
scripts/
├── verify-phase1.sh           # wrapper: python3 scripts/verify_phase1.py "$@"
├── verify-phase1.ps1          # wrapper: python scripts/verify_phase1.py @args
├── verify_phase1.py           # actual implementation
├── verify-corpus.sh           # wrapper: python3 scripts/verify_corpus.py "$@"
├── verify-corpus.ps1          # wrapper: python scripts/verify_corpus.py @args
├── verify_corpus.py           # actual implementation
├── byte_preservation_check.py # called from verify_phase1.py
├── ast_roundtrip_check.py     # called from verify_phase1.py
├── token_delta_spot.py        # called from verify_phase1.py
├── token_delta_corpus.py      # called from verify_corpus.py
├── migrator_corpus_dryrun.py  # called from verify_corpus.py
├── migrator_revert_roundtrip.py # called from verify_corpus.py
├── run_phase_2_gate.py        # Tier 3 driver
├── run-phase-2-gate.sh        # wrapper
├── run-phase-2-gate.ps1       # wrapper
├── phase1_post_ship_monitor.py # Tier 4
├── phase1-post-ship-monitor.sh
├── phase1-post-ship-monitor.ps1
├── validate_task_template.py  # helper
├── validate-task-template.sh
└── validate-task-template.ps1
```

(Wrappers exist only for the entrypoints; internal helpers are pure Python.)

### 7.cp.c Python version pinning

Python ≥ 3.11 (matches existing `.py` scripts in `scripts/`). Specified in `scripts/requirements.txt` if new dependencies are needed; otherwise stdlib-only.

### 7.cp.d Documentation

`CONTRIBUTING.md` updated in PR-0d to document that Tier 1 entrypoints have both `.sh` and `.ps1` wrappers, that helpers and Tier 2/3/4 logic are pure Python, and that Windows developers should be able to run any harness command from PowerShell without WSL.

---

## §7-bis — Reviewer-side extension to the grep-verify rule

v5-implementation's §7 (draft content for `docs/process/rfc-review-checklist.md`) targeted RFC **authors**. v6 augments §7 with a "For RFC reviewers" addition that the §0.2 false-blocking claim made necessary:

Add to §7 (under "For RFC reviewers"):

```markdown
4. **Grep-verify artifact-state claims, not just architectural claims.**
   When a review document asserts something about the state of an
   artifact ("§N is missing," "the file truncates at line X," "the
   directory does not exist"), the reviewer MUST grep-verify the
   claim before including it in the review:

   - "§N is missing" requires `rg "^## §N" <file>` showing no match.
   - "File truncates at line X" requires `wc -l <file>` showing
     line count == X AND `tail -1 <file>` showing the truncation.
   - "Directory does not exist" requires `ls <path>` or
     `Test-Path <path>` showing absence.

   If the reviewer's tool truncates the read at some byte threshold,
   the reviewer MUST use a second tool (`rg`, `wc -l`, `tail`,
   `Get-Content -Tail`) to verify the artifact ends where their
   read ends, before concluding "the file is truncated."

   This rule was added v5-impl → v6 after one DA review made a
   "blocking" claim that §8 was missing from a file that
   demonstrably contains §8.1–§8.5 (verified by `rg "^## §8"`).
```

PR-0a is the right place for this content to land alongside the original §7. v6 adds it as PR-0a's scope expansion.

---

## §9 (NEW) — Solo-mode adjustments

For projects where the coding agent and the repo maintainer are the same person (the situation v5-implementation implicitly assumed but never named), three failure modes arise. v6 prescribes a mitigation for each.

### 9.a Maintainer-hat self-review ritual (Tier 2 red triage)

**Failure mode:** Tier 2 red on a phase merge. v5-implementation says "wait for maintainer triage." In solo mode, "wait for maintainer triage" is "wait for yourself," which is motivated reasoning's most familiar form.

**Mitigation:** The maintainer-hat ritual:

1. Open a GitHub issue titled `Tier 2 triage: <phase> <date>`.
2. In the issue body, paste the Tier 2 output and write the triage reasoning *before* resolving the issue — what the red signal indicates, why fix-forward vs. revert is appropriate, what specific PR to fix or revert.
3. Wait at least 4 hours (or one ScheduleWakeup tick, whichever is longer) before acting on the triage.
4. Then act, and close the issue with a comment recording the action taken.

The 4-hour wait + written-before-action discipline defends against in-the-moment motivated self-clearing. The committed issue creates an audit trail the agent can reference at v6+1 critique time if a Tier 2 triage decision turns out to have been wrong.

### 9.b Tier 4 scheduling (30-day post-ship monitoring)

**Failure mode:** A coding agent does not have a persistent 30-day calendar. Without explicit scheduling, the day-30 monitoring run won't happen.

**Mitigation:** PR-1h's deliverables include a GitHub Actions scheduled workflow:

```yaml
# .github/workflows/tier4-monitor.yml (added in PR-1h)
on:
  schedule:
    - cron: '0 12 * * 1'  # weekly Monday noon UTC
jobs:
  tier4-check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Check if 30 days post-ship
        run: python scripts/check_tier4_due.py
      - name: Run Tier 4 if due
        if: env.TIER4_DUE == 'true'
        run: python scripts/phase1_post_ship_monitor.py
      - name: Notify if regression detected
        if: failure()
        uses: actions/github-script@v7
        with:
          script: |
            github.rest.issues.create({
              owner: context.repo.owner,
              repo: context.repo.repo,
              title: 'Tier 4: Phase 1 post-ship regression detected',
              body: 'See workflow run for details. Per v6 §6.1, investigate first; do not auto-revert.'
            });
```

The workflow runs weekly, but only takes action on the first Monday after day 30. This provides scheduling without depending on the agent's session-bound memory.

### 9.c Tier 3 retry sign-off

**Failure mode:** Per RFC §10.5, a retry of the gate experiment requires sign-off. In solo mode the sign-off authority is the same person who decides to retry, which weakens the discipline.

**Mitigation:** Before triggering the second gate run:

1. Write a justification document at `docs/plans/phase-2-measurement-protocol-retry-rationale.md` covering: (a) what protocol violation triggered the halt, (b) what mitigation was applied to prevent recurrence, (c) why a retry is more cost-effective than abandoning Phase 2 or re-pre-registering with `phase-2-measurement-protocol-v2.md`, (d) the estimated $2k–$4k cost.
2. Commit the justification.
3. Send the file to **one external reader** (mirroring the personal-project-contract pattern). Wait for an acknowledgment that the reasoning is plausible — not approval, just acknowledgment.
4. Then trigger the retry.

This adds ~24 hours of latency to the retry decision, which is small relative to the gate's 4–5 day run and the $2k–$4k retry cost.

### 9.d When this section doesn't apply

If at any point the project transitions to a multi-person team (separate agent operator, maintainer, and statistician roles), §9 may be dropped in favor of the natural separation of concerns. v6 §9 exists for the operational reality, not the aspirational team structure.

---

## §15 (NEW) — PR-1d collapse decision

v5-implementation's PR-1d (byte-preservation verifier) is potentially redundant with PR-1c (the migrator). v5 RFC §5.7.3 establishes byte-preservation as a *by-construction* property of the text-edit migrator: it removes only the byte ranges identified as `{id…}` blocks, leaving everything else untouched.

If by-construction holds, a separate verifier asserts what the migrator already provides. The verifier may still be useful as:

- A regression-test target distinct from the migrator's own tests.
- A reusable tool for future migrators (PR-2d's compact-id migrator, future RFC migrators).
- A documentation artifact for the byte-preservation property.

**v6 keeps PR-1d as a separate PR**, but marks it as **"may collapse into PR-1c's tests if the maintainer judges at PR-1d kickoff that the verifier adds no testing surface beyond PR-1c's unit tests."** The decision is recorded at PR-1d kickoff in the PR description, not pre-emptively in this plan.

The 5 test cases in v5 RFC §5.7.6 (T-5.7-a through T-5.7-e — citation verified by `rg "T-5\.7" docs/plans/path-2-drop-ids-v5.md` returning 5 matches at lines 123–127) are written regardless: as PR-1d tests if PR-1d ships, or as PR-1c tests if PR-1d collapses.

---

## §17 — Decision

v5-implementation's recommendation stands, refined by v6's 12 calibrations and one institutional observation:

1. **Approve v6, ship the prerequisite PRs in week 0.** PR-0a (with v6 §7-bis added), PR-0b (with v6 §4.0/4.1/4.3/4.4 corrections), PR-0c (with v6 §5.1 baseline range), PR-0d (with v6 §3.1 measured-budget + §7-cross-platform Python implementation).
2. **Begin Phase 1 in week 1.** Phase 1 PRs land per the v5-implementation §2.2 sequence, with PR-1d possibly collapsing per v6 §15.
3. **Begin Phase 2 in week 3** (subject to the week-2.5 dependency checkpoint from v5 §11).
4. **Run the §10 gate in week 4** with v6 §3.3 monitoring mechanism, §4.0/§4.1/§4.3/§4.4 pre-reg corrections, and §9 solo-mode adjustments.
5. **Apply v6 §0.2 lesson going forward.** The §7-bis reviewer-side rule lands in PR-0a alongside the original §7 content. The next review document (v6 critique round, if any) is expected to grep-verify artifact-state claims.

v6 is the **terminal implementation plan revision** barring discovery of another factual error in v5-implementation's substance (not just its reviews). The convergence of both v5-implementation reviewers on "approve, ship the prerequisite PRs" — even after one made a false blocking claim — suggests further iteration is diminishing returns. PR-0a..0e ships in week 0; the work begins.

---

## Appendix — v5-impl → v6 review summary

| Reviewer | Verdict | v6 disposition |
|----------|---------|----------------|
| Designer critique | "Approve as-is. Begin PR-0a..0e in week 0." (10 calibration items) | All 10 items folded in (items 1, 2, 3, 4, 5, 6, 7, 8 mapped to v6 §1-header, §3.2, §1.2-split, §9, §3.1.a, §4.3, §3.3, §3.5; items 9, 10 are duplicates/verifications, no plan change). |
| Devil's advocate | "Revise once, then green-light. 1 blocking + 4 substantive + 6 smaller." | The 1 "blocking" item (§8 missing) is verified false (v6 §0.2); 4 substantive items folded in (v6 §2.3, §4.0/§4.1, §7-cross-platform, §5.1); 6 smaller items folded in (v6 §3.1.c, §3.1.b, §15, §4.4, §1-header, §7-bis). |

Both reviewers said the plan is shippable. v6 captures all valid calibrations and documents the one invalid claim as an instance of the same architectural-fiction pattern §7 was designed to prevent — applied this time to artifact-state claims by reviewers.

## Appendix B — Confidence statement

**v6 confidence: High.**

Justification:
1. Both reviewers explicitly said "approve, ship the prerequisites in week 0."
2. Every substantive critique item is folded in with grep-verified facts (PR-2c's 7 call sites, fixture directory names, lexer behavior, file structure of v5-implementation).
3. The §0.2 observation (the §7 rule applies to reviewers too) closes the meta-loop: v6 has been adversarially reviewed, and the rule that catches author-side fictions also caught a reviewer-side fiction this round.
4. v6 introduces no new architectural claims to be a v7 target. It only tightens citations, expands one PR scope, names one statistical-test fallback, and adds one operational section (§9 solo-mode).

Residual: v6 itself has not been adversarially reviewed. The trajectory pattern (v2→v6) suggests another round might surface 1–2 calibration items, but the substantive review surface is now small (most of the v5-implementation calibration items were one-line wording fixes). Another iteration is plausibly diminishing returns.
