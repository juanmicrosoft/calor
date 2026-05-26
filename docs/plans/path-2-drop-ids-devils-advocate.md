# Devil's Advocate Review — Path 2: Drop IDs

**Target:** `docs/plans/path-2-drop-ids.md`
**Voice:** an engineering auditor who is sympathetic to the goal of cutting tokens but unconvinced by the artifact in front of them
**Stance:** every load-bearing claim in this RFC is either unmeasured, internally contradictory, or under-scoped. The proposal is not yet ready to be acted on.
**Companion to:** `path-2-drop-ids-critique.md` (which attacks the *thesis*; this document attacks the *artifact*).
**Date:** 2026-05-22

---

## TL;DR

> The RFC asks for a breaking change to surface syntax, three releases of deprecation tooling, a rewrite of the philosophy, and a 3–4 week engineering effort — and the entire economic justification rests on a 5-program micro-benchmark, an unmeasured "LLM cognitive tax," and a round-trip strategy (`[CalorSymbol]`) that **silently re-introduces the very thing the RFC claims to remove**. Even if you accept the goal, the artifact is too thin to merge.

I am not arguing against ever doing this. I am arguing that this specific document, in its current form, is a draft of an idea — not a proposal to execute.

---

## 1. The savings number is a marketing figure, not an engineering one

§2.1 anchors the whole RFC on one table:

| Variant | Tokens | vs zerolang |
|---|---:|---:|
| Calor today | 485 | 1.82× |
| Calor Path 2 | 389 | 1.46× |
| zerolang | 266 | 1.00× |

Three problems compound:

**1.1.** **N=5.** Five tasks. `hello`, `add`, `divide`, `fizzbuzz`, `is_prime`. Every one of them is small enough that one module-header and one function-header dominate the token budget. The denominator (logic tokens) is tiny by construction, so removing fixed structural tokens (IDs) produces large *percentage* savings. Run the same measurement on a 300-line module with 15 functions and 4 nested types and the percentage collapses, because the logic denominator grows linearly while the structural numerator (ID tokens) grows logarithmically. **The 20% headline is a small-N artifact.** Nothing in §2.1 acknowledges this.

**1.2.** **The proxy is wrong.** "Tokens of source we send to the LLM" assumes the LLM consumes the whole file every turn. In practice, modern coding agents:

- Use tool-based reads (`view --range`)
- Cache prefixes
- Are billed per *output* token at 4–5× the rate of input tokens

For any production agent, the marginal token of *generated* Calor matters orders of magnitude more than the marginal token of *read* Calor. The RFC measures the cheap side. Where is the output-token measurement? Where is the cache-hit measurement?

**1.3.** **The comparison set excludes the alternative the RFC just rejected.** §11.4 dismisses "shorter, more BPE-friendly IDs" with "cognitive tax is independent of token tax." Fine — but then *measure both* and put them in the table. An 8-char alphanumeric ID (`f_a1b2c3d4`) at ~3 tokens per occurrence would land Calor between Path 2 and today. That cell is missing from the table because it would force the RFC to defend its decision against a milder alternative that costs no philosophy.

The honest version of §2.1 is: *we ran one cheap, badly-scoped benchmark, and it pointed in the direction we already wanted to go.*

---

## 2. "Everything is addressable" silently re-introduces identifiers — and the RFC admits it

§5.1 says the canonical address is the dotted qualified name. §6.6 then says:

> **Option A (recommended):** Replace `[CalorId]` with `[CalorSymbol("Calculator.Divide")]`. … The attribute is cheap (one line per emitted member) and the migration tooling already depends on it.

Stop. Read that again.

The RFC's central claim is that names are now identity, so no separate identifier is needed. Then in the round-trip section it emits a separate identifier (the qualified name, redundantly attached as a string attribute to every C# member) **because the round-trip cannot survive without one**. If names truly were identity, the attribute would be derivable. The fact that the RFC keeps it — and notes that "migration tooling already depends on it" — proves the implicit dependency on a stable, machine-readable handle that is *not the source-visible name*.

