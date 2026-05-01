# Devil's Advocate Review: Milestone 1 — Scaffold Ready

**Reviewer:** Copilot CLI (Claude Opus 4.6)
**Date:** 2026-05-01
**Audit method:** Read all 6 pre-registration docs, verified scaffold builds (0 warnings, 41/41 tests pass, 45 `.cs` files), inspected all 7 MESS instances, all 7 INV tests, all 10 state-transition tests, and the scoring rubric.

**Verdict:** The scaffold is genuinely well-built — clean architecture, real invariants, plausible mess. But the experimental design has methodological flaws that could produce a false positive for Calor regardless of whether Calor actually helps.

---

## What's Genuinely Good

- **Build is clean.** Verified: 0 warnings, 0 errors, `TreatWarningsAsErrors=true`, 41/41 tests pass in 580ms. The scaffold is functional, not just spec'd.
- **Invariants are real invariants.** INV-1 through INV-7 are cross-aggregate constraints that a naive edit could easily violate. They're not trivial checks.
- **State machines are testable.** The `StateTransitionTests.cs` covers both happy paths and illegal transitions, including the MESS-1 drift case.
- **Seeded mess is well-calibrated.** All 7 MESS instances are present in code with comments. They're realistic — not toy bugs, but the kind of inconsistencies that real codebases accumulate. MESS-5 even has a `#pragma` suppression, which is exactly how production code handles it.
- **The scoring rubric has anti-gaming clauses.** No spec leakage, separate grading, adversarial post-hocs. These prevent the most obvious confounds.

Now for the problems.

---

## 🔴 Fundamental Issues

### 1. The Line Count Claim Is Wrong

The milestone claims "2,160 lines of C#." Actual count (verified via `Measure-Object -Line` on all `.cs` files excluding `obj/bin`): **1,838 lines**. That's a 15% discrepancy. Minor in isolation, but this is a pre-registration document — accuracy of claims matters for credibility.

### 2. The Calor Port Is the Entire Experiment's Control Validity — And It Doesn't Exist Yet

The spec calls this "scaffold ✅ / dry-run pending / **Calor port pending**." But the Calor port is not just a to-do — it's the **most dangerous step** in the entire design:

- If the port is non-trivial (compiler chokes at 45-file scale, as Risk #3 acknowledges), you'll need to simplify the scaffold, which changes the C# baseline too.
- If the port introduces subtle semantic differences (different rounding behavior in Calor's emitted C#, different async state machine shapes, different exception propagation), you're comparing apples to slightly-different-apples.
- If the port requires adding contracts and effects, that **adds information to the Calor variant that the C# variant doesn't have**. The rubric measures whether models break invariants — but if Calor's contracts *tell* the model about those invariants explicitly, the comparison is biased toward Calor by construction.

**The fundamental confound:** The scaffold-spec says the Calor variant includes `§Q`, `§S`, and `§E{...}` declarations that the C# variant doesn't have. But those annotations *encode the invariants and coordination points* that the model is being tested on preserving. Giving one arm explicit machine-readable invariant documentation and not the other is not a fair test of "does the language help?" — it's a test of "does giving the model the answer help?"

This is the experiment's most critical design flaw, and it's not acknowledged anywhere in the milestone.

### 3. N=5 Is Underpowered for the Effect Sizes Being Claimed

The rubric's pass criterion is `mean(PrimaryMetric_calor) ≥ 1.50 × mean(PrimaryMetric_csharp)` at N=5. But at N=5, the confidence interval on a mean ratio is enormous. With high variance (which LLM-based code generation certainly has — one bad run can blow the mean), you need N=15–20 minimum to reliably detect a 50% improvement.

The cost-budget doc acknowledges this implicitly by budgeting $575 total. But the alternative — running N=10 on T1.B only (the prompt most likely to show a Calor advantage) — would give better statistical power at similar cost. The decision to spread thin across 3 prompts × 2 languages × N=5 prioritizes breadth over power.

### 4. The Primary Metric Formula Has a Division-by-Zero Adjacent Problem

```
PrimaryMetric = RawScore / dollar_cost
```

If a model completes T1.A correctly in 3 turns (say $4), the metric is ~0.25. If another run completes correctly in 1 turn via a lucky shot ($2), the metric is ~0.50 — 2× better, but the code quality might be worse. The metric rewards speed/cheapness, not quality of the edit.

More problematically: if the Calor model is slower (more turns due to MCP tool latency, longer prompts) but equally correct, it will **score worse** by this metric. The rubric's second clause ("raw correctness is non-inferior within 10%") partially guards against this, but the primary metric still rewards cheaper-and-faster, which biases toward the variant with less tooling overhead (C#).

---

## 🟡 Methodology Issues

### 5. The "Seeded Mess" Comments Are Visible to the Model

All MESS instances have inline comments like `// MESS-1: validator says yes, service says no.` and `// MESS-5: this method is marked async but does not await anything.` These comments are explanations of the mess — they're documentation for human readers of this research, but they'll also be visible to the coding agent.

If the agent reads these files (which it will), it now knows:
- There's a validator/service drift on cancellation (MESS-1)
- There are two rounding implementations (MESS-2)
- The `LegacyCustomerCode` is still in use despite `[Obsolete]` (MESS-7)

This makes the mess **significantly less messy** from the model's perspective. Real codebases don't label their inconsistencies. A fair test would either remove the MESS comments (making the mess truly hidden) or add equivalent documentation to both variants. As-is, the scaffold is easier than a real codebase, which weakens the ecological validity claim.

### 6. The In-Memory AppDbContext Creates a Simpler-Than-Real Testing Environment

The `AppDbContext` is thread-safe lists with a lock, not EF Core with a real database. This means:
- No migration concerns
- No transaction isolation issues
- No lazy-loading surprises
- No query-vs-LINQ behavioral differences
- Tests run in 580ms (no DB overhead)

While this simplifies the scaffold (good for experimental control), it also removes the class of bugs that real maintenance tasks encounter most often: DB schema migration conflicts, N+1 queries introduced by new features, transaction boundary issues. The scaffold's "maintenance task" is therefore easier than a real maintenance task, which limits generalizability.

For T1.B specifically (reservation expiry with a background worker), the in-memory store means there's no real concurrency to reason about — the lock serializes everything. A real system would have race conditions between the expiry worker and concurrent reservation/release calls. The test for concurrent access (adversarial post-hoc) can't truly test concurrency on this scaffold.

### 7. Feature-Acceptance Tests Don't Exist Yet

The milestone says graders are "Task #1" in "What's next." But the feature-acceptance tests are critical:
- They define what "correctness" means for scoring
- They must be written without seeing model output (to avoid bias)
- They need to work on both C# and Calor variants

The design says tests will be "tested through API surface, not bound to model's type names" — but the scaffold has no HTTP layer tests (the API controllers are present but there's no `TestServer` or `WebApplicationFactory`). All 41 current tests go through `TestFactory` directly to services. How will feature-acceptance tests work? Through the same `TestFactory`? Through HTTP? This is unspecified.

### 8. The Calor Variant's Test Strategy Is Unclear

The scaffold-spec says: "Same invariant tests (xUnit, written in C#, against the compiled Calor)." This means the Calor variant's tests are `.cs` files that import the `.g.cs` output. But:

- When the model modifies the Calor variant, does it modify `.calr` files or `.cs` test files?
- If it modifies `.calr` files, does the test project need to be rebuilt to pick up new `.g.cs`?
- If the model writes new tests, are those in C# or Calor?

These are workflow details that affect turn count (extra builds, extra compile-fix cycles) and therefore affect the primary metric.

---

## 🟠 Strategic Issues

### 9. The Pass Criterion Is Asymmetric and Favors Calor

The pass criterion: "Calor 'passes' if `mean(PrimaryMetric_calor) ≥ 1.50 × mean(PrimaryMetric_csharp)` for at least one of T1.A/B/C."

The failure criterion: "Calor underperforms C# by >20% on two or more prompts."

So Calor needs to be 50% better on ONE prompt to pass, but needs to be 20% worse on TWO prompts to fail. This asymmetry means:
- Calor can be 50% better on T1.B and 15% worse on T1.A and T1.C, and still "pass"
- The most likely Calor advantage (T1.B — effect system helps with explicit coordination) only needs to show up once
- The "mixed results" bucket (middle ground) leads to "diagnose" — which means more runs, not termination

A symmetric criterion would be: "Calor passes if mean across ALL THREE prompts is ≥ 1.2×; fails if < 0.8×." The current framing is designed to find a signal even if Calor only helps on one specific task type.

### 10. No Blinding

The scorer/grader knows which variant is Calor and which is C#. The adversarial post-hoc tests are written by the same person who designed the scaffold. The "code-quality grade" is subjective enough that unconscious bias toward the preferred outcome is possible.

Real experimental methodology would blind the grader: randomize which runs are shown without language labels, have a separate person write adversarial tests who doesn't know the hypothesis, or use purely automated scoring (which the rubric mostly does, to its credit, but the "code-quality grade" is partially subjective).

### 11. T1.A Is Likely a Null Result by Design

The prompts document itself predicts this:

> "T1.A — Baseline — does Calor help on a 'boring' change? [...] If Calor wins on T1.A but not T1.B/C, that's noise."

So why run N=5 × 2 languages on a prompt you expect to produce no signal? That's 10 runs ($100+) on a predicted null. Either skip T1.A (save budget) or run it N=2 as a sanity check, not N=5.

---

## 🟢 Minor Issues

### 12. The `TestFactory` Wires Everything Manually

The `TestFactory` constructor manually creates and wires all dependencies. This is fine for 14 services/repos, but when T1.A adds a new enum and T1.C adds shipment-line-item relationships, the model needs to update `TestFactory` too — a file not mentioned in the "files this should touch" lists. This might cause an extra turn or two for both variants equally, so it's probably not a confound — but it could frustrate the model and inflate turn counts.

### 13. No `Return` Transition Tests

The scaffold-spec's state machine shows `Shipped → Return → Returned (only within 30d)`, but no test exercises the Return transition. If a model's edit accidentally enables or breaks the Return path, no existing test catches it. This is a gap in the invariant tests that could allow silent regressions in both variants equally.

### 14. MESS-6 Is Tested But Not As a Regression Check

`OrderServiceTests.cs:45` documents MESS-6 (double vs decimal), but the test doesn't assert that `EstimateTotal` returns the `double`-precision result. If a model "fixes" MESS-6 by changing `double` to `decimal`, no test fails — which means the "seeded mess" isn't actually protected by the test suite. A model could clean up MESS-6 without penalty, which is fine, but it contradicts the spec's claim that these are "the noise that maintenance edits must navigate around without amplifying."

---

## Summary Table

| Concern | Severity | Recommendation |
|---------|----------|----------------|
| Calor variant has contracts/effects that encode the invariants being tested (information asymmetry) | 🔴 Critical | Either add equivalent doc-comments to C# variant, or acknowledge this is testing "does explicit annotation help?" not "does the language help?" |
| Line count claim wrong (2,160 vs actual 1,838) | 🔴 Minor-but-credibility | Fix before locking pre-registration |
| N=5 underpowered for 1.5× effect detection | 🟡 Medium | Either increase N or narrow to one prompt |
| MESS comments visible to model (not truly hidden mess) | 🟡 Medium | Remove MESS-N labels; keep the code inconsistencies but don't document them inline |
| Feature-acceptance tests (graders) don't exist yet | 🟡 Medium | Write before any dry run; their design affects what "correctness" means |
| In-memory DB removes real-world concurrency/migration challenges | 🟠 Low-Med | Acceptable for v1; note as threat to ecological validity |
| Pass criterion asymmetrically favors Calor | 🟡 Medium | Use symmetric criterion or justify asymmetry explicitly |
| T1.A predicted null — why spend N=5 on it? | 🟢 Low | Reduce to N=2 or acknowledge as calibration |
| Return transition untested | 🟢 Low | Add test or remove from state diagram |
| Primary metric rewards cheapness over correctness | 🟡 Medium | Consider using RawScore as primary, cost as secondary |

---

## Bottom Line

The scaffold is solid engineering — it builds, tests pass, the mess is realistic, the invariants are non-trivial. As a synthetic codebase for benchmarking coding agents, it's well above average.

But the experimental design has a **critical confound**: the Calor variant includes explicit contracts and effects that encode the very information the agents are being tested on preserving. This is like giving one group of students the answer key and then measuring whether they score better on the test. To make this a fair comparison, either:

1. **Strip contracts/effects from the Calor variant** (test the language, not the annotations), or
2. **Add equivalent structured comments or attributes to the C# variant** (e.g., `[Invariant("Total == Sum(LineItems)")]`), so both variants have the same information available, or
3. **Reframe the hypothesis honestly**: "Does explicit machine-verifiable annotation of invariants (which Calor provides and C# doesn't) help coding agents maintain code?" — which is a legitimate research question, just a different one than "does Calor help?"

Without addressing this, a positive result proves that "more information helps," not that "Calor helps."
