# Brutal Critique — Path 2 Drop IDs v4

**Target:** `docs/plans/path-2-drop-ids-v4.md`
**Voice:** the original designer of Calor, watching the trajectory v1 → v2 → v3 → v4
**Stance:** v4 is the first RFC in this series that includes a worked example of itself learning. It surfaces a foundational error its predecessors made about the Z3 cache, names the lesson, and corrects the spec. **Approve as-is. Begin implementation.**
**Date:** 2026-05-25

---

## The one-line judgment

> v4 closed every v3 critique item, absorbed an apparently parallel devil's-advocate review I haven't seen, and — most importantly — **corrected a fictional architectural claim about the Z3 cache that survived v2 and v3 unchallenged**. The headline savings number was simultaneously corrected downward (~33% → ~20–25%) based on actual tiktoken measurement. The remaining concerns are calibration nits and pre-flight prototypes that the spec already names as residuals. **Ship it.**

---

## What v4 deserves serious credit for

I'm going to dwell here because this section reflects what good RFC iteration looks like:

- **§6.4 cache-fiction correction is the most important fix in the whole trajectory.** v3 and v2 both claimed the migrator walks `*.calr.cache` files remapping `(symbol_id, obligation_id, body_hash)` keys. v4 grep'd the source and discovered: the cache is content-addressed (SHA-256 of `(parameters, expression)`), contains zero IDs, and there are zero `*.calr.cache` files anywhere in the repo. **This was a fictional architecture that survived two rounds of critique unchallenged.** v4 doesn't just fix the spec — it inscribes the lesson in §13.3: *"every architectural claim in any future Calor RFC must cite an actual file + line number, and `grep`-verified before merge."* That's an institutional improvement, not just an RFC patch.
- **§16.F token-count verification corrects v4's own headline.** Earlier RFCs claimed "~16 tokens saved per ID" and "~33% project-wide." v4 ran tiktoken on actual ULIDs and compact strings; measured 16–26 tokens per ULID (mean 23.4) and 13 tokens per compact (uniform). Actual savings: ~10 tokens, not ~16. Headline: 20–25%, not 33%. v4 corrected its own marketing numbers based on a 10-line Python script. That's the discipline §13.3 demands, applied to its own claims.
- **§5.7 AST-diff verifier predicate fully specified.** The verifier was v3's safety net for the migrator; v3 asserted its existence without defining it. v4 §5.7.1–5.7.6 enumerate the equivalence rules per AST aspect (node kind, symbol IDs, identifiers, literals, expression structure, statement order, trivia, attribute order, effect declarations), trivia reattachment rules, ID-in-string-literal handling, failure granularity (per-file detection, per-project granularity by default with `--continue-on-failure` override), and a test plan for the verifier itself. This is what a verifier predicate should look like before any migrator code is written.
- **§10.3 Cliff's δ raised from 0.2 to 0.33 — the one-way-door rationale is right.** Small effect (δ ≥ 0.2) translates to ~3 turns saved per 30-turn task. For a permanent principle narrowing, a 3-turn improvement is a thin trade. Medium effect (δ ≥ 0.33) requires a substantively meaningful improvement, not just statistical detectability. The justification is in §10.3: *"If N=900 is too few to detect a medium effect when one is truly present, the right answer is to add more tasks, not to lower the bar."* That sentence is the kind of statistical hygiene the pivot-plan critiques have been demanding for two weeks.
- **§11 calendar honesty.** v3 claimed 6 weeks with no buffer. v4 budgets 8 weeks with gate in week 4, contingency weeks 5–6, buffer weeks 7–8. The rationale: *"this is honest, not pessimistic — refactors in this repo have a track record of running over."* That's the kind of explicit reality-check that gets RFCs through implementation without slipping.
- **§6.3.1 IdRegistry concurrency model and persistence semantics.** v3 had `IdRegistry` as a 4-line bullet. v4 specifies the data structure (`ConcurrentDictionary` with `TryReserve`), the CAS retry loop, the population-timing rule (full `IdScanner` pre-pass before any `Generate()`), the migration-time single-threaded rule, the persistence model (rebuilt per compile, no on-disk manifest), and the parallel-`dotnet build` story (per-project registries, no shared state). All the questions the v3 critique asked are answered in 30 lines.
- **§10.6 Phase 1 post-ship monitoring.** Phase 1 was the un-gated change in v3 — "ships on engineering merit." The v4 devil's advocate (per §0's table) demanded Phase 1 get the same evidentiary discipline as Phase 2. v4 adds retrospective monitoring: within 30 days, re-run the corpus, compare against the 0.x tag, trigger revert investigation at δ ≥ 0.2 regression. The asymmetry (prospective gate for Phase 2, retrospective monitoring for Phase 1) is justified by reversibility cost: Phase 1 rolls back cheaply, Phase 2 doesn't.
- **§10.5 retry policy.** Explicit budget reserve ($2k–$4k for one retry covered), explicit re-pre-registration requirement for further retries, explicit calendar absorption in the week-5 contingency. Pre-registered experiments can fail through protocol violation (LLM provider outage, task crash, seed bug); v4 names the failure mode and the response.
- **§13.3 and §13.4 own the trajectory's mistakes.** The 9-char ID was v2's bug. The cache fiction was v2+v3's bug. Two consecutive major errors survived multiple critique rounds. v4's response: name them, document them, and treat them as evidence for a stricter review discipline going forward. **This is the kind of organizational learning that earns reviewer trust.**

