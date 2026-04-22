# Calor Direction

**Date:** 2026-04-21
**Status:** Direction commitment
**Supersedes informally:** the evidence-gated hedged portfolio in `docs/plans/calor-native-type-system-v2.md`

## The decision

Calor commits to the **safety** direction: correctness-by-construction as the organizing principle, with the type and effect systems doing the load-bearing work. Ergonomic features (inference into .NET type space, nullable elimination as a benchmark gate, generics-as-cosmetics) are **deferred**, not forever — just until the safety story is coherent.

This decision is made now because the v2 plan's hedged "run six hypotheses, see what sticks" portfolio is the wrong shape for a pre-1.0 language with no external users. With no shipped code to break, the cost of committing to a direction and being wrong is a rebase; the cost of committing to everything and measuring your way to a language is a plan that never produces a language.

## What "safety Calor" is

The through-line is: **bugs that the type/effect/contract systems can catch should be caught there, not at runtime and not by convention.** Calor already leans this way — effects, refinement types, contracts, obligations, and Z3 verification are the mature systems. Safety direction commits to making them load-bearing rather than optional.

Three features define the direction in the near term:

1. **Flow-sensitive Option/Result tracking (TIER1A in v2).** Unwrap-before-check becomes a compile error, not a `.unwrap` heuristic. Bounded implementation (new checker, no AST changes).

2. **Exhaustive match on Option/Result (TIER1C in v2).** Non-exhaustive matches on known sum types become errors. One-week ship.

3. **Effects as type-level row attributes (TIER2D in v2, but as a design commitment, not an evidence-gated hypothesis).** The centerpiece: unify the effect pass with the type checker so that function types carry effect rows (`Int !{db:w, throw}`), binding sites can type-check effect compatibility, and cross-module propagation is a type-system property rather than a separate pass. This is a 2–3 month commitment with compiler-architecture consequences; it requires a dedicated design doc (`docs/design/effect-rows-in-the-type-system.md`) before implementation starts.

Downstream, this direction points toward affine/linear types for resource safety (Tier 3 in v2, now a natural future step rather than speculative research).

## What ergonomic Calor would have been

For the record, the deferred half:

1. **Full local inference `§B{x := expr}`.** Including inference into .NET method returns. Requires importing .NET type metadata into the binder — the real bottleneck for most interop ergonomics. Deferred because doing this right is a multi-week binder project, and doing it partially (as v2's TIER1B does) is worse than not doing it at all.

2. **Generics with constraints, variance, higher-rank.** Moving generic type reasoning into the Calor binder rather than passing through to C#. Deferred because the type-system work for effect rows (TIER2D) is more foundational; generics can build on that algebra.

3. **Nullable elimination as a language commitment.** Disallowing `string?` in favor of `Option<string>`, with a migration tool. This is a breaking change dressed as a benchmark comparison in v2 — a design commitment either way. Deferred until the safety story has enough weight that the breaking change is justifiable.

These are deferred, not dropped. When safety reaches a plateau, we revisit.

## Why safety, not ergonomic

Four reasons, in order of weight:

1. **TIER2D (effects-in-the-type-system) is the one feature that would make Calor materially different from "C# with weird syntax."** It's the architectural commitment the v2 plan treated as a sibling of three other hypotheses. Either Calor makes this move or it doesn't; if it doesn't, the language doesn't have a story beyond what Roslyn already does. Committing to safety says: we're making this move.

2. **Calor's existing type/effect infrastructure is the mature part of the compiler.** Effects (per-file + cross-module + manifests + IL analysis), refinement types, contracts, obligations, Z3 — these are substantial and point the same direction. Building on them is cheaper than pivoting to ergonomic features that touch the binder, the emitter, and the .NET import path at once.

3. **Ergonomic features measured against current LLMs are a moving target.** Benchmark-Correctness / TokenEconomics / Comprehension are evaluated against the current model generation. A feature that improves them on 4.7 may regress on 5.x. Safety features measured by the compiler catching bugs are stable across model generations; correctness is not a fashion.

4. **Agents benefit from unambiguous code.** A language that makes common bug classes impossible produces fewer agent mistakes on the bugs it precludes. TokenEconomics is a losing metric for Calor (0.79x in v0.4.9) and not a winnable one against C# — C# is dense and terse by design. Comprehension, ErrorDetection, RefactoringStability are all winning for Calor and all improve further with safety infrastructure. Don't chase the losing metric.

## What this commits to

Concrete commitments:

- **TIER1A ships in ~1 week of engineering + ~1 day of classification**, without the v2 plan's governance pipeline. If new true positives exist and false positives are zero or explainable, it promotes. If false positives appear, we tune or revert. Hand-classification by Juan is the gate; no Wilcoxon, no bootstrap, no registry entry.
- **TIER1C ships in ~1 week** as an unconditional promotion. Option/Result are sum types in Calor's view of the world; exhaustive match is mandatory syntax per the design, not a hypothesis.
- **A TIER2D design doc** — `docs/design/effect-rows-in-the-type-system.md` — is written next. Before/after binder symbols, worked examples (intra-module, cross-module, generic effect polymorphism), EffectSummary cache migration, error-message samples at binding sites. Commit-to-implement is a decision on reading the doc, not a benchmark score. If the after-code reads better to Juan, it ships. If not, it doesn't.
- **TIER1B, TIER2E, TIER2F are not scheduled** in the safety direction. They move to a `docs/design/deferred.md` log with rationale for revisiting.

## What this does NOT commit to

- The v2 plan's Phase 0 governance pipeline, most of which is YAGNI at this stage: registry tamper-evidence, AB subcommand, micro-validation framework, pilot dogfood, statistician review. Some shipped code is kept (feature flags are genuinely useful regardless of direction), but the evidence-gating ceremony is mothballed until safety features have shipped and we have enough context to know whether it's worth building on.
- Pre-registration discipline for TIER1A / TIER1C / TIER2D. These are design decisions, not hypotheses. The governance overhead was designed for a research program; Calor at 0.3.5 is not yet that.
- Measurement as the arbiter of the language's shape. Measurement informs; the designer decides. The benchmarks are retained as a regression check, not as gates.

## How this changes v2

The v2 plan remains in the repo as history. For any reader, the operational source of truth is this direction document plus:

- The three design docs it commits to writing (TIER2D, deferred features, and whatever comes out of safety's downstream path).
- The handful of Phase 0 infrastructure pieces that earn their keep (feature flags, labeled corpus for bug-pattern checkers).
- Concrete PRs for TIER1A and TIER1C when they ship.

The v2 plan's governance machinery is not rolled back — it's installed and available when Calor has external users and shipped stakes worth protecting. Today, it's infrastructure in front of a decision, and the decision comes first.

## Timeline

- **This week:** TIER1A engineering (lean).
- **Next week:** TIER1A classification + ship; TIER1C engineering.
- **Week 3:** TIER1C ship; TIER2D design doc drafting starts.
- **Weeks 4–6:** TIER2D design doc iterates with critiques and examples. Implementation gate: does the worked-example code read better? Yes → implement. No → iterate or abandon.
- **Weeks 7–18 (if TIER2D implements):** TIER2D engineering. This is the actual language change.

Total: ~4 weeks to ship TIER1A + TIER1C + commit-to-TIER2D. Plus TIER2D implementation if approved. No hypothesis registry, no Stage 0/1/2 gates, no AB evaluator runs, no statistician review. Just: design, ship, judge, iterate.

This is the plan.

— Juan

## Postscript — TIER1A outcome (2026-04-22)

TIER1A was prototyped and reverted one day after this doc was written. **This is the direction doc's first testable prediction, and it failed.** Recording the result honestly; the direction is held in place pending the next test, not vindicated by this one.

**What shipped (on a branch, never merged):** `OptionResultFlowChecker` — flow-sensitive checker for unwrap-before-check, reassignment invalidation, and guard-return patterns. 11 unit tests passing. Gated behind `--experimental flow-option-tracking`.

**What the corpus said:**
- 436 in-repo `.calr` files → 1 finding in an intentional test fixture. 0 false positives. 0 new true positives beyond the existing suffix-based `NullDereferenceChecker`.
- 262,142 external `.calr` files (real C# projects converted to Calor) → 0 actual `.unwrap()` / `.expect()` / `.is_some()` method calls. The migration preserves C# idioms (`.Value`, `!`, `??`) verbatim.

**The revert:** checker, tests, diagnostic code, and experimental flag wiring removed. Working tree clean.

**What this updates:**

1. **The designer-judgment gate used by TIER1A failed.** "Does this catch a real bug class?" did not survive contact with the corpus. The same gate is what this doc proposes for TIER2D ("does the after-code read better?"). TIER2D therefore inherits TIER1A's gate-risk, at materially higher cost (4+ months of visitor-pattern tax, EffectSummary cache migration, test corpus breakage, LSP surface) and a harder-to-revert blast radius. Pre-committing to TIER2D on the same gate that just failed would be confirmation-bias infrastructure.

2. **TIER1A's failure mode is ambiguous between two readings**, which point in different directions:
   - (a) *Corpus is too shape-C# to validate safety features*, because the migration preserves C# idioms. → The design-gated approach to future features is the right response; benchmarks can't help.
   - (b) *Agents don't write idiomatic Calor Option/Result even when the language supports it.* → Nullable elimination (which this doc deferred as "ergonomic") might be a **precondition** for the safety direction having any validation surface at all, not an orthogonal feature. The safety direction may not survive without an ergonomic component that forces idiomatic usage.

3. **The TIER1C reasoning in this doc was right for the wrong reason at first, and needs re-stating honestly.** TIER1C is a **design-rule commit**, not a bug-catching hypothesis: "exhaustive match on known sum types is mandatory syntax." Low corpus presence is the argument *for* shipping it (it's a forcing function for how code is written, and its first firings are the usage signal we currently lack), not against. It ships. Honest pricing: ~1 week of checker work plus whatever the sample/conversion-snapshot/roundtrip-harness breakage costs.

4. **Calor is already materially different from C# without TIER2D**: explicit `§E` effects, cross-module enforcement, refinement types with Z3, contract enforcement, obligation tracking. TIER2D's contribution is architectural elegance (unifying effects into the type algebra), not a new user-facing capability. That reframes TIER2D from "the feature that defines the language" to "the architectural refactor that cleans up a system that already works" — which changes the cost-benefit materially.

**Updated near-term ordering** (supersedes the Timeline section above):

- **This week:** ship TIER1C as a design-rule commit. Honest pricing includes sample/conversion/roundtrip updates, not just the checker.
- **Before committing to TIER2D,** we need one of: (a) a 2–3 week emitter-output spike run in parallel with the design doc — evaluated on actual before/after compiler output on 1–2 non-trivial modules, not on prose examples written by the designer; (b) a serious evaluation of **.NET binder work** (resolve .NET method signatures in Calor's binder) as the competing higher-leverage investment. Binder work unblocks TIER1B (type inference), gives TIER2D effects from .NET calls, makes TIER2F (Option<T> vs T?) possible, and directly attacks the "shape-C#" corpus problem that just killed TIER1A. If the binder path is higher-leverage, TIER2D waits.
- **The "worked-examples-read-better" gate is insufficient as a single-reviewer aesthetic judgment on a 4+ month commitment.** If TIER2D proceeds, it proceeds with an external critique cycle, an emitter spike producing actual compiler output to critique (not prose), and honestly priced blast radius in the design doc itself.

The direction doc's safety bet holds. The specific roadmap above (TIER1A this week → TIER1C next → TIER2D design doc) is re-opened.

— Juan
