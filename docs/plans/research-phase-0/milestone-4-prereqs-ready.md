# Milestone 4 — Phase 0 Prerequisites (almost) Complete

**Date:** 2026-05-01
**Phase:** 0
**Status (under v3):** B1 ✅ / B2 ✅ / B3 ✅ / ~~B4~~ dropped / B5 ✅ / **B6 pending user dry-run** / B7 ✅

## What's done since the pivot (Milestone 3)

- **B7 (csharp-bare arm):** copy of `csharp-baseline` with annotation comments stripped. 0 warnings, 42/42 passing. Committed at `d94d82d`.
- **B5 (graders):** acceptance test files for T1.A, T1.B, T1.C. T1.B is the primary, with reflection + hosted-service + HTTP probing fallback chain to find the model's expiry trigger. T1.A and T1.C use straight `WebApplicationFactory<Program>` + JSON.
- **Dry-run protocol** at `bench/research-phase-0/dry-run-protocol.md`: explicit recipe for running B6 in a fresh Claude Code session.

## Commit graph since the program started

```
d94d82d  research: pivot to v3 (T1-prime annotation regime), add csharp-bare arm
58b921e  docs: milestone 2 — Calor port hits emission bugs at scaffold scale
3b423c1  bench(research): add Phase 0 C# scaffold with B1-B3 prerequisites applied
6c5db3b  docs: pre-register Phase 0 framework with rubric v2
```

(Plus this commit: B5 graders + dry-run protocol + this milestone.)

## State of all six prerequisites

| | Prerequisite | Status |
|--|--------------|--------|
| B1 | C# annotations on public methods | ✅ committed at `3b423c1` |
| B2 | MESS labels stripped | ✅ committed at `3b423c1` |
| B3 | MESS coverage table; SerializationTests added | ✅ committed at `3b423c1` |
| ~~B4~~ | ~~Calor compiler scale validation~~ | dropped — see milestone-2 |
| B5 | Graders for T1.A / T1.B / T1.C | ✅ committed at this milestone |
| B6 | Dry-run T1.B on Opus 4.7, annotated arm | ⏸️ **needs user to run a fresh CC session** |
| B7 | csharp-bare variant exists, builds, tests pass | ✅ committed at `d94d82d` |

## Why B6 needs the user

I can spawn an Opus sub-agent for the dry-run, but three problems make that ecologically invalid:

1. **Cost accounting bleeds.** Sub-agent tokens add to *this* conversation's running cost; the dry-run number gets contaminated.
2. **System prompt differs** between sub-agent and vanilla Claude Code; the cost envelope I measure isn't the envelope the real trials will hit.
3. **Context contamination** from a 30-turn coding session degrades my subsequent reasoning on the rest of the program.

The autonomy memo explicitly carves out cases like this: when I genuinely cannot do the step myself without invalidating its purpose, surface it.

## Confidence

**~75%** that Phase 0 produces a clean directional signal once B6 is done and the N=5×3×2 trials run. Pretty good.

The 25% residual:
- The grader-probing chain in T1.B might fail for unanticipated reasons (e.g., the model implements expiry as a `Task.Run`-style fire-and-forget that none of my probes can discover). If it does, the operator falls back to `correctness=0` per pre-committed rule, but that means we lose data on what the model *did* implement.
- One run could blow $25 (cost cap). That doesn't kill the experiment, but it forces re-budgeting.
- T1-prime might find no signal at all. That itself is a meaningful finding — annotation alone doesn't help — but it changes the program direction.

## What I'm doing while waiting on B6

The right answer is **nothing further on Phase 0** until B6 numbers come back. Adding more work before the cost envelope is validated risks throwing more time after a path that may need re-shaping based on cost.

If the user wants to use this gap productively, candidates:

- **Calor compiler bug-fix** for the three v0.5.0 emission bugs (CS1729, CS8618, CS8917). Those benefit Calor regardless of the research program. Filed in milestone-2.
- **Pre-emptive T2 scaffold work.** If T1-prime fails, T2 (adversarial-edge-case detection) is the natural next theory; sketching its scaffold now would save days when we need it.

I'll await direction or the B6 numbers. If the user is offline for a while, I will *not* spin up the dry-run myself — that's the trap the autonomy memo warns about (taking destructive shortcuts when blocked).

## Hand-off summary for fresh CC sessions

If you're picking this up cold, read in this order:

1. `docs/plans/research-phase-0/README.md` — what the program is and where v3 lives
2. `docs/plans/research-phase-0/scoring-rubric-v3.md` — what we're measuring
3. `docs/plans/research-phase-0/methodology-changelog.md` — why v3 supersedes v2 supersedes v1
4. `docs/plans/research-phase-0/milestone-2-b4-finding.md` and `milestone-3-pivot.md` — why we pivoted
5. `bench/research-phase-0/dry-run-protocol.md` — how to run B6
6. This file — current state