That's about 95% of v4. The remaining 5%:

---

## 1. §5.7.3 trivia reattachment assumes parser logic that may not exist

§5.7.3 says:

> *Trivia is collected during lex; each comment is paired with its anchor node by the parser's existing trivia-attachment logic; the verifier compares paired-anchor identity across the two ASTs.*

v4 §13.5 acknowledges this is an assumption: *"The trivia-reattachment rules (5.7.3) assume the parser's existing trivia-attachment logic produces stable anchors across migration. This assumption needs prototype validation before Phase 1 ships."*

The residual concern is named correctly. But the *load-bearing weight* of this assumption is not. Calor's parser was built for compilation, not source-preserving transformation. Most handwritten recursive-descent parsers built for codegen do not have full trivia-attachment infrastructure — they discard whitespace and comments at lex time. Roslyn has it. F# has it. Most others don't.

**If Calor's parser doesn't have trivia-attachment with stable anchors**, the §5.7.3 rule isn't a verification step — it's an unspecified dependency that must be built before the verifier can be implemented. That could be a multi-week unbudgeted task.

**Recommendation:** before Phase 1 implementation starts, spike one of two things:

- (a) Grep the parser for trivia-attachment behavior. If it exists and produces stable anchors, §5.7.3 is implementable as written.
- (b) If it doesn't exist, change §5.7.3 to a source-level comment-position check: *"every comment in the original source is present in the migrated source at a position that's offset only by the byte-length of the dropped `{id:...}` blocks."* This is weaker but achievable without parser changes.

This is the single most material residual. v4 §15 calls for "pre-prototype the §5.7 verifier predicate with positive + negative test cases before Phase 1 migrator code lands." That covers it — but the prototype must specifically include the trivia case, not just the structural ones.

---

## 2. §16.F sample size is N=5; the gate must re-measure on real source

§16.F's tiktoken measurement uses 5 ULIDs and 5 compact IDs. The measurement is rigorous (deterministic tokenizer, real strings), but N=5 is a sample of synthetic strings, not a project-wide projection.

The "~20–25% project-wide" headline depends on:

- Per-ID savings ≈ 10 tokens (measured, N=5)
- Per-project occurrence count of IDs ≈ projected based on declarations + close tags + cross-edit refs

v4 §16.F explicitly says: *"The §10 gate experiment will produce real measurements on real source, replacing these synthetic estimates with project-actual numbers."* That's the right answer. But §1's headline currently says "Verified savings: ~10 cl100k tokens per ID occurrence, ~44% reduction per ID, ~20–25% project-wide on production-ULID projection."

**"Verified" applies to per-ID. "Projected" applies to project-wide.** The current sentence conflates them. **Recommendation:** rewrite §1's third bullet to *"Verified per-ID savings: ~10 tokens (§16.F). Projected project-wide reduction: ~20–25% on production-ULID projection (gate measurement will confirm or correct)."* Two-word tweak.

