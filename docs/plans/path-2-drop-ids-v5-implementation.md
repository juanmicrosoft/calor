# v5 Implementation Plan — Compact Stable Identifiers

**Status:** Draft, awaiting maintainer sign-off
**Implements:** [path-2-drop-ids-v5.md](path-2-drop-ids-v5.md)
**Companions:**
  - `docs/process/rfc-review-checklist.md` (authored as PR-0a per §2; content drafted here in §7)
  - `docs/plans/phase-2-measurement-protocol.md` (authored as PR-0b; content drafted here in §4)
  - `tests/E2E/agent-tasks/templates/path-2-gate/` (6 new task templates per §5)

> **Reader's note.** v5 (the RFC) specifies *what* to build and the *human-grade* §10 statistical gate. This plan specifies *how* the work is sequenced, *what the coding agent must run per PR*, and *what "high confidence" means operationally*. The largest new content is §3 (the tiered self-verification harness) — the RFC assumes humans run the gate; this plan addresses how a coding agent can ship PRs without waiting 4–5 days for the gate per change.

---

## §1 — Scope and prerequisites

### 1.1 What this plan delivers

| Deliverable | Owner | Form | Where |
|-------------|-------|------|-------|
| PR-by-PR breakdown for Phase 0/1/2 | This plan | §2 | here |
| Coding-agent self-verification harness (3 tiers) | This plan | §3 + scripts/ | here + `scripts/verify-*.sh` |
| Phase 2 pre-registration document (the actual one, not the template) | PR-0b | §4 here = its content | `docs/plans/phase-2-measurement-protocol.md` |
| 6 new benchmark task templates | PR-0c | §5 skeleton + rubric | `tests/E2E/agent-tasks/templates/path-2-gate/` |
| Rollback playbook (operational decision tree) | This plan | §6 | here |
| `docs/process/rfc-review-checklist.md` (per RFC §0.1) | PR-0a | §7 draft | `docs/process/rfc-review-checklist.md` |
| Definition of "high confidence" — per-PR, per-phase, per-release | This plan | §8 | here |

### 1.2 Hard prerequisites before Phase 1 implementation begins

Per RFC §10.2: "before any Phase 2 implementation code is written, `docs/plans/phase-2-measurement-protocol.md` must be committed." v5 extends this with additional prerequisites:

| Prerequisite | Why | PR |
|--------------|-----|----|
| `docs/process/rfc-review-checklist.md` committed | Codifies the §0.1 grep-verify rule before authors touch any RFC-adjacent work | PR-0a |
| `docs/plans/phase-2-measurement-protocol.md` committed | RFC §10.2 hard prerequisite for Phase 2; lands early to give time for review | PR-0b |
| 6 new benchmark task templates committed | Per RFC §10.1 the corpus is 24 existing + 6 new; the new 6 must exist before Phase 1 starts so the corpus is stable | PR-0c |
| `scripts/verify-phase1.sh` (Tier 1 harness) committed and CI-integrated | The agent needs Tier 1 to exist *before* writing Phase 1 PRs, so it can self-verify as it goes | PR-0d |
| §8.3 standalone diagnostic addressing shipped in a 0.x patch | RFC §11: this ships first (week 0), in days, separate from 0.x+1 | PR-0e |

**Phase 1 implementation PRs (PR-1*) cannot start until PR-0a through PR-0d are merged.** PR-0e is independent and ships on a different release cadence.

### 1.3 Non-goals of this plan

- Does not author the **actual code** for Phase 1 or Phase 2; that work is the PRs themselves.
- Does not specify the exact bash/Python of every script — gives interface, inputs, outputs, exit codes; PR-0d implements.
- Does not author the **actual content** of the 6 new benchmark tasks — §5 gives the rubric and skeleton; PR-0c fills them in.
- Does not replace the human-run §10 statistical gate; specifies how the coding agent gets shorter-horizon confidence (§3) so it can ship intermediate PRs without blocking on the gate.

---

## §2 — PR breakdown

15 PRs total: 5 prerequisite (PR-0*), 8 Phase 1 (PR-1*), 6 Phase 2 (PR-2*). Phase 2 PRs are conditional on the §10 gate.

### 2.1 Phase 0 — Prerequisites (week 0, parallel; can land in any order except PR-0d which depends on PR-0c for testing)

