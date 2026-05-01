# Research Phase 0 — Pre-Registration

This directory contains pre-registered artifacts for Phase 0 of the autonomous research program testing whether Calor materially outperforms C# on complex codebases when used by coding agents.

## Goal

Reach 95% confidence that Phase 0 is a well-designed **directional pilot** before any model run begins. Phase 0 is *not* powered to deliver a confirmatory pass/fail — it produces a signal that authorizes (or doesn't) a confirmatory study. After model runs start, the contents of this directory are frozen.

## Mandate (confirmed)

- **Budget**: $10,000 total program ceiling
- **Authority**: full control over Calor language structure (no v1 compatibility)
- **Pilot target**: identify whether T1.B-style multi-file maintenance shows ≥1.5× median Quality ratio for Calor (with non-inferior cost). Phase 0 outcome funds, or doesn't fund, a powered confirmatory study.
- **Project type**: synthetic large codebase first, then OSS port if signal emerges
- **Failure mode**: stop and report
- **Cadence**: milestone updates only
- **Model**: Opus 4.7 (`claude-opus-4-7`) — not Sonnet, despite Sonnet being cheaper

## File inventory

| File | Status | Purpose |
|------|--------|---------|
| `scaffold-spec.md` | locked | Domain, file list, invariants, seeded realistic mess |
| ~~`scoring-rubric.md`~~ | **superseded by v2** | (kept for audit trail) |
| **`scoring-rubric-v2.md`** | **authoritative** | Primary metric, pilot framing, decision table, blocking prerequisites |
| `methodology-changelog.md` | append-only | Why each rubric version supersedes the prior |
| `t1-maintenance-prompts.md` | locked | Exact prompts for T1.A / T1.B / T1.C (T1.B is the primary decision prompt under v2) |
| `cost-budget.md` | living | Token/$ estimates and stop-triggers |
| `milestone-1-scaffold-ready.md` | historical record | Status report at scaffold completion |
| `critique-milestone-1-scaffold-ready.md` | review record | Devil's-advocate review (Copilot CLI) |
| `critique-milestone-1-scaffold-ready-audit.md` | review record | Devil's-advocate review (source-audited) |
| `runs/` | append-only | Per-run logs (one folder per run) |
| `results/` | append-only | Aggregated results, post-hoc analysis |
| `graders/` | TBD (B5 prerequisite) | Feature-acceptance tests applied post-hoc |

## Pre-registration discipline

1. **Lock = committed**. A doc is "locked" once committed to git. Any change after first run requires a *new* file (e.g., `scoring-rubric-v3.md`) and an entry in `methodology-changelog.md`. Never edit the original.
2. **No peeking at the rubric mid-experiment**. Rubric scoring runs only after all N=5 × 3 prompts × 2 languages = 30 trials complete.
3. **Pre-commit invariant tests**. The test files used for "regression rate" are part of the scaffold and frozen with it. No new tests added after model runs begin.
4. **Honest cost accounting**. Every run logs its $ cost. If actual cost diverges >2× from the budget estimate, halt and re-plan.
5. **Six prerequisites (B1–B6)** in `scoring-rubric-v2.md` must be completed and committed before T1.B's first run. The rubric is locked-but-incomplete until B1–B6 are done.

## Why pre-registration matters here

The previous round of benchmarking produced a negative finding: Sonnet 4.6 with careful specs matches Calor on every small-codebase bug class. With the existential bar set at 50% improvement, the temptation to retrofit a positive interpretation is high. Pre-registration is the one mechanism that prevents this.

If Phase 0 produces no signal under these locked criteria, the system says so — and that becomes the milestone update.

## Pilot vs confirmatory

Under v2 of the rubric, Phase 0 is explicitly a **pilot**. With N=5 per cell, statistical power is too low to claim "Calor passed" at any specific effect size. The pilot's job is to identify which theory (T1.A, T1.B, T1.C, or none) shows enough directional signal to justify spending ~$1,500 on a powered confirmatory study at N=20.

This is a deliberate de-escalation from v1, which framed Phase 0 as confirmatory. v1's framing was a methodological mistake; v2 corrects it. See `methodology-changelog.md` for the full list of corrections.