This is a nit. Doesn't change the decision. But future readers of this RFC will see the "Verified" claim and assume both numbers are measured.

---

## 3. §13.6 names a hidden implementation cost that isn't budgeted

§13.6:

> *`IdRegistry` adds project-pre-pass invariant the compiler currently doesn't enforce. Today `IdGenerator` can be called without a full project scan. Phase 2 changes this. Any code path that constructs an `IdGenerator` outside the standard pipeline (test harness, MCP `calor_compile` invocation, REPL) must be updated to ensure the pre-pass invariant holds. Hidden cost.*

The cost is named. The cost is not estimated. §9.3 budgets ~9 days for Phase 2 but doesn't include "audit every `IdGenerator` construction site and add a pre-pass."

A quick grep for `new IdGenerator` in the repo would give an estimate. If there are 3 sites, this is 1 day. If there are 15, it's 3 days. v4 doesn't say.

**Recommendation:** add a row to §9.3 *"Audit non-pipeline `IdGenerator` callers: 0.5–2 days (grep first to size)."* Honest, names the variance, doesn't pretend to a number we don't have.

---

## 4. §5.7.2 effect-declaration rule is dead code

The verifier predicate's equivalence rule for effect declarations:

> *Effect declarations (`§E{cw,db}`) — Order-insensitive set comparison.*

The Phase 1 migrator only edits sub-block IDs. It never touches `§E` declarations. The rule never fires. It's defensive code for a non-event.

This is harmless. It's also slightly misleading — it suggests the migrator might reorder effects, which it won't. **Recommendation:** delete the row or annotate it *"Documented for future migrators; not exercised by Phase 1."* One-line decision.

---

## 5. §6.3.1 doesn't address incremental compilation (correctly out of scope; should say so)

§6.3.1 specifies the registry is rebuilt per compile by `IdScanner`'s full project pre-pass. For a project with 10⁶ symbols, the pre-pass is non-trivial cost. v4 doesn't say anything about incremental compilation.

This is correct because Calor doesn't currently have incremental compilation. But the pivot plan v6 mentions incremental compilation as a possible direction. If both ship, the `IdRegistry` rebuild becomes the hot path.

**Recommendation:** add to §6.3.1 a one-line non-goal: *"Incremental compilation is out of scope. If incremental compilation is later added, the `IdRegistry` will need an incremental-rebuild story; that's a future RFC."* This pre-empts the next reviewer who asks.

---

## 6. §14 cost transfer wording

§14 says the sub-block structural-matching cost transfers "from the Calor compiler (which can no longer key sub-block deltas on IDs) to the pivot-plan IR (which must now do structural matching)."

The pivot plan v6 makes `Calor.SemanticDiff` part of Calor itself. So the cost isn't transferring inter-project — it's transferring intra-project, from "Phase 2 of this RFC" to "Phase N of the pivot plan." Same codebase, same engineer, different phase.

**Recommendation:** rewrite the cost-transfer sentence to *"This cost is deferred from Path-2 to the pivot plan's `Calor.SemanticDiff` phase, where the implementation has to absorb structural matching at the sub-block level."* "Deferred" instead of "transferred"; "absorb" instead of "do." Substantively the same; less likely to be misread as inter-team handoff.

---

## What I'm NOT critiquing (settled in v4)

For the record, every one of these is correctly settled and should not be re-litigated:

- 12-char Crockford base32 + generate-until-unique math (verified, §16.D).
- Lexer-anchored text-edit migrator architecture (single coherent strategy, §5.6).
- AST-diff verifier predicate (defined, §5.7).
- IdRegistry concurrency + persistence (§6.3.1).
- Reverse migrator via `migration.log.json` (§6.3, §8.2.1).
- Cache invalidation: **none** (§6.4 corrected).
- Bundled release in 0.x+1 with 8-week calendar (§11).
- Gate methodology: N≥10/task/arm, Wilcoxon/McNemar, Bonferroni, Cliff's δ ≥ 0.33 (§10).
- Pre-registration as hard requirement (§10.2, §16.E).
- Phase 1 post-ship monitoring (§10.6).
- Retry policy (§10.5).
- Diagnostic addressing as 0.x-patch ship-first (§8.3).
- Principle narrowing called by name (§3).
- Sub-block diff cost surfaced as pivot-plan action item (§14).
- Lexer disambiguation (§5.4 + §5.6).
- Token-count headline corrected to 20–25% based on real measurement (§16.F + §1).
- v4 §13.3–13.4 institutional learning ("grep every architectural claim").