| PR | Title | Files touched | Acceptance criteria | Tier | Reviewer |
|----|-------|---------------|---------------------|------|----------|
| **PR-0a** | Add RFC review checklist | `docs/process/rfc-review-checklist.md` (new), `CLAUDE.md` cross-reference | Checklist exists, CLAUDE.md links to it, no code changes | none (doc) | maintainer |
| **PR-0b** | Author Phase 2 pre-registration doc | `docs/plans/phase-2-measurement-protocol.md` (new) | Doc covers all 9 items in RFC §16.E; cites RFC §10.3 thresholds verbatim | none (doc) | maintainer |
| **PR-0c** | 6 new agent-task templates for §10 gate | `tests/E2E/agent-tasks/templates/path-2-gate/task-{01..06}/{task.md, setup/, expected/, acceptance.sh}` | Each task passes `scripts/validate-task-template.sh`; each is solvable today by a baseline agent in ≤30 turns | Tier 1 | compiler + benchmark owner |
| **PR-0d** | Tier 1 self-verification harness | `scripts/verify-phase1.sh`, `scripts/byte-preservation-check.sh`, `scripts/ast-roundtrip-check.sh`, `scripts/token-delta-spot.sh`, `.github/workflows/tier1.yml` | All scripts: exit 0 on green corpus, exit 1 on synthetic regression; CI workflow runs on every PR; runs in <60s locally | Tier 1 | compiler |
| **PR-0e** | §8.3 standalone diagnostic addressing | Compiler diagnostic emission for Calor0700-series (per RFC §8.3); ships in next 0.x patch | New diagnostics surface with correct codes; snapshot tests cover all paths | Tier 1 | compiler |

### 2.2 Phase 1 — Drop structural IDs (weeks 1–3, on `phase-1` branch)

| PR | Title | Files touched | Acceptance criteria | Tier | Reviewer |
|----|-------|---------------|---------------------|------|----------|
| **PR-1a** | Lexer: make `{id…}` optional on structural openers | `src/Calor.Compiler/Parsing/Lexer.cs`, `Token.cs`; new lexer unit tests | Lexer accepts both `§M{id:Name}` and `§M Name`; tests cover all structural tag forms (M, F, L, IF, TR, etc.) | Tier 1 | compiler |
| **PR-1b** | Parser: omit structural IDs from AST nodes | `src/Calor.Compiler/Parsing/Parser.cs`, AST node changes for nodes that previously held a structural ID (drop the field) | Round-trip parse → emit → parse preserves AST equality; existing tests still pass; new tests cover both forms | Tier 1 | compiler |
| **PR-1c** | Migrator: `calor fix --drop-structural-ids` command | `src/Calor.Compiler/Migration/StructuralIdDropper.cs` (new), CLI wiring; writes `migration.log.json` | Migrator on `samples/` and `tests/` produces byte-identical-outside-removed-ranges output; log is round-trippable | Tier 1+2 | compiler |
| **PR-1d** | Byte-preservation verifier (RFC §5.7.3) | `src/Calor.Compiler/Migration/BytePreservationVerifier.cs` (new), `scripts/verify-byte-preservation.sh` | Verifier asserts byte-equality over non-migrated regions; the 5 test cases in RFC §5.7.6 (T-5.7-a through T-5.7-e) all pass | Tier 1 | compiler |
| **PR-1e** | Update `samples/` to optional-ID form | All `samples/*.calr` edited via `calor fix --drop-structural-ids`; commit `migration.log.json` | All samples compile; byte-preservation verifier green; visual diff is "remove `{id…}` blocks only" | Tier 1+2 | compiler |
| **PR-1f** | Update `tests/` fixtures to optional-ID form | All `tests/**/*.calr` migrated; commit logs | All tests still pass; verifier green | Tier 1+2 | compiler |
| **PR-1g** | Diagnostic Calor0820 implementation (RFC §8.1) | `src/Calor.Compiler/Diagnostics/`, analyzer hooks; snapshot tests | Calor0820 fires for legacy `{id…}` blocks with a `fix` payload that removes them | Tier 1 | compiler |
| **PR-1h** | Phase 1 docs + post-ship monitoring scaffold | `docs/syntax-reference.md`, MCP primer; `scripts/phase1-post-ship-monitor.sh` (per RFC §10.6) | Docs reflect new syntax; monitor script runs (but does not yet have a baseline to compare against) | Tier 1 | maintainer |

Phase 1 merge gate: all Tier 1 green, Tier 2 (corpus-wide) green on the merged `phase-1` branch. The week-2.5 dependency checkpoint (RFC §11) is the deadline for PR-1a (parser change); if it slips beyond week 2.5, Phase 2 shifts.

### 2.3 Phase 2 — Compact symbol IDs (weeks 3–4, on `phase-2` branch rebased on `phase-1`)