This is the ULID, just spelled differently. The agent now has to keep `[CalorSymbol("Calculator.Divide")]` in sync with the actual qualified name across every rename. That sync is a *new* class of bug:

- Today: rename → IdScanner says "this ID moved, that's fine, it's stable."
- Path 2: rename → must update every `[CalorSymbol]` attribute in generated C# to match, or the round-trip recovers the wrong name.

The RFC handwaves this in §7.3 ("This is a one-line code change in any consumer") but it isn't a consumer problem. It is a **producer correctness problem** for the migration pipeline itself. If `Calculator.Divide` is renamed to `Calculator.SafeDivide` in the `.calr`, the next `calr → cs → calr` cycle has to:

1. Re-emit `[CalorSymbol("Calculator.SafeDivide")]` (new)
2. Detect that the *previous* attribute `[CalorSymbol("Calculator.Divide")]` referred to the same entity
3. Not lose any metadata attached to the old name

In the ID world, step 2 was free (ID matched). In the name world, step 2 requires either a separate change-history file or a heuristic ("the function in roughly the same place with similar signature is probably the renamed one"). **The RFC does not specify the algorithm.** §5.5 just says "git follows entities by name" and waves at `git log --follow`. That's a file-level affordance; it does not address entity-level round-trip.

---

## 3. Positional sub-block addressing is strictly worse than IDs

§5.1 specifies:

```
Calculator.Divide.body[3]                  // the 4th statement
Calculator.Divide.body[3].then.body[0]     // the first statement of its then-branch
```

And §6.7 says diagnostics emit this form: `Calor0501 at Calculator.Divide.body[3] (file:line)`.

Consider the lifecycle of a diagnostic:

1. Compiler emits `Calor0501 at Calculator.Divide.body[3]`
2. Agent reads the diagnostic
3. Agent adds a `§B{x:i32}` binding at the top of the function
4. Now the offending statement is at `body[4]`, not `body[3]`

The address **invalidated itself** the moment the agent made a single edit. Every cached diagnostic, every memorized location, every cross-turn reference is stale after any insert/delete above it. This is the exact failure mode `stable-identifiers.md` was designed to prevent, restored verbatim under a different name.

Three follow-on consequences the RFC does not address:

- **MCP `calor_navigate` "definition"** for sub-block targets is now non-cacheable across edits. Today, the ID is the cache key. Path 2 has no cache key.
- **Diagnostic suppression** ("ignore Calor0501 at this specific site") becomes ambiguous: which `body[3]` did you mean? The one in the original snapshot or the one after the edit?
- **Test fixtures** that assert "diagnostic at Calculator.Divide.body[3]" become brittle the same way line-numbered fixtures are. Calor's snapshot tests already churn enough without adding this.

§5.2 fixes uniqueness at the sibling-declaration level. It does not fix positional fragility within bodies. That problem is, by construction, structurally worse than the ID problem the RFC removes.

---

## 4. The dual-mode parser is not a transition tax, it is a permanent tax

§6.2:

> For backward compatibility during transition: parser accepts both forms. If `_pos0` looks like an ID (prefix matches `m_`, `f_`, etc.), it's parsed as the legacy form and the Name is at `_pos1`. Otherwise the new form. The parser emits `Calor0806` (deprecation warning) on legacy form.

Compounding §7.1:

> | **0.x** (Path 2 ships) | Parser accepts both forms. … Legacy form emits `Calor0806` deprecation warning.
> | **0.x + 1** | Legacy form is opt-in via `--allow-legacy-ids` flag.
> | **1.0** | Legacy form removed.

Three problems:

**4.1.** **Detection is heuristic.** "If `_pos0` looks like an ID (prefix matches `m_`, `f_`, etc.)" — but in test files, IDs use the short form `f001`, `m001`. The disambiguation is: does `_pos0` start with `f` followed by digits? Then it's a legacy ID. But what if a developer writes a module called `f1Module`? `m1Helper`? The collision space is small but real, and the RFC does not specify the precedence rules formally. There is no grammar in this RFC, only a hand-wave.

