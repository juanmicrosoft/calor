# TIER1A Post-Mortem

**Date:** 2026-04-22
**Feature:** TIER1A — flow-sensitive Option/Result tracking (flow checker for unwrap-before-check, reassignment invalidation, and guard-return patterns)
**Outcome:** Built on a branch, never committed, reverted one day after the direction doc that committed to shipping it.
**Purpose of this document:** record the facts of the attempt, audit the causal story behind the revert, and preserve an empirical result that updates the downstream plan. Deliberately does not prescribe next steps beyond the one experiment needed to disambiguate remaining diagnoses.

**Status:** draft; four critiques integrated (EM, biostats, round-2, designer). Diagnosis (d) added as a fourth candidate and tested empirically (§4d); its rejection updates what follows.

---

## 1. What was built

Implemented 2026-04-22 in the working tree of branch `feat/design/calor-direction`. **Never staged, never committed, never pushed.** `git fsck --unreachable` after the revert shows only the direction doc blob; the checker's source is not recoverable via Git.

- **`OptionResultFlowChecker`** (~340 lines) — `IBugPatternChecker` implementing a forward dataflow walk over `BoundFunction`. Tracked per-variable `VarState` (`Unchecked` / `Checked`), with guard-return detection (`if (x.is_none()) return;` makes `x` checked on fallthrough) and reassignment invalidation.
- **`DiagnosticCode.UnsafeUnwrapFlow = "Calor0929"`** — new diagnostic code distinct from the existing `UnsafeUnwrap` (`Calor0925`).
- **Experimental flag wiring** — opt-in via `calor --analyze --experimental flow-option-tracking`. Wired through `BugPatternOptions.CheckOptionResultFlow` and `CreateCheckers()`.
- **11 unit tests** — positive cases (unwrap without check fires), negative cases (guard-return suppresses, `unwrap_or` is safe), new-behavior cases (reassignment invalidation, guard-return fallthrough, outside-branch).
- **Corpus scan harness** — enumerates `.calr` files, parses/binds/checks, reports via `ITestOutputHelper`. Two variants: in-tree and external.

All 11 tests passed. Feature was gated, correct within its own tests, and had zero impact on the default-off build.

## 2. What the corpus scans returned

**In-repo (436 `.calr` files):** 1 finding.
- `tests/TestData/Benchmarks/ErrorDetection/NullDeref_buggy.calr:5` — `.unwrap` on unchecked `Option<i32>`. Identifiable as a bug fixture by **filename only** (the file contains no `§DOC{BUG:...}` marker; the previous draft of this post-mortem overstated).
- **False positives:** 0.
- **Incremental TPs** — sites the existing `NullDereferenceChecker` *misses*: **0**. The same site is caught by the suffix-based checker.
- **Sites exercising the three new behaviors** (guard-return fallthrough, reassignment invalidation, outside-branch): **0**.