| PR | Title | Files touched | Acceptance criteria | Tier | Reviewer |
|----|-------|---------------|---------------------|------|----------|
| **PR-2a** | Crockford-lowercase 12-char ID generator | `src/Calor.Compiler/Ids/CompactIdGenerator.cs` (new); preserve `IdGenerator` for the legacy ULID path during transition | Generator outputs 12-char IDs from the `0123456789abcdefghjkmnpqrstvwxyz` alphabet (excludes `i,l,o,u` per RFC §6.1); unit tests cover collision math at sampled densities | Tier 1 | compiler |
| **PR-2b** | `IdRegistry` implementation | `src/Calor.Compiler/Ids/IdRegistry.cs` (new) with `ConcurrentDictionary` per RFC §6.3.1 | Registry supports registration, lookup, collision detection; concurrency-safe (existing callers are single-threaded; defense-in-depth per v5 §6.3.1 annotation) | Tier 1 | compiler |
| **PR-2c** | `IdGenerator.Generate()` caller audit + migration (RFC §9.3) | `src/Calor.Compiler/Ids/IdAssigner.cs:175` and `:180` (the 2 production call sites grep-verified in v5) | Both sites switched to `CompactIdGenerator`; legacy `IdGenerator.Generate()` kept for migrator's reverse-path only; tests green | Tier 1 | compiler |
| **PR-2d** | Migrator: `calor fix --compact-ids` command | `src/Calor.Compiler/Migration/CompactIdMigrator.cs` (new); writes additional entries to `migration.log.json` | Migrator rewrites symbol IDs (those carrying `fix`-target identity); log supports reverse migration via `calor fix --revert-compact-ids` | Tier 1+2 | compiler |
| **PR-2e** | Diagnostics Calor0821 + Calor0822 (RFC §8.2) | `src/Calor.Compiler/Diagnostics/`; full Calor0822 text per RFC §8.2.1 | Both diagnostics fire with correct messaging; Calor0822 includes the git-history recovery path text | Tier 1 | compiler |
| **PR-2f** | Phase 2 final integration + revert path tested | `src/Calor.Compiler/Migration/CompactIdReverter.cs` (new); `tests/Calor.Migration.Tests/RevertRoundTripTests.cs` | Round-trip: ULID source → compact-ids → revert-compact-ids → byte-equal to original | Tier 1+2 | compiler |

Phase 2 ship gate: §10 statistical gate (Tier 3) passes per RFC §10.3 (medium effect, all 4 kill criteria).

### 2.4 PR dependencies

```
PR-0a ─┐
PR-0b ─┤
PR-0c ─┼─→ PR-0d (Tier 1 harness needs templates to test against)
       │
       │   week 0
       ▼
PR-0e (independent, ships in 0.x patch)

       PR-0a..d merged → Phase 1 starts
       ┌──────────────────────────────┐
       │                              │
       ▼                              ▼
   PR-1a ─→ PR-1b ─→ PR-1c ─→ PR-1d   (compiler track)
                                  │
                                  ▼
                              PR-1e, PR-1f, PR-1g  (parallel)
                                  │
                                  ▼
                              PR-1h (final)

       Phase 1 merged + week-2.5 checkpoint passed → Phase 2 starts
                                  │
                                  ▼
              PR-2a ─→ PR-2b ─→ PR-2c ─→ PR-2d ─→ PR-2e ─→ PR-2f
                                                              │
                                                              ▼
                                                       Tier 3 gate (§10)
                                                              │
                              ┌───────────────────────────────┤
                              ▼                               ▼
                          Gate PASS                       Gate FAIL
                              │                               │
                              ▼                               ▼
                    Ship Phase 1 + Phase 2            Ship Phase 1 only
                       as 0.x+1                          as 0.x+1
                                                     (revert Phase 2)
```

---

## §3 — Coding-agent self-verification harness (the key new content)

**Problem the harness solves.** RFC §10 is a 4–5-day, 900-task-run, statistical gate. A coding agent shipping PR-1a through PR-2f cannot block on the gate per PR (15 × 5 days = a year of calendar). The agent needs a sub-hour, deterministic, green/red signal per PR that gives **engineering confidence** the change is safe. The §10 gate then validates that the safe-per-PR changes have the **expected aggregate effect** on agent behavior.

The harness has three tiers, distinguished by runtime, what they verify, and who runs them.

### 3.1 Tier 1 — Per-PR sanity (<60 seconds)

**Runs:** locally before pushing, in CI on every PR.
**Authoritative location:** `scripts/verify-phase1.sh` (extended to `verify-phase2.sh` when PR-2a lands).
**Components:**

| Check | Script | What it asserts | Failure mode |
|-------|--------|-----------------|--------------|
| Unit tests | `dotnet test --filter Category=Unit` (existing) | All unit-tagged tests pass | Tier 1 red |
| Byte-preservation on samples/ | `scripts/byte-preservation-check.sh samples/` | For each file: migrate → reconstruct from log → byte-equal to original | Tier 1 red |
| AST round-trip on tests/ | `scripts/ast-roundtrip-check.sh tests/` | For each `.calr` fixture: parse → emit → parse → AST-equal | Tier 1 red |
| Token-delta spot check | `scripts/token-delta-spot.sh tests/E2E/agent-tasks/templates/path-2-gate/task-01/setup/main.calr` | Print Δtokens (pre → post migration); compare to expected from RFC §16.F (~9.67 × N_IDs_in_file) | Tier 1 amber (printed warning, not red) — soft signal because per-file variance is high |

**Acceptance rule for an agent:** **The agent may not mark a PR "ready for review" until `scripts/verify-phase1.sh` exits 0 and the agent pastes the full output into the PR description.** This is operationally how "high confidence per PR" is enforced.