**4.2.** **The parser carries the dual mode for at least two release cycles.** Given Calor's ~bi-monthly release cadence (per `Directory.Build.props` history), that's 4+ months of *every commit to Parser.cs* having to consider both forms. The branch coverage of test cases doubles for that period.

**4.3.** **Snapshot churn is grossly underestimated.** §8 Risk R1 says "~50 golden files need new variants." Let's count properly. The `tests/Calor.Conversion.Tests/TestData/` directory plus `tests/Calor.Compiler.Tests/TestData/` plus the round-trip harness fixtures all contain `.calr` and `.g.cs` files with embedded IDs. A grep for `[CalorId(` across `src/`, `tests/`, and `samples/` will return *hundreds* of hits, not ~50. Each one needs:

- A new snapshot baseline
- A regenerated `.g.cs` because the `[CalorId]` → `[CalorSymbol]` switch changes every emitted member

The "snapshot updater script" the RFC promises does not exist yet, and writing it correctly is itself a 1-week task because the existing snapshot infrastructure was not designed for a global attribute renaming.

---

## 5. The effort estimate is wrong by a factor of two

§10:

| Phase | Estimate |
|---|---|
| A — Parser, AST, snapshots | 5 days |
| B — Visitors, emitters | 4 days |
| C — Diagnostics, migrator, MCP | 4 days |
| D — Samples, docs, agent instructions | 3 days |
| **Total** | **~3 weeks** |

Let's audit by reference to known costs in this repo:

- **`CodeGen/CSharpEmitter.cs` is 4,600 lines** and is one of ~6 visitor implementers that need to handle the AST nullability change. The RFC allots ~20 lines of delta. The actual change touches every emit site that today reads `node.Id` and either inserts it into output or uses it as a cache key. Without a careful audit, I would expect the real delta to be 200–400 lines per major visitor, not 20.

