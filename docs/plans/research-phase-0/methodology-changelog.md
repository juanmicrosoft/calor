# Methodology Changelog

Records why each version of the rubric supersedes the prior one. Append-only.

## v2 supersedes v1

**Date:** 2026-05-01
**Trigger:** two devil's-advocate critiques (`critique-milestone-1-scaffold-ready.md`, `critique-milestone-1-scaffold-ready-audit.md`) of the milestone declaring scaffold ready under v1.
**Authority:** user instruction "rework the rubric (item #6) first and commit a more coherent v2."

### Defects in v1 that v2 corrects

| v1 defect | v2 correction | Why it mattered |
|-----------|---------------|-----------------|
| `mean(Quality / dollar_cost)` as primary aggregator | Median + IQR primary; geometric mean / trimmed mean secondary; mean reported but not decisive | Means of ratios are biased toward denominator outliers; one cheap accidental run skews the cell |
| Disjunctive pass criterion across T1.A/B/C inflated FPR to ~14% | T1.B pre-specified as the **only** decision-relevant prompt; T1.A/C reported but descriptive | A 5% per-prompt FPR over three disjunctive prompts gave the experiment a coin-flip's worth of false-positive risk |
| Pass criterion 50%-better-on-one vs 20%-worse-on-two was asymmetric | Symmetric decision table on a single primary prompt | Made it easier for Calor to "win" than to "fail" — biased experiment design |
| Quality and cost conflated into one ratio | Quality and CostEfficiency reported and decided separately | A win on cost-only would have masqueraded as language quality |
| Confirmatory framing ("Calor passes if 1.5×") at N=5 | Explicit pilot framing; decision authorizes confirmatory budget, not "pass" | At N=5, statistical power is too low for any pass/fail claim. v1 was confirmatory in language but underpowered in execution |
| No prerequisite on closing the "Calor variant has annotations C# variant lacks" confound | **B1** prerequisite: C# variant must have comment-equivalents to all Calor `§E{}`/`§Q`/`§S` declarations before runs | Without this, a positive Calor result proves "more information helps" not "Calor helps" — uninterpretable |
| MESS-N labels visible to the model in source comments | **B2** prerequisite: strip inline `// MESS-N:` labels | Hand-holds the agent on what to be careful about; doesn't simulate real codebase entropy |
| Some MESS items had no detection tests (3, 4, 7) | **B3** prerequisite: each retained MESS-N must have a detection test, else dropped | Untested mess can be silently "improved" with no scoring consequence; hides regressions |
| Calor compiler scale unvalidated, treated as one of four equal risks | **B4** prerequisite: port `OrderService.cs` to Calor as a binary go/no-go gate before locking pre-registration | Calor failing at 45-file scale kills the experiment; not equal-weight with "models find T1.A trivial" |
| Graders not yet written, but rubric claimed lockable | **B5** prerequisite: `graders/T1.B/` written and independently reviewed before runs | Rubric without graders is locked-but-unscorable |
| No dry-run cost validation before committing to N=5 budget | **B6** prerequisite: one T1.B dry-run on Opus 4.7, halt-and-replan if > $25 | Per-run cap was estimated, not measured |
| Subjective 0–3 code-quality grade | Removed entirely | Subjective + unblinded = bias vector. Objective metrics suffice |
| Adversarial post-hoc tests altered `correctness` | Adversarial tests retained as diagnostic, no longer alter the decision-table input | They were doing two jobs (input to score, narrative for milestone). Separated |

### What v2 keeps from v1

- Hypothesis: Calor's machine-verifiable annotation regime helps on multi-file coordination tasks
- Three-prompt structure (T1.A, T1.B, T1.C); T1.B is now privileged but A/C still run for descriptive value
- Per-run hard cap $25; Phase 0 cap $1,000; program caps $5K (no-signal stop) and $9K (regardless stop)
- Anti-gaming clauses #1, #2, #3 from v1 (no spec leakage; tests not in prompt; tautological postconditions reclassified)
- Run logging schema
- Pre-registration discipline ("locked = committed; new versions only with new file name")

### What v2 does not address

These are explicitly out of scope and acknowledged as unresolved:

- Real EF Core vs in-memory `AppDbContext` (ecological validity)
- Operator discretion on cap enforcement (manual procedure, not automated harness)
- Lack of full grader blinding (mitigated by run-folder anonymization, not eliminated)
- 1M-context tier premium pricing (will be measured by B6 dry-run)

These were not blocking because they affect generalizability or cost variance, not the central claim. Documented as known limitations in `scoring-rubric-v2.md` § "Open items."

### Migration path

- `scoring-rubric.md` (v1) **superseded** but retained for audit trail. Do not edit.
- `scoring-rubric-v2.md` (v2) is the **authoritative** rubric until further notice.
- `README.md` updated to point readers to v2.
- `t1-maintenance-prompts.md` (v1) **retained as authoritative** for prompts. v2 introduces no prompt changes.
- `cost-budget.md` (v1) **retained as authoritative** for budget envelope. v2 introduces no budget changes.
- `scaffold-spec.md` (v1) — its description of the scaffold matches what was built. v2 adds the prerequisite that the scaffold be **modified** before runs (B1, B2, B3) but does not invalidate the spec itself. A `scaffold-spec-v2.md` is not necessary; the modifications are tracked in this changelog and applied during the prerequisite work.

## Lock criteria for the program

The pre-registration framework is "locked" iff:

1. v2 rubric is committed
2. README references v2 as authoritative
3. This changelog explains why
4. The six prerequisites (B1–B6) are completed and committed before T1.B's first run

Items #1–#3 happen at the v2 commit. Item #4 happens incrementally as the punch list is worked, with each prerequisite getting its own commit pointing back to this changelog.