CI runs the same harness on every PR; the green/red badge is the second enforcement layer.

### 3.2 Tier 2 — Per-phase verification (<30 minutes)

**Runs:** on PR to a release branch (`phase-1`, `phase-2`, or `release/0.x+1`); manually invokable for sanity checks.
**Authoritative location:** `scripts/verify-corpus.sh`.
**Components:**

| Check | Script | What it asserts | Failure mode |
|-------|--------|-----------------|--------------|
| All Tier 1 checks (re-run on whole corpus) | `scripts/verify-phase1.sh --corpus all` | Tier 1 properties on the full corpus, not just samples | Tier 2 red |
| Migrator corpus dry-run | `scripts/migrator-corpus-dryrun.sh` | Run migrator with `--dry-run` on every `.calr` in repo; byte-preservation green for all | Tier 2 red |
| Token-delta corpus aggregate | `scripts/token-delta-corpus.sh` | Aggregate Δtokens across whole corpus is within expected range (Phase 1: ~9.67 × N_structural_IDs ± 20%; Phase 2: additional ~9.67 × N_symbol_IDs ± 20%) | Tier 2 red if outside range (indicates either a measurement bug or an unexpected migrator behavior) |
| Diagnostic snapshot tests | `dotnet test --filter Category=DiagnosticSnapshot` (existing pattern) | All emitted diagnostics match golden files; new Calor0820/0821/0822 diagnostics covered | Tier 2 red |
| Round-trip migration test | `scripts/migrator-revert-roundtrip.sh` | Original → migrate → revert → byte-equal to original, for every file in corpus | Tier 2 red |

**Acceptance rule for a phase merge:** Tier 2 must be green on the merge commit of the release branch. The maintainer reviews the Tier 2 output as part of release sign-off.

### 3.3 Tier 3 — The §10 statistical gate (4–5 days, human-driven)

**Runs:** once, at week 4, on the merged `release/0.x+1` candidate, per RFC §10.
**Authoritative location:** `scripts/run-phase-2-gate.sh` (drives the agent harness across all 30 tasks × 10 runs × 3 arms = 900 runs).
**Owner:** repo maintainer.
**Agent's role:**
- Prep: ensure `scripts/run-phase-2-gate.sh` correctly drives the three arms, model versions are pinned per pre-registration (PR-0b), seeds are recorded.
- Monitor: watch for protocol violations (agent harness crash, model API outage, fixture mutation); if detected, halt and trigger RFC §10.5 retry policy.
- Report: write `docs/plans/phase-2-measurement-results.md` from raw analysis output, regardless of pass/fail (per RFC §16.E item 8).