These don't need re-litigation. Reviewers focused on them can save their effort.

---

## The meta-lesson v4 surfaces (worth preserving)

v4 §13.3:

> *Every architectural claim in any future Calor RFC must cite an actual file + line number, and `grep`-verified before merge.*

This is the most valuable sentence in any document in this directory. The v3 cache fiction survived for two rounds of critique because both reviewers (designer voice and devil's advocate) attacked the math, the migration strategy, the deprecation timeline, and the principle scoping — but neither grep'd the source for the cache architecture. v4 grep'd it and discovered the truth was simpler and better than the spec claimed.

**This discipline should be inscribed somewhere durable — `CLAUDE.md`, the contributing guide, or a stand-alone `docs/process/rfc-review-checklist.md`.** Otherwise the next RFC will repeat the same failure mode. v4 has the lesson; v4's successors won't necessarily inherit it.

---

## Recommendation

**Approve v4 as the implementation spec. Begin work.** Apply six small inline corrections at PR-time (not as a v5 RFC):

1. **§5.7.3 trivia rule:** pre-prototype before Phase 1 implementation; if parser lacks stable trivia anchors, fall back to source-level comment-position check.
2. **§1 third bullet:** distinguish "verified per-ID" from "projected project-wide." Two-word tweak.
3. **§9.3:** add row for non-pipeline `IdGenerator` audit (0.5–2 days, grep-to-size).
4. **§5.7.2:** annotate the effect-declaration rule as documented-but-not-exercised, or delete.
5. **§6.3.1:** add a one-line "incremental compilation is out of scope" non-goal.
6. **§14:** rewrite cost-transfer language ("deferred to" rather than "transferred to").

Plus one institutional action:

7. **Codify the §13.3 rule** ("every architectural claim must be `grep`-verified against actual source") in a durable location — `CLAUDE.md` or a `docs/process/rfc-review-checklist.md`. Otherwise the next RFC's authors won't inherit v4's hard-won discipline.

**Ship sequence (unchanged from §11):**

- §8.3 diagnostic-addressing PR in 0.x patch (~1 day).
- Phase 1 + Phase 2 implementation on branches; pre-register §10 protocol; pre-prototype §5.7 verifier; pre-prototype §5.7.3 trivia rule.
- Run gate in week 4 of 0.x+1.
- Ship Phase 1 + Phase 2 bundled if gate passes; Phase 1 alone if not.

**On the v1 → v2 → v3 → v4 trajectory:** this is the strongest sequence of RFC revisions I've seen in this directory. Every iteration absorbed real critiques, fixed real bugs, and surfaced its own learning. v4 in particular demonstrates organizational humility by naming v2's collision bug and v2+v3's cache fiction as worked examples. Keep all four RFCs + all four critiques + the devil's advocate documents in tree. The trajectory is the case study.

---

## One-line summary

v4 corrected the fictional Z3-cache architecture that survived v2 and v3 unchallenged by actually grepping the source, simultaneously corrected its own marketing headline from ~33% to ~20–25% based on tiktoken measurement, fully specified the AST-diff verifier predicate that v3 only asserted, raised Cliff's δ from small (0.2) to medium (0.33) for the one-way-door change, added 2 weeks of calendar buffer with explicit retry policy, gave Phase 1 retrospective monitoring matching Phase 2's prospective gate, and inscribed the lesson "every architectural claim must be `grep`-verified" — leaving only six calibration nits that apply inline at implementation time and one institutional action (codify the grep-verification rule somewhere durable); **approve, ship the 0.x patch PR, start the 0.x+1 branches, and stop iterating on the RFC**.

---

*Full path: `C:\Users\juanrivera\sources\repos\juanmicrosoft\calor-2\docs\plans\path-2-drop-ids-v4-critique.md`*