- **`Migration/RoslynSyntaxVisitor.cs` is 6,500 lines** and currently has dozens of sites that read `[CalorId]` for round-trip stability. Switching to `[CalorSymbol]` requires not just a rename but a re-think of how the visitor reconstructs structure when the attribute is missing (which it will be on hand-written C# being migrated for the first time). The RFC allots 15 lines. This is wishful.

- **AST nullability change across ~14 node types at 50 lines each = 700 lines**. Fine. But every `IAstVisitor<T>` and `IAstVisitor` implementer (per `CLAUDE.md`, there are 5 listed) must be re-audited. That's 5 × 14 = 70 visit-method touchpoints, each requiring a decision: does this visitor depend on ID? What does it do when ID is null? The RFC treats this as mechanical. It isn't.

- **Diagnostic test fixtures.** Every test that asserts a diagnostic location string (today `f_01J5X…`) now asserts a qualified name. Hundreds of assertions across `Calor.Compiler.Tests`, `Calor.Semantics.Tests`, `Calor.Verification.Tests`, `Calor.Enforcement.Tests`. The RFC does not budget for this at all.

- **VSCode extension grammar** (mentioned in §8 Phase D as a single bullet). The TextMate grammar for `.calr` files has explicit patterns for ID positions in tags. Updating it correctly while keeping legacy highlighting working through the deprecation period is itself a multi-day task.

A realistic estimate, by analogy to comparable refactors in this repo (e.g., the binder class-members work in `implementation-summary-binder-class-members.md` ran 14,588 bytes of *summary*, suggesting weeks of actual work for a smaller surface), is **6–10 weeks of focused work for one engineer**, plus a tail of snapshot churn that lasts months. Calling it 3 weeks is the kind of optimism that ends with a half-migrated codebase and a parser that has to support both forms forever because the migration stalled.

---

## 6. The Z3 verification story is unaddressed

`docs/philosophy/stable-identifiers.md` lists "round-trip stability" as benefit #3. Calor's verification layer (`Verification/`) uses node identity to:

- Cache Z3 proof obligations between compiles (proof caches keyed by obligation ID).
- Map obligations to their declaration site for incremental re-verification.
- Detect when a proof becomes stale (the body changed, the obligation didn't).

The RFC mentions Z3 only once, in passing (§5 "Verification/ExpressionSimplifier.cs — no change (doesn't touch IDs)"). This is not a real claim about the verification system as a whole. It is a claim about one file.

Concrete questions the RFC does not answer:

- Today, a `§PROOF{pf_01ABC:DivisorNotZero}` has a stable ID. When the source name is `Calculator.Divide.DivisorNotZero`, what is the proof-cache key? The qualified name? Including the body hash? Just the obligation name?
- If two functions in different modules both have a `DivisorNotZero` obligation, do they share a proof cache entry?
- If the function is renamed `Divide` → `SafeDivide`, does the proof have to be re-run, or can the cache be migrated? Today (ID-based) it's a free identity match. Path 2 has to introduce some other mechanism.

The verification cache is one of Calor's actual differentiators (per the RFC's own §2.1, which credits Z3 with the "verification dividend"). Path 2 breaks the cache key model and ships no replacement. **This is not an annoying detail. This is the load-bearing argument for why Calor's ID system existed in the first place.** Skipping it in the RFC is not "out of scope" — it is the RFC dodging the part of the argument it cannot win.

---

## 7. Refinement types and proof obligations get the worst of both worlds

§5.1 example:

```
Calculator.PosInt                // refinement type
Calculator.Divide.DivisorNotZero // proof obligation (named)
```

Two problems:

**7.1. Refinement types share a namespace with classes and functions.** `Calculator.PosInt` is unambiguous *if* you know `PosInt` is a refinement type. But what disambiguates `Calculator.User` (a class) from `Calculator.User` (a refinement type)? Today, the ID kind prefix (`c_` vs `rt_`) does it for free. Path 2 §5.2 says "sibling declarations must have unique names" — but does this enforce kind-cross-uniqueness? The RFC does not say. There is one paragraph on overload disambiguation and zero paragraphs on cross-kind disambiguation.

**7.2. Proof obligation names are now required to be unique within a function.** `Calculator.Divide.DivisorNotZero` is fine until a function has two obligations the author wants to name the same thing (e.g., two divisions, each with a "DivisorNotZero" obligation). Today, IDs make that trivial. Path 2 forces the author to mint a *new* name — `DivisorNotZero1`, `DivisorNotZero2` — which is exactly the kind of meaningless suffix the RFC criticized ULIDs for in §2.3. The cognitive tax just moved from "copy a ULID" to "invent a unique name." For a verification language, this is a regression.

---

## 8. The "agent UX is better" claim is asserted, not measured

§5.4:

> The agent's UX is better because it can issue rename commands in natural-language form.

§2.3:

> The LLM gets zero semantic benefit from an ID it cannot reason about — it has to maintain a parallel name-to-ID mapping in its working memory.

Both claims are intuitive. Neither is supported by data from this repo.

The repo does have an agent-evaluation harness (`tests/E2E/agent-tasks/run-agent-tests.sh`, referenced in the project instructions). The RFC could have:

- Run the existing agent tasks against today's Calor and a Path 2 prototype.
- Reported success rate, turn count, and edit-correctness deltas.
- Shown that agents do measurably better on Path 2.

It does none of this. It cites Phase 0 (which measured *tokens*, not *agent performance*) and asserts that token reduction implies UX improvement. **That implication is the entire load-bearing argument and it is not measured.**

It is also entirely plausible that agents do *worse* under Path 2 in some classes of task — specifically, multi-edit refactors where the agent's first edit invalidates the addresses it cached for the second edit. The fragility introduced in §3 (positional sub-block addresses) is the obvious risk vector, and the RFC has no data ruling it out.

---

## 9. The migrator is described as if it were trivial; it isn't

§7.2:

> 1. Parses every `.calr` file under `path`.
> 2. For each AST node with an ID, sets the ID to null.
> 3. Re-emits with `CalorEmitter` (which under Path 2 never writes IDs).
> 4. Writes the file back.
>
> The migrator is idempotent. Files already in Path 2 form pass through unchanged.

Specific things the RFC does not address:

- **Comment preservation.** `CalorEmitter` today round-trips most code but loses comment placement in some edge cases. A migration that nukes comments is a non-starter. The RFC does not commit to a level of comment preservation.
- **Formatting preservation.** Calor source files have author-chosen whitespace, blank-line patterns, and section ordering. Does re-emit preserve them? `CalorEmitter` is not currently a fully-preserving printer. A migration that reformats the entire codebase is a giant blast radius hidden inside "the migrator is idempotent."
- **Diagnostic suppressions.** Are there any `// calor-suppress: Calor0801 f_01ABC` style directives in the codebase? The RFC does not say. If there are, they must be rewritten.
- **External references.** Anything outside the `.calr` source that names an ID (documentation, commit messages, issue trackers, the website) becomes stale. The RFC mentions docs (§6.9) but not the long tail.

§7.2's four steps are a sketch, not a plan. A real migrator-design document would itemize each of these and assign a behavior. This RFC does not.

---

## 10. The deprecation timeline contradicts itself

§7.1 proposes three releases of deprecation: ship → warn-only → flag-gated → removed. The RFC also says, at the very top:

> **Target release:** Pre-1.0 breaking change (0.x → 0.x+1)

And in §7.1's text:

> Three releases gives downstream users time to migrate. Because Calor is pre-1.0, this is well within the "breaking changes allowed" envelope per `CLAUDE.md`.

Pick one. Either Calor is pre-1.0 and we can do hard breaks without a deprecation period (the user gets a clear error message, runs `calor fix --drop-ids`, moves on), or we owe users a slow deprecation. The RFC wants both: the *speed* of "we're pre-1.0, this is allowed" and the *politeness* of a three-release window. The cost of that politeness is the dual-mode parser tax (§4) for the full window. Either commit to one big rip-the-bandage release with a mandatory migrator run, or commit to a full deprecation cycle — but stop pretending you can have both for free.

A single-release hard break is what Calor's pre-1.0 status actually permits and what `CLAUDE.md` actually encourages. The three-release window in §7.1 is the RFC importing the etiquette of a 1.0+ language while keeping the freedom of a 0.x one.

---

## 11. The "rejected alternatives" section is a strawman parade

§11 lists five rejected alternatives. Each rejection is one paragraph. The reasoning is consistently:

- **11.1 (Sidecar file):** rejected because the user disliked it. (That's a preference, not an analysis.)
- **11.2 (Optional IDs):** rejected because choosing-when-to-add-an-ID is itself a tax. (True, but no measurement of how often the choice would actually fire.)
- **11.3 (Compiler-generated inline IDs):** rejected because agents would learn to ignore them. (Asserted, not measured.)
- **11.4 (Shorter, BPE-friendly IDs):** rejected because "cognitive tax is independent of token tax." (See §1.3 — this should have been the alternative the RFC actually steelmanned, because it would have shrunk the token gap to ~5–8% while keeping every benefit of stable identifiers.)
- **11.5 (Status quo):** rejected because the verification dividend "only" applies to programs with preconditions. (But §6.6's `[CalorSymbol]` Option A is itself a status-quo-with-rename — so the RFC is partially adopting the thing it claims to reject.)

A good rejected-alternatives section makes the reader nod: "yes, those are worse." This one makes the reader suspect the alternatives were never seriously prototyped. None of them have a token measurement. None of them have an agent evaluation. They are dismissed by argument, not by experiment.

---

## 12. The grammar in §6.1 is incomplete

§6.1 lists 11 declaration tags and 4 sub-block tags. The Calor language has more constructs than that. From a quick scan of `Ast/`:

- **Pattern matching** (if it exists — the RFC doesn't say what happens to match-arm IDs)
- **Async/await markers** (`§AF`, `§AMT`, `§AWAIT` — what IDs do these have today, and what do they look like Path 2?)
- **Collection literals** (`§LIST`, `§DICT`, `§HSET` — they have names today; are those names IDs?)
- **CSharpInteropBlockNode** — the RFC explicitly says "Migration/FeatureSupport.cs: no change" in §8. But interop blocks have IDs today for round-trip. Does Path 2 keep them?
- **MemberPreprocessorBlockNode** — preprocessor conditions; how are they addressed when IDs are dropped?

The RFC's §6.1 grammar table is selective. A real proposal needs the full enumeration, not a sample. The reader cannot evaluate the proposal without knowing whether the things they care about are in or out.

---

## 13. The "Open questions" section is the proposal

§12 lists six open questions:

1. Round-trip attribute footprint
2. Overload disambiguation syntax
3. Constructor naming
4. Whether to delete the philosophy doc
5. Migrator in-place vs parallel-tree
6. Deprecation timeline duration

Of these, **(1), (2), (3), and (6) are not open questions — they are the proposal.** A reader cannot evaluate Path 2 without knowing the answer to "how do I name an overload" or "what does a constructor address look like." These are not edge cases; they are core syntactic surface area. Punting them to a follow-up is fine for a design discussion but disqualifying for an RFC marked **Status: Draft** that is also explicitly recommending acceptance in §14.

The maintainer cannot say "yes, accept this RFC and proceed with Phase A" without committing to answers for §12. §14's "Recommended: accept and proceed" therefore asks for a blank check.

---

## 14. The token math was decided before the RFC was written

§2.4 says "Names are required where they exist today" and §5.1 says "the canonical address of any declaration is its dotted qualified name." Both claims are consistent.

But the *order* of writing — and the *direction* of the argument — give away the actual reasoning process. The RFC starts with a 20% token reduction (§2.1), then derives a syntactic proposal (§5), then derives an implementation plan (§6–8), then dismisses alternatives (§11), then asks "should we proceed?" (§14). This is the structure of a memo arguing *for* a decision already made, not the structure of a memo evaluating a decision space.

The honest structure would be:

1. **What problem are we solving?** (Agent UX, with a measurement.)
2. **What is the design space?** (Sidecar, optional, short IDs, no IDs, Lisp-style nameless, …)
3. **What did we measure?** (Token cost, agent success rate, parse complexity, round-trip stability) **for each point in the space.**
4. **What does the data say?** (Maybe no IDs wins. Maybe short IDs win. Maybe optional IDs win.)
5. **What do we recommend?** (Based on the data, not the preference.)

The current RFC skips (1) (no measurement of the problem), conflates (2) and (5) (the design space is "Path 2" and the recommendation is also "Path 2"), and treats (3) and (4) as completed when the only measurement is N=5.

This is not a fatal flaw. It is a request to rewrite the RFC in the form of an inquiry, not a brief.

---

## 15. The strongest argument *for* Path 2 is not in the RFC

There is a real argument for dropping IDs that the RFC does not make:

> Calor's surface syntax is the medium through which agents write the language. Every character of structural noise an agent writes is a character it could have spent on logic. The right way to measure the ID tax is not "tokens in the file" but "characters the agent has to plan, type, and verify per declaration." On that metric, an ID is ~28 characters of attentional load every time the agent emits a function. Multiplied across a session, this is the dominant cost of writing Calor.
>
> The fact that no one has measured this is itself the argument: ULIDs are an unmeasured cost that everyone in this codebase has been paying without an A/B comparison. Path 2 is not a definitive answer; it is the first cheap experiment we can run to find out whether the cost was real.

This argument is *valid*. It is also *humble* — it does not claim the win is 20% on toy programs; it claims that running the experiment is worth ~3 weeks of engineering. A reframed RFC that asked for **a four-week timeboxed experiment with explicit kill criteria** (e.g., "if agent task success rate on the harness does not improve by ≥10%, we revert and document") would be hard to argue against.

The current RFC asks for a permanent surface change, a philosophy repeal, and a three-release deprecation window — based on the same evidence that would justify only an experiment. That asymmetry is the central problem with the document.

---

## Recommendations (devil's advocate edition)

If I were the maintainer, I would respond to this RFC with:

**1. Reject in its current form**, with prejudice toward a rewrite, not the idea.

**2. Require a measurement gate before any code lands:**
- Run the agent harness (`tests/E2E/agent-tasks/run-agent-tests.sh`) against today's Calor and a Path 2 prototype.
- Report: success rate, turn count, edit-correctness errors, identity-preservation errors per task.
- Demand statistical significance — minimum N=20 tasks, ≥3 runs each, confidence intervals.

**3. Require the rejected alternatives to be measured, not argued away.** Specifically: a "short IDs (8-char base32)" variant should be in the table. If short-ID Calor lands at 1.50× zerolang vs Path 2's 1.46×, the philosophy repeal is unjustified.

**4. Require the round-trip story to be coherent.** Either commit to "names are identity" and drop `[CalorSymbol]` (Option B), or admit that round-trip needs a stable handle and design that handle deliberately. The current compromise (Option A) is the worst of both worlds: it carries the cost of an attribute on every member without committing to attribute-as-identity.

**5. Require positional sub-block addresses to be designed for stability**, not just defined. A scheme that breaks on every edit-above is not a scheme.

**6. Require an answer to §12 Q2 and Q3** (overload disambiguation, constructor naming) before approving. These are surface syntax, not appendix material.

**7. Require a real Z3 cache-key story.** What's the cache key under Path 2? If it's "qualified name + body hash," the cache invalidates on every code change inside the function, defeating the cache. If it's "qualified name only," the cache returns stale proofs after a body change. If it's something else, name it.

**8. Pick one timeline strategy.** Either:
- **Hard break:** ship Path 2 in 0.x+1 as the only form, with a one-shot migrator. No deprecation window. Pre-1.0 lets us do this.
- **Full deprecation:** keep the dual-mode parser, but commit to maintaining it for the full deprecation period as a *first-class* feature, not as a bridge. Then the parser complexity is amortized.

The current "three releases of polite deprecation" is the most expensive option and isn't justified by the user base (which, as a pre-1.0 internal-research project, is essentially the project itself).

---

## What I am *not* saying

I am not saying ULIDs are good. I am not saying the token tax is imaginary. I am not saying Path 2 is wrong in concept.

I am saying:

- The evidence in the RFC is too thin for the magnitude of the change.
- The technical design has at least four unresolved holes (round-trip identity, positional fragility, Z3 cache keys, overload disambiguation).
- The effort estimate is roughly half of what the change will actually cost.
- The deprecation strategy is internally contradictory.
- The "rejected alternatives" section dismisses the strongest competitor (short IDs) on philosophical grounds without measuring it.

A version of Path 2 that addressed these — a humbler, experiment-framed, measurement-anchored RFC with explicit kill criteria — would be a much stronger document. The current document is a recommendation in search of evidence.

---

## Coda

The companion critique (`path-2-drop-ids-critique.md`) attacks the *thesis* of Path 2: that names are identity. This review takes no position on that thesis. It says only that the *artifact* arguing for it is not yet good enough.

Both can be true. The thesis might be right and the RFC might still need a rewrite. Approving the RFC because the thesis is correct would set a precedent for accepting under-evidenced proposals in this repo — and the eight previous `critique-*` files in `docs/plans/` suggest the team has been pushing back on exactly that pattern for months. Path 2 should not get a pass because its conclusion is convenient.

**Verdict:** *Send back for revision. Do not merge.*