**Pass condition:** all 4 kill criteria green per RFC §10.3 (medium effect, δ ≥ 0.33, Bonferroni-corrected α' = 0.0125).
**Fail condition:** any kill criterion red → Phase 2 reverted from `release/0.x+1`; ship Phase 1 alone.

### 3.4 Tier 4 — Post-ship monitoring (per RFC §10.6, runs 30 days after 0.x+1 ship)

**Runs:** `scripts/phase1-post-ship-monitor.sh` (committed in PR-1h).
**Authoritative location:** `scripts/phase1-post-ship-monitor.sh`.
**What it does:** re-runs the 24+ existing agent-task fixtures × 10 runs against 0.x+1's Phase-1-only build; compares turn-count median against the latest 0.x pre-release tag; if |Cliff's δ| ≥ 0.2 regression, surfaces an alert.
**Owner:** maintainer schedules the run; agent can be invoked to produce the analysis report.

### 3.5 Confidence calculus — what each tier proves

| Combination | What you can claim | What you can NOT claim |
|-------------|--------------------|------------------------|
| Tier 1 green | "This PR did not break the build or local invariants." | "This PR is safe to merge into a release branch." |
| Tier 1 + Tier 2 green | "This PR preserves system invariants across the whole corpus; aggregate token deltas are in expected range." | "Agents will actually benefit from this change." |
| Tier 1 + Tier 2 + Tier 3 green | "Agents materially benefit (per RFC §10.3 medium-effect threshold), with statistical significance, and no regressions." | "Long-term codebase rot is unaffected" — per RFC §10.7 the gate explicitly does not measure this. |
| Tier 4 (post-ship) green at day 30 | "Phase 1 in production has not introduced a turn-count regression detectable at the small-effect level." | Anything about Phase 2 — Phase 2 has its own gate (Tier 3). |

### 3.6 What if Tier 1 is too slow?

If `scripts/verify-phase1.sh` exceeds 60s on a developer machine, the harness fails its own design constraint. The remediation order:

1. Profile each check; the AST round-trip on `tests/` is the likely culprit. If so, split into "default" (samples/ only, runs always) and "extended" (tests/, runs in CI only).
2. Cache parse results between checks within a single harness invocation.
3. Only as a last resort, drop a check from Tier 1 (move to Tier 2). This loses per-PR confidence and should be documented in `scripts/verify-phase1.sh` with a banner explaining the trade-off.

### 3.7 What if the harness itself has a bug?

The harness is code; code has bugs. Mitigations:

- **Self-tests.** `scripts/verify-phase1.sh --self-test` runs the harness against synthetic positive and negative fixtures (a "should-pass" file and a "should-fail" file). PR-0d includes these self-tests.
- **CI canary.** PR-0d adds a CI job that runs the harness against a known-good main commit; if it fails, the harness is broken, not main.
- **Triage rule.** If Tier 1 is red but the agent believes the change is safe, the agent must NOT bypass the harness; instead, file an issue against the harness, and the maintainer either confirms the harness bug or confirms the change is unsafe.

---

## §4 — The Phase 2 pre-registration document (content for `docs/plans/phase-2-measurement-protocol.md`)

This section is the **content** of the document authored as PR-0b. Per RFC §16.E, the document must contain these 9 items. The drafts below should be moved to the pre-reg file verbatim (with hashes and versions filled in at PR-0b authoring time).

### 4.1 Task list with commit hashes

```
Existing tasks (24):
  tests/E2E/agent-tasks/fixtures/task-001/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/fixtures/task-002/  @ <commit-sha-to-fill>
  ... (24 entries) ...

New tasks (6, authored in PR-0c):
  tests/E2E/agent-tasks/templates/path-2-gate/task-01/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/templates/path-2-gate/task-02/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/templates/path-2-gate/task-03/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/templates/path-2-gate/task-04/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/templates/path-2-gate/task-05/  @ <commit-sha-to-fill>
  tests/E2E/agent-tasks/templates/path-2-gate/task-06/  @ <commit-sha-to-fill>
```

### 4.2 New task authoring artifacts

Each new task in `path-2-gate/task-NN/` contains:
- `task.md` — prompt given to the agent
- `setup/` — pre-existing `.calr` files in the workspace at task start
- `expected/` — post-task expected state (for deterministic acceptance check)
- `acceptance.sh` — exit-0-on-success script that diffs agent output against expected

### 4.3 Implementation flag — branch + commit hash per arm

```
Arm A (today/baseline):   main @ <commit-sha-to-fill> (latest 0.x pre-release tag)
Arm B (Phase 1 only):     release/0.x+1 @ <commit-sha-to-fill> (after PR-1h merges)
Arm C (Phase 1 + Phase 2): release/0.x+1 @ <commit-sha-to-fill> (after PR-2f merges)
```

### 4.4 Run protocol

- **Agent harness invocation:** `./tests/E2E/agent-tasks/run.sh --task <id> --arm <A|B|C> --seed <n> --model <pinned>`
- **Model version (pinned):** e.g., `claude-sonnet-4.5` at the version available on `2026-MM-DD`; record the API model string verbatim. Locked at pre-registration; any model change invalidates the run per RFC §10.5.
- **Random seed:** seeds 1..10 per (task, arm) combination, recorded; harness must accept and respect the seed.
- **Concurrency:** runs are sequential within a (task, arm) tuple; parallelism allowed across tuples.

### 4.5 Data schema (recorded per run)

```json
{
  "task_id": "task-001",
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
  "raw_log_path": "..."
}
```

### 4.6 Analysis pipeline

- Library versions pinned: `scipy==<version>`, `pandas==<version>`, `numpy==<version>`.
- Analysis seed: 42 (only matters for any bootstrap CI computation).
- Code path: `scripts/analyze-gate-results.py` (committed in PR-0b alongside the pre-reg doc).
- Outputs: `phase-2-measurement-results.md` summarizing all 4 kill criteria per RFC §10.3.

### 4.7 Pass/fail thresholds (per RFC §10.3)

All four must be true to ship Phase 2:

1. McNemar p > 0.0125 (Bonferroni-corrected, no success-rate regression).
2. Wilcoxon p > 0.0125 (no identity-preservation regression).
3. (Turn-count median reduction ≥ 10% AND Wilcoxon p < 0.0125 AND **|Cliff's δ| ≥ 0.33**) OR (Token median reduction ≥ 15% AND Wilcoxon p < 0.0125 AND **|Cliff's δ| ≥ 0.33**).
4. Phase 2 distinguishable from Phase 1 alone on criterion (3), Wilcoxon p < 0.0125.

### 4.8 Reporting commitment

`docs/plans/phase-2-measurement-results.md` will be written and committed *regardless of pass or fail*. The doc reports all four criteria with their p-values, effect sizes, and ship/no-ship decision. This is RFC §16.E item 8 verbatim.

### 4.9 Retry policy reference

Per RFC §10.5: one retry covered by reserve budget; further retries require re-pre-registration in `phase-2-measurement-protocol-v2.md`. Sign-off authority for retry: maintainer.

---

## §5 — The 6 new benchmark task templates (skeleton + rubric for PR-0c)

### 5.1 Authoring rubric

Each task in `tests/E2E/agent-tasks/templates/path-2-gate/task-NN/` must satisfy:

| Property | Requirement |
|----------|-------------|
| **Solvable today** | A baseline agent (Arm A) must achieve ≥80% success across 10 seeds. If a task is unsolvable today, it cannot distinguish Phase 1/2 from baseline. |
| **Multi-edit** | Each task requires the agent to edit ≥3 files OR ≥5 distinct locations within a file. Single-file, single-edit tasks do not stress the ID-identity properties under test. |
| **Cross-reference touching** | Each task must include at least one edit that crosses a structural-ID or symbol-ID boundary (e.g., rename a function and update its callers; add a new contract that references an existing function ID). This is what stresses the "did dropping/compacting IDs hurt agent comprehension" question. |
| **Deterministic acceptance** | `acceptance.sh` must exit 0 if and only if the task is solved correctly. No LLM-as-judge in the acceptance step; the gate must be reproducible. |
| **Expected turn count** | The task's expected median turn count for a baseline agent must be 10–30. <10: too easy, no signal; >30: too expensive at N=900 runs. |
| **Authored independently per arm** | The task prompt itself does **not** reference structural IDs in any arm; the difference between arms is the *workspace state*, not the *task instructions*. |

### 5.2 Suggested task themes (PR-0c author should expand each into a full template)

| # | Theme | Why it stresses the ID system |
|---|-------|-------------------------------|
| **task-01** | "Refactor function `Calculate` into three smaller functions in the same module" | Phase 1: agent must locate function-end positionally; Phase 2: new function names need stable symbol IDs |
| **task-02** | "Add the `db` effect to an existing function and its 2 transitive callers" | Tests cross-file symbol references; in Phase 2, callers reference compact IDs |
| **task-03** | "Change a method from private to public and update all 5 call sites across 3 files" | Tests cross-file identity; ID compaction here exercises the IdRegistry round-trip |
| **task-04** | "Add a postcondition to an existing function that references a parameter" | Tests contract-to-function symbol-ID references in Phase 2 |
| **task-05** | "Restructure a nested 3-level loop into a separate helper function" | Structural restructure; tests that without structural IDs, the agent can still navigate by name and position |
| **task-06** | "Add an error-handling try/catch around an effectful call chain" | New structural blocks added; Phase 1 stresses positional vs. ID-based block identification |

Each theme is one paragraph; the actual template in PR-0c expands to ~10–15 files of fixture content plus prompt + acceptance script.

### 5.3 Validation

`scripts/validate-task-template.sh path-2-gate/task-NN/` (added in PR-0d) checks:

1. `task.md` exists and is non-empty.
2. `setup/` contains at least one `.calr` file.
3. `expected/` contains at least one file used by `acceptance.sh`.
4. `acceptance.sh` is executable and exits 0 when invoked on `expected/` contents.
5. A pilot run against `setup/` does NOT yet satisfy `acceptance.sh` (i.e., the task is non-trivial).

PR-0c is not mergeable until `scripts/validate-task-template.sh` exits 0 for all 6 tasks.

---

## §6 — Rollback playbook (operational decision tree)

### 6.1 Decision tree

```
PR-level Tier 1 RED?
  → revert PR; no production impact; agent files issue if cause unclear.

Phase-merge Tier 2 RED?
  → block phase merge; maintainer triages; either fix-forward (small fix to a PR-N PR)
    or revert specific PR(s); re-run Tier 2.

§10 gate (Tier 3) FAIL for Phase 2?
  → revert Phase 2 from release/0.x+1 (drop PR-2a..2f).
  → re-validate Phase 1 alone via Tier 2 on the reverted branch.
  → ship Phase 1 only as 0.x+1; per RFC §11.
  → log gate outcome in phase-2-measurement-results.md (per RFC §16.E item 8).
  → if retry policy invoked (RFC §10.5), schedule re-pre-registration as
    phase-2-measurement-protocol-v2.md.

Tier 4 (post-ship Phase 1 monitoring) RED at day 30?
  → INVESTIGATE FIRST. A regression detected by the |Cliff's δ| ≥ 0.2 trigger
    is the *signal*, not automatic revert.
  → Maintainer reviews tier-4 data; if confirmed and material, schedule a
    0.x+1 → 0.x+2 release that reverts Phase 1.
  → Phase 1 revert path: `calor fix --revert-drop-structural-ids` consuming
    the migration.log.json produced by PR-1c migrator. (If no log exists for
    user — e.g., they upgraded a new project — surface guidance to recover
    from git history.)

Post-ship Phase 2 success-rate or identity-preservation regression?
  → RFC §10.4: `calor fix --revert-compact-ids` consumes migration.log.json,
    symmetric and deterministic, tested in PR-2f.
  → For projects without migration.log.json: Calor0822 diagnostic surfaces
    the git-history recovery path (per RFC §16.C).
```

### 6.2 What the agent does in each scenario

| Scenario | Agent action |
|----------|--------------|
| Tier 1 red on a PR I authored | Investigate locally; if I can fix in <30 min, push fix; otherwise revert and reopen as a fresh PR with the fix |
| Tier 2 red on a phase merge | Open an issue with the Tier 2 output; do not attempt to merge-around; wait for maintainer triage |
| Tier 3 fail (Phase 2 gate) | Execute the revert per §6.1 step 3 *without* delay: revert Phase 2 PRs from `release/0.x+1`, re-run Tier 2 on the reverted branch, push to maintainer for ship-Phase-1-alone sign-off |
| Tier 4 red at day 30 | Surface the data; do NOT initiate a revert without maintainer sign-off (post-ship reverts are user-facing breakage) |

### 6.3 What the agent never does

- Bypass a red harness by editing the harness itself.
- Revert a post-ship release without maintainer sign-off.
- Mark a PR done without pasting Tier 1 output.
- Author Phase 2 implementation code before `docs/plans/phase-2-measurement-protocol.md` is merged (RFC §10.2 hard rule).
- Edit the pre-registration document after it lands (RFC §10.5: any change invalidates the run).

---

## §7 — `docs/process/rfc-review-checklist.md` (draft content for PR-0a)

> This section is the **content** of the file PR-0a creates. Move verbatim into `docs/process/rfc-review-checklist.md` (adjusting for repo conventions: front matter, license header if any).

```markdown
# RFC Review Checklist

This checklist applies to all RFC-class documents under `docs/plans/`.
The lesson driving this checklist: the v2→v3→v4→v5 trajectory of the
Compact Stable Identifiers RFC caught one *architectural fiction* per
review round — a sentence asserting the compiler does X when, in fact,
the source code did not do X. The checklist exists to catch the next
analogous fiction at PR review time, not at vN+1.

## For RFC authors

1. **Architectural claim rule.** Every sentence of the form
   "the compiler does X", "the verifier asserts Y", or "the migrator
   guarantees Z" MUST EITHER:
   - cite a file:line in the source tree, OR
   - be explicitly marked `[PROPOSED]` to indicate the behavior is
     proposed but not yet implemented.

2. **Effort estimates with citations.** When estimating effort for an
   audit/refactor task, cite a grep result for the call site count:
   - GOOD: "0.5 day — IdGenerator.Generate() has 2 production call sites
     (Ids/IdAssigner.cs:175, :180)"
   - BAD: "0.5–2 days" (unfounded upper bound)

3. **Measurement claims.** When citing a numeric measurement
   (token counts, runtime, memory), include the script that produces
   the number, the sample size, and (for noisy measurements) a
   confidence interval. Hand-picked samples of N < 20 are not
   acceptable for headline numbers.

## For RFC reviewers

1. **Grep-verify every architectural claim.** Take 30 seconds per
   architectural assertion to run `rg` against the cited file:line.
   If the citation is missing OR if grep does not find the asserted
   behavior, add a `[VERIFY]` comment to the RFC PR.

2. **Be the devil's advocate explicitly.** Pair each RFC with two
   review documents:
   - A designer-voice critique (read the RFC charitably, find
     genuine improvements).
   - A devil's-advocate critique (read the RFC adversarially, find
     the assertions that don't hold up).

3. **Verdict convergence is the ship signal.** When both reviewers
   converge on "approve and ship" with only calibration concerns,
   author one calibration revision (capturing the calibrations
   in-line) and ship. Do not iterate further unless a new
   architectural concern emerges.

## For all RFC contributors

- A new RFC version (vN+1) is justified when EITHER:
  - A reviewer identifies an architectural fiction (claim not in
    the source), OR
  - A reviewer identifies a missing required artifact (statistical
    test, retry policy, rollback path).
- A new RFC version is NOT justified for:
  - Wording polish,
  - Adding citations to claims that are correct,
  - Reorganizing structure without changing substance.
- Calibration deltas should be captured in the next-version's §0
  change table; new RFC versions should reference predecessors and
  document deltas only.
```

---

## §8 — Definition of done

### 8.1 Per PR

- Tier 1 green (CI badge AND agent-pasted output in PR description).
- PR description references the relevant RFC section(s) ("Implements RFC §5.4 lexer change") so the reviewer can verify scope alignment.
- For PRs touching the migrator: a `migration.log.json` sample committed in `tests/` covering the new migrator behavior.
- For PRs introducing diagnostics: snapshot test + a sample `.calr` file that triggers the diagnostic.

### 8.2 Per phase merge

- All PRs in the phase merged.
- Tier 2 green on the phase branch's HEAD.
- Maintainer sign-off on the phase merge PR.

### 8.3 Per release (0.x+1)

- Phase 1: Tier 2 green, post-ship monitoring scaffold (PR-1h) committed.
- Phase 2 (if shipping): Tier 3 (§10 gate) PASS per all 4 kill criteria; `phase-2-measurement-results.md` written and committed regardless of outcome.
- Release notes describe migration commands (`calor fix --drop-structural-ids` and optionally `calor fix --compact-ids` or the `--upgrade-from 0.x` wrapper).
- CHANGELOG entry references RFC v5.

### 8.4 Per RFC closure (this RFC fully implemented)

- All 15 PRs (PR-0a..0e, PR-1a..1h, PR-2a..2f IF gate passed) merged.
- `docs/plans/phase-2-measurement-results.md` exists.
- Tier 4 (Phase 1 post-ship monitoring) green at day 30.
- v5 RFC marked "Implemented" in front matter.
- `docs/process/rfc-review-checklist.md` referenced from CLAUDE.md.

### 8.5 What "high confidence" means

For a coding agent shipping work on this RFC:

| Action | Required confidence level | How achieved |
|--------|--------------------------|--------------|
| Push a PR | Engineering safety | Tier 1 green + paste output |
| Merge a PR to a phase branch | Engineering safety + corpus invariance | Tier 1 + Tier 2 green |
| Merge a phase branch to `release/0.x+1` | Engineering safety + corpus invariance | Tier 2 green |
| Ship Phase 2 | Statistically validated agent benefit | Tier 3 PASS per RFC §10.3 |
| Mark RFC implemented | All of the above + 30-day production stability | Tier 4 green at day 30 |

These five rows are the operational definition of "high confidence" for this work. The agent does not invent new confidence levels and does not skip levels.

---

## Appendix A — Glossary of harness scripts (all new, committed in PR-0d or PR-1*)

| Script | Tier | Committed in | Inputs | Output |
|--------|------|--------------|--------|--------|
| `scripts/verify-phase1.sh` | 1 | PR-0d | `--corpus all` (optional) | exit 0 green, exit 1 red; prints summary |
| `scripts/verify-phase2.sh` | 1 | PR-2a | — | wraps verify-phase1.sh + Phase-2-specific checks |
| `scripts/byte-preservation-check.sh` | 1 | PR-0d | `<dir>` | per-file pass/fail report |
| `scripts/ast-roundtrip-check.sh` | 1 | PR-0d | `<dir>` | per-file pass/fail report |
| `scripts/token-delta-spot.sh` | 1 | PR-0d | `<file>` | Δtokens (positive = savings) |
| `scripts/verify-corpus.sh` | 2 | PR-0d | — | corpus-wide harness driver |
| `scripts/migrator-corpus-dryrun.sh` | 2 | PR-1c | — | dry-run migrator, byte-preservation check on every file |
| `scripts/token-delta-corpus.sh` | 2 | PR-0d | — | aggregate Δtokens for whole repo |
| `scripts/migrator-revert-roundtrip.sh` | 2 | PR-2f | — | round-trip migrate-then-revert byte-equality |
| `scripts/run-phase-2-gate.sh` | 3 | PR-0b | per pre-reg doc | drives 900-run gate experiment |
| `scripts/analyze-gate-results.py` | 3 | PR-0b | raw run logs | statistical analysis per §4.6 |
| `scripts/phase1-post-ship-monitor.sh` | 4 | PR-1h | — | rerun corpus, compare to baseline |
| `scripts/validate-task-template.sh` | helper | PR-0d | `<task-dir>` | validate task template per §5.3 |
| `scripts/verify-byte-preservation.sh` | helper | PR-1d | `<file> <log>` | single-file verifier |

## Appendix B — Calendar overlay on RFC §11

| RFC §11 week | This plan's activity |
|--------------|----------------------|
| Week 0 | PR-0a, PR-0b, PR-0c, PR-0d, PR-0e merge in parallel. PR-0e ships as a 0.x patch. |
| Week 1 | Phase 1 starts on `phase-1` branch. PR-1a, PR-1b in flight. Tier 1 green on every PR push. |
| Week 2 | PR-1c (migrator), PR-1d (verifier) in flight. End-of-week deadline for PR-1a per v5 §11 week-2.5 checkpoint. |
| Week 2.5 | Phase 1 dependency checkpoint: maintainer confirms PR-1a (parser change) merged. If not, Phase 2 slips. |
| Week 3 | Phase 1 finishing PRs (PR-1e, 1f, 1g, 1h). Tier 2 must be green on `phase-1` HEAD by end of week. Phase 2 starts on `phase-2` branch rebased on `phase-1`. |
| Week 4 | PR-2a..2f land on `phase-2`. Phase 2 merged to `release/0.x+1`. **Tier 3 gate runs (4–5 days).** |
| Week 5 | Contingency. Gate retry if RFC §10.5 triggered. Final analysis. Maintainer ship/no-ship decision per RFC §10.3. |
| Week 6 | Release prep. If gate passed: bundle Phase 1 + Phase 2. If gate failed: revert Phase 2 per §6.1, ship Phase 1 only. |
| Weeks 7–8 | Buffer; release cut. |
| Day 30 post-ship | Tier 4 runs (RFC §10.6). |
