# Milestone 3 — Pivot from Calor port to annotation-regime pilot

**Date:** 2026-05-01
**Phase:** 0
**Status:** Calor port path **abandoned at low confidence**; pivoted to T1-prime (annotation-regime pilot on C#-only scaffold).

## Why pivot

Per `milestone-2-b4-finding.md`, the Calor port path was at **~55% confidence** because of three v0.5.0 emitter bugs (CS1729 record-struct ctors, CS8618 non-nullable property emission, CS8917 lambda type inference) that surfaced when auto-converting representative modern C#.

User directive on receiving milestone 2: *"if you have low confidence in a path, document it and move on to the next idea."* Saved as durable feedback. Applied here: documented in milestone 2; pivoting now instead of investing 4–8 hours in hand-augmentation that may also hit unanticipated walls.

## What's being abandoned

The Calor port path is **paused, not killed**. Specifically:

- `bench/research-phase-0/calor-baseline/` (the partial Calor port) is left in place as evidence; not extended.
- The three Calor compiler bugs (CS1729, CS8618, CS8917) are noted in `milestone-2-b4-finding.md` as candidates for separate Calor.Compiler maintenance, *not* Phase 0 work.
- Resuming the Calor port becomes contingent on either (i) those three bugs being fixed, or (ii) a different scaffold complexity / sample size that avoids the affected patterns.

## The new path — T1-prime: annotation regime on C# only

**Hypothesis (T1-prime):** Coding agents perform measurably better on multi-file C# maintenance tasks when the codebase has structured `// EFFECTS:`, `// PRECONDITION:`, `// POSTCONDITION:` annotations than when it doesn't, even without machine verification.

**Why this is the next idea, not a sub-step of the old idea:**

T1-prime answers a different question than T1. T1 asked: *"does Calor (annotations + compiler enforcement) help?"* T1-prime asks: *"does the annotation regime alone help, independent of any compiler enforcement?"*

If T1-prime shows a strong positive signal, *then* investing in the Calor compiler-enforcement layer is justified — the structural premise has empirical support. If T1-prime shows no signal, the foundational assumption is wrong: machine verification can't add value on top of structural advantages that don't exist. This decision-gates further Calor work cleanly.

**Why this is the highest-confidence next idea:**

| Property | T1-prime | T1 (Calor) | T2 (adversarial) | T6 (language form design) |
|----------|----------|------------|------------------|---------------------------|
| Reuses existing scaffold | ✅ | ⚠️ | partial | ❌ |
| Needs working Calor compiler | ❌ | ✅ blocked | ❌ | ❌ |
| Produces empirical signal | ✅ | ✅ | ✅ | ❌ |
| New tooling required | none | manual augmentation | new prompts + graders | none |
| Time to first run | hours | hours-days | days | n/a (design only) |
| **Confidence we can run it cleanly** | **~75%** | **~55%** | **~65%** | **n/a** |

T1-prime has the highest confidence-per-hour and tests the most foundational claim.

## Experimental design changes (vs rubric v2)

| Item | v2 (Calor vs C#) | v3 (T1-prime, annotated-C# vs bare-C#) |
|------|-------------------|----------------------------------------|
| Arm A | Calor variant | C# scaffold with `// EFFECTS:` / `// PRECONDITION:` / `// POSTCONDITION:` comments (current `csharp-baseline/` state) |
| Arm B | C# variant with comment-equivalents | C# scaffold with annotation comments stripped (new `csharp-bare/` variant) |
| Hypothesis | Machine-verified annotation regime helps | Annotation regime alone helps (without enforcement) |
| Decision interpretation | Calor passes → invest in confirmatory study | Annotated passes → invest in Calor compiler work + confirmatory study; bare wins or null → pivot to T2 |
| Blocking prerequisites | B1–B6 | B1, B2, B3, B5, B6 (B4 dropped — no Calor needed); plus new B7: bare-C# variant exists and tests pass |

The metric, aggregator, decision table, prompts, MESS coverage, and cost discipline are **unchanged** from rubric v2. T1.B remains primary.

## What this milestone produces

1. `scoring-rubric-v3.md` — locks the annotated-vs-bare experimental design.
2. `bench/research-phase-0/csharp-bare/` — copy of `csharp-baseline/` with annotation comments stripped. Same code, same tests, same MESS. Just no `// EFFECTS:` / `// PRECONDITION:` / `// POSTCONDITION:` lines.
3. Updated `README.md` and `methodology-changelog.md` reflecting v3 supersedes v2.
4. Updated tasks: B4 dropped from punch list; B5 (graders) and B6 (dry-run) unblocked.

## Risks

- **Risk: T1-prime is a strawman.** If "annotation regime" without enforcement doesn't help even though Calor's verified version would, we'd reject Calor unfairly. Mitigation: T1-prime is a *gate*, not a final verdict — a positive result authorizes Calor compiler work; a negative result is informative but not terminal (could still try T1 directly after compiler bugs are fixed).
- **Risk: my hand-written annotations are not representative of what a Calor-experienced author would write.** True; this is a limitation. The annotations were written based on what a faithful port to Calor's `§E{}/§Q/§S` would say. A more sophisticated annotation regime (with cross-references, machine-readable structure, etc.) might score differently. Documented as a known limitation.
- **Risk: same maintenance prompts (T1.A/B/C) might exercise annotated vs bare differently than Calor vs C#.** The prompts don't reference annotations or effects directly — they're about features. So the prompts probe whether annotations get read and used by the model. Whether they do is itself the experiment.

## Next concrete step

Write `scoring-rubric-v3.md`, create `csharp-bare/`, commit. Then unblock B5 (graders) and B6 (dry-run on T1.B).

## Updated confidence

- **~75%** that T1-prime produces a clean directional signal in this scaffold within Phase 0 budget.
- A negative result on T1-prime is itself a meaningful finding that reshapes the Calor program.