**External (~262k `.calr` files under `C:\Users\juanrivera\sources\repos\github`, all produced by the C# → Calor migration pipeline):** 0 actual `.unwrap` / `.expect` / `.is_some` / `.is_none` / `.is_ok` / `.is_err` method calls. Three textual matches on a pre-filter, all false matches (one field named `expect`, two string literals `"nullable.unwrap"` in IL opcode tables).

### 2.1 Reframing the "0 new TPs" number (EM critique §1 / designer §1)

"0 new TPs" is not a measurement of TIER1A's value. It is the consequence of a null experiment: **the corpus contained no material that would exercise the checker's new behaviors**, so the measurement was null by construction. The one in-repo finding is a naive `unwrap`-without-check, redundant with the existing checker. Nothing exercised guard-return, reassignment invalidation, or outside-branch logic. TIER1A's incremental value over `NullDereferenceChecker` is those three behaviors; the experiment tested none of them.

This matters because the revert was called on the "feature tested, didn't pay off" framing. The honest framing is: *the experiment didn't test TIER1A's surface.* That is a weaker basis for revert and points the diagnosis elsewhere.

### 2.2 The shape-C# problem is a migration-pipeline property (designer §2)

The external scan returning 0 `.unwrap` calls across 262k files is not a corpus-selection accident — it is a **structural property of `RoslynSyntaxVisitor`**, which preserves C# idioms verbatim (nullable refs, `try/catch`, `.Value`, `??`, `?.`) rather than translating them to Calor's Option/Result/match constructs. Every future corpus built from the migration pipeline will have ~0 Option/Result usage regardless of how much C# source is fed through.

This is distinct from "the corpus happens to be biased toward C# style." It is a **language-design choice** in the migration pipeline, treated by the direction doc as a neutral conversion layer when it is in fact the main determinant of what Calor-corpus evidence looks like. Every safety feature that validates against migrated code will hit this wall until either (i) the pipeline is changed to translate idioms, (ii) a shape-Calor corpus is generated via LLM, or (iii) the feature is validated by something other than corpus scans.

## 3. Why the revert was called

### 3.1 The chain of decisions

Stated reason in `docs/design/calor-direction.md` postscript: *"the direction doc's first testable prediction, and it failed."* The concrete chain:

1. The direction doc committed to shipping TIER1A under the rule *"if new true positives exist and false positives are zero or explainable, promote. If false positives appear, we tune or revert."*
2. The corpus scan produced 0 new true positives and 0 false positives.
3. The implementer's (Claude, in the previous session) initial recommendation was to keep the checker behind the experimental flag. Juan pushed back, accepted that framing, and called the revert. All code deleted in one session.

### 3.2 The revert rule had a gap (EM critique §2)

The rule had two branches: *promote* (new TPs exist) and *revert* (FPs appeared). The observed state (0 TPs, 0 FPs) matches neither. The rule silently defaulted to revert — but that was not the rule's explicit text. The previous draft of this post-mortem claimed the rule was "correctly applied"; that was papering over the gap. The rule was **under-specified for the 0/0 case**, and the default chosen (revert) forfeited reusable infrastructure (experimental flag wiring, diagnostic code reservation, 11 unit tests, corpus scan harness) that §6's follow-up test would have needed.

The v2 plan's three-state lifecycle (promote/hold/drop) was designed for exactly this case: 0 TPs with 0 FPs on a corpus that doesn't exercise the feature = **HOLD**, not DROP. The direction doc collapsed that lifecycle to binary (ship/revert) and inherited the ambiguity.

### 3.3 Weighing keep-gated vs revert (biostats D2)

"Keep gated behind experimental flag" was a defensible alternative:

- 0 FPs, passing tests, no user-facing impact (the flag is default-off).
- The corpus problem was already visible (sparse `.unwrap` sites), suggesting the test would re-run meaningfully only after a better corpus existed.
- Keeping the infrastructure parked costs nothing and makes the re-test cheap when the corpus question is answered.

"Revert immediately" has the advantage of not carrying unproven code. But given the 0/0 result, not carrying it was less valuable than preserving the means to re-test it. In retrospect, keep-gated was the better call. The post-mortem records this as an asymmetry in the decision, not as a claim about who was wrong — the implementer's initial recommendation was defensible; the human-override was defensible on different grounds; neither was obviously correct. Recording both sides here rather than treating the revert as default-right.

## 4. Candidate diagnoses

Four candidates. Each gets evidence-for / evidence-against / status. The diagnoses are **not mutually exclusive** (biostats C1, critique #3): (a) and (c) can coexist, (b) is independent, (d) — tested below — excludes most of its own support if rejected.

### (a) The migration pipeline — not corpus selection — is the cause of shape-C# input

Originally phrased as "the corpus is shape-C#," sharpened by §2.2 above to **"the migration pipeline preserves C# idioms by design."**

**Evidence for:**
- External corpus: 0 `.unwrap` across 262k migrated files.
- Sample migrated file confirms: nullable access via `(?. Activity.Current "Id")`, never via Option.
- `RoslynSyntaxVisitor` has no Option/Result translation path for C# nullable patterns.

**Evidence against:**
- We didn't test whether an idiomatic-Calor corpus, if it existed, would exercise the checker's three new behaviors at a rate that justifies the feature. Diagnosis (a) claims the experiment was null; it doesn't claim the feature would have been valuable with better input.

**Status:** confirmed as the proximate cause of the null result. The deeper question — whether TIER1A has value on a shape-Calor corpus — remains open.

### (b) The implementation was the problem

**Claim:** defects in the checker (FPs, perf, diagnostic quality) made it un-shippable independent of corpus.

**Evidence for:** none in the repo.

**Evidence against:** 11 unit tests passed, including the three new-behavior cases. No FPs in the scan. Performance was not the blocker (the external scan was cancelled after text pre-filter confirmed 0 `.unwrap` calls, not for time).

**Status:** no support. Cannot be fully ruled out until re-run on a shape-Calor corpus.

### (c) TIER1A's incremental value over `NullDereferenceChecker` is marginal

**Claim:** the feature was picked because it was cheap to ship, not because the three new behaviors matter in practice. Even on a shape-Calor corpus, it would catch too little to justify maintenance.

**Evidence for:**
- The one in-repo TP is redundant with the existing checker.
- TIER1A's original value claim was "beyond the existing `.unwrap` heuristic"; we have no corpus data on how often guard-return / reassignment / outside-branch patterns actually harbor bugs.

**Evidence against (revised — the previous draft's argument was circular, designer §3):**
- The direction doc committed to TIER1A pre-revert; but that commitment is itself what the post-mortem is auditing, not evidence against the diagnosis.
- We have no concrete prior work demonstrating these patterns are common in agent-generated Calor. Absent such evidence, "evidence against (c)" is honestly **none**.

**Status:** plausible. Separable from (a) only by re-running on a shape-Calor corpus with incremental-TP measurement (TPs beyond `NullDereferenceChecker`, not absolute TPs — biostats A4, critique #3).

### (d) Calor doesn't force Option/Result; agents default to C#-shape idioms

**Claim:** Calor accepts nullable refs (`T?`), `??`, `!`, throw. Agents writing Calor default to C# idioms because the language allows it. If true, the shape-C# problem is not a pipeline artifact but a **language-surface artifact**, and TIER2F (nullable elimination) becomes a precondition for any safety feature validating against Option/Result patterns.

**Evidence for (prior to test):**
- In-repo corpus: 8 of 436 files (1.8%) mention Option/Result at all, most in test fixtures.
- 262k external = 0% usage.

**Evidence against (empirical test, §4d.1 below):** 9 of 9 agent runs produced idiomatic Option/Result output under both doc-reading and no-doc-reading conditions.

**Status:** **rejected at N=9 for the Claude model family under these task conditions.** See §4d.1 for the test and caveats.

### §4d.1 Diagnosis (d) test — method and results

**Method:** three Calor tasks involving nullable data (parse `key=value`, lookup user by ID, safe divide). Two conditions:

- **Condition 1 — "with docs"** (N=6, 2 runs per task): Agent instructed to read `CLAUDE.md`, `docs/syntax.md`, and a few `samples/*.calr` files, then implement idiomatically. No mention of Option / Result / nullable / null in the prompt.
- **Condition 2 — "no docs"** (N=3, 1 run per task): Agent given a minimal syntax primer inline and **explicitly informed that both nullable refs and Option/Result are available**, with instruction to pick whatever is idiomatic. Instructed not to read other repo files.

Outputs stored under `tmp/test_d_task_{a,b,c}_{run1,run2,control1}.calr` (gitignored).

**Results:**

| Task | Run 1 (with docs) | Run 2 (with docs) | Control (no docs) |
|---|---|---|---|
| A — ParseKeyValue | `[str]!str` Result | `[str]!str` Result | `Result<(str, str), str>` |
| B — LookupUser | `?str` Option | `?str` Option | `Option<str>` |
| C — SafeDivide | `i32!str` Result | `i32!str` Result | `i32!str` Result |

**9/9 used Option/Result. 0/9 used nullable refs, `??`, `!`, or throw.**

Agents also consistently made the right semantic distinction — Result for genuine errors (parse failure, division undefined), Option for non-error absence (user not found) — across both conditions.

**Caveats (A3 biostats, B2 anti-circularity):**

- Single model family (Claude via Agent tool). Does not generalize to GPT, Gemini, etc.
- Tasks were explicitly nullability-heavy. Mixed-concern tasks (where the agent chooses whether nullability comes up at all) were not tested.
- "With docs" condition may have been primed by Option/Result usage in the samples the agent read. The no-docs control, which explicitly surfaced both options, is the cleaner test; it still returned 3/3 idiomatic.
- No mechanical audit tool was run against the generated `.calr`; files were hand-read by the implementer (who is the same party that designed the tasks — biostats B3). Outputs were unambiguous enough that adjudication bias is unlikely, but the post-mortem records the limitation.

**Interpretation:** Under test conditions, the Claude model family spontaneously produces idiomatic Calor when asked to write Calor code with nullable data and given even a minimal primer naming the available options. Diagnosis (d) is rejected at this scale. Agents don't default to shape-C#; the **migration pipeline does.**

This inverts a claim the scoping doc critique made (Addendum A1): TIER2F is *not* a precondition for TIER1A on agent-preference grounds. The shape-C# problem is contained to migrated code, not agent-generated code. A shape-Calor corpus can be LLM-generated; the (d) test proves the method works.

## 5. What this resolves and does not resolve

**Resolves:**
- The facts of the build and revert.
- That the revert rule as written had a 0/0 gap, silently defaulted to revert, and a different default (hold) was defensible and probably better.
- That diagnosis (d) — agents default to C#-shape idioms — is rejected for the Claude model family at N=9.
- That the shape-C# corpus is a migration-pipeline property, not a selection accident or an agent preference.
- That (a), (b), (c) are distinguishable from (d), but not from each other without a further experiment.
- That future features should include a **pre-build corpus base-rate check** (biostats C3): "before building TIER1A, what fraction of target-corpus files contain any `.unwrap`?" is a 30-minute question that would have changed the build decision.

**Does not resolve:**
- Whether TIER1A has value on a shape-Calor corpus. Separating (a) from (c) requires that experiment (§6).
- Whether any in-flight follow-up work (the uncommitted scoping doc, the binder spike, TIER2D design) is pointed at the right problem. The scoping doc assumes (a) alone justifies binder work; (a) is now sharpened to "migration-pipeline property," which **binder work does not attack** — binder work makes the compiler see into C#-shaped code, it does not produce Calor-shaped code. That is a material gap the scoping doc inherits. See §7.
- Whether the three candidate diagnoses exhaust the space. At minimum, a fifth candidate exists (the revert rule was under-specified, §3.2) and is arguably the highest-leverage finding for process.

## 6. Minimal next test (rewritten)

Goal: disambiguate (a) from (c). Does TIER1A catch material beyond `NullDereferenceChecker` when run on a shape-Calor corpus?

### 6.1 Corpus generation

**Method:** LLM generation under strict system prompts. **The migration-pipeline option from the previous draft is dropped** (critique #2 / biostats B1): using migration output would re-measure the same property the 262k scan already established.

**Size:** 150 programs (biostats A3 — N=50 was noise-limited at the decision threshold).

**Prompt design (anti-circularity, biostats B2):**
- Full syntax reference inline: not assumed from training, since Calor-specific training data is thin.
- ≥ 5 example programs inline that exercise guard-return and reassignment patterns (so the model has patterns to vary over, not just types to use).
- Task-driven prompts that require Option/Result (prompt says *"returns possibly-absent data" / "returns data or error"*), not model-chosen.
- No mention of the checker or its three new behaviors.

**Mechanical "idiomatic" definition (biostats B3):** a program counts as shape-Calor iff it (i) uses at least one `Option<T>` or `Result<T,E>`, (ii) has at least one `.unwrap` / `.expect` / pattern-match-with-unwrap in a non-trivial context, (iii) contains no `.Value`, `!`, `??`, or `throw`. Non-idiomatic programs are discarded, not argued about.

**Countersigner (biostats C1):** programs are accepted only after a second reviewer (not the implementer) confirms adherence. One hour, non-blocking.

### 6.2 Measurement

Run **both** `NullDereferenceChecker` and a reconstructed `OptionResultFlowChecker` on the generated corpus. Report:

- Total findings per checker.
- **Incremental findings** — TIER1A findings that `NullDereferenceChecker` does not catch (biostats A4, critique #3). This is the quantity the original TIER1A gate required.
- False positive count and rate (denominator: total TIER1A findings).

### 6.3 Outcome matrix (biostats A1, critique #4)

| TIER1A incremental TPs | TIER1A FP rate | Interpretation |
|---|---|---|
| ≥ 10 | < 10% | (a) confirmed; (c) rejected. Shape-Calor material exercises the new behaviors at a useful rate. Rebuild and ship. |
| ≥ 10 | ≥ 10% | (a) + (b). Feature has material to catch but the implementation FPs too often. Rework, not rebuild. |
| 3–9 | any | **Inconclusive.** Corpus or checker needs redesign. Scale corpus to 300 and re-run, OR audit checker for missed categories. |
| 0–2 | 0 | (c) confirmed. Feature's incremental value is marginal even with ideal input. Do not rebuild. |
| 0–2 | > 0 | Incoherent — implementation produced FPs on a corpus it found no TPs on. Audit checker. |

"Inconclusive" is a valid outcome, named explicitly to prevent post-hoc narrative fitting.

### 6.4 Prerequisite

TIER1A's checker and 11 unit tests must be reconstructed before the test runs. The code is **not** in Git (§1). Reconstruction from the descriptions in this post-mortem plus the existing `NullDereferenceChecker` pattern is ~½ day of engineering. Not cheap, but not expensive either. Record this as a process lesson (§8).

## 7. Implications for in-flight work

### 7.1 The scoping doc (`docs/design/tier2d-vs-binder-scoping.md`)

Currently uncommitted on disk, draft status. Built on the premise that (a) — corpus shape-C# — is the diagnosis, and that binder work addresses the shape-C# problem.

**Two updates needed before it can proceed:**

1. **Sharpen (a) to "migration-pipeline property":** binder work gives the compiler visibility into C#-shaped code; it does not produce Calor-shaped code. The scoping doc's §C framing ("binder partially attacks the shape-C# problem") was closer to right than the main recommendation suggested, but "partially" is unquantified. A shape-Calor corpus (§6) is the more direct response; binder work is tangential to the corpus question.

2. **Correct the factual errors identified in the EM critique's Verification Addendum (V1-V5):**
   - V1: `BoundCallExpression` already has `ResolvedTypeName` / `ResolvedMethodName` / `ResolvedParameterTypes` fields. The binder spike is "complete partial resolution," not "add metadata from scratch."
   - V2: IL analysis (1,787 lines) is complementary, not redundant. "Manifest redundancy reduction" is wrong.
   - V3: `BoundExpression.TypeName` is a string, not `CalorType`. TIER2D spike is 3–5 days, not 1–2.
   - V4: Overload resolution (18 overloads on `Console.WriteLine`) is unmentioned.
   - V5: `Binder.cs` is 918 lines, not ~1060.

Until both updates land, the scoping doc's binder-spike-vs-TIER2D decision is premature. Neither spike should start.

### 7.2 The (d)-rejection result updates the plan order

With (d) rejected, TIER2F is **not** a precondition for safety features that validate against Calor code. It may still be a design-coherence decision (nullable refs in the language surface undermine Option's safety claims), but that is a separate argument — not one this post-mortem resolves.

### 7.3 §6 is the next experiment, not another architecture doc

The direction doc pulled TIER2D forward after TIER1A reverted. The scoping doc pulled binder work forward as an alternative. Both are architectural commitments made before the cheap test (§6) has run. §6 produces an answer cheaper than either spike and informs both — if (a)+(c) confirms, any architectural investment that builds on a shape-Calor corpus has a foundation; if (c) confirms alone, the safety direction's entire premise needs re-examining before either spike is worth running.

## 8. Process observations

Non-prescriptive. These are lessons the diagnosis supports without dictating action:

1. **Direction docs committing to ship a feature should confirm a validation corpus exists before the commit.** The direction doc's 4-week timeline for TIER1A/TIER1C implicitly assumed corpus sufficient to evaluate the features; the 24-hour revert shows the assumption was load-bearing and untested.

2. **Feature-value claims should cite at least one example from the actual target corpus, not only synthetic unit tests.** TIER1A passed 11 unit tests and failed on the first corpus scan. The unit tests were not evidence of real-world value; they were evidence of logical correctness.

3. **Revert rules must cover the 0/0 case explicitly.** "New TPs exist, promote" and "FPs appeared, revert" is a binary for a ternary outcome space. Add HOLD explicitly: *if neither TPs nor FPs appeared, hold until the corpus question is answered.*

4. **Reverts with a named follow-up test should retain the branch by default, not delete the code.** Reconstruction from description is more expensive than `git checkout`.

5. **Pre-build corpus base-rate checks cost 30 minutes and change build decisions.** Before committing to build TIER1A, "what fraction of corpus files contain `.unwrap`?" would have set expectations correctly.

6. **24-hour direction-to-revert cycles are information, but whose nature depends on context.** On a pre-1.0 solo project, fast revert is the correct response to null evidence. On a shipped product, it would indicate brittle decision-making. The post-mortem doesn't take a position on which applies — only that the cycle is fast and worth watching.

---

**Reviewers:** four critiques landed (EM, biostats, round-2, designer) and are integrated above. The (d) test was added as a result of the designer critique's missing-fourth-diagnosis observation. Any finalizer other than the implementer would close the single-reviewer gap EM §3 flags.

— *Draft by Claude, for Juan's review, 2026-04-22*
