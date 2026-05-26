# Brutal Critique — Path 2: Drop IDs

**Target:** `docs/plans/path-2-drop-ids.md`
**Voice:** the original designer of Calor, who built the ID system and shipped it
**Stance:** this RFC is wrong about what Calor is for, and approving it kills the project
**Date:** 2026-05-12

---

## The one-line indictment

> Path 2 makes Calor cheaper to *write* by deleting the only thing that made Calor worth *evolving*. It optimizes for the first edit and surrenders every edit after that. It declares parity with zerolang (RFC §2.1) and calls that a win. **Parity with zerolang is the loss condition for this project.**

---

## What Calor was actually built to do

I built the ID system because every other language has the same identity model — names — and every other language has the same failure mode: rename breaks blame, extract breaks references, move breaks merges, and agents thrash on every refactor because their mental model of "this function" is a string that changes whenever a human or another agent edits it.

Calor's reason to exist was never "a slightly more verbose C# with effects." It was: **a language whose declarations have identity that survives the edit history.** ULIDs were the operational mechanism. The mechanism is replaceable. The thesis is not.

The RFC repeals design principle #3 ("Everything Has an ID") and replaces it with "Everything is addressable." Read those two sentences carefully. The first is a claim about *identity*. The second is a claim about *lookup*. They are not the same thing, and the RFC pretends they are.

Names are addressable. They are not identity. A renamed function has a different name and the same identity. Path 2 cannot tell the two apart. **That is the whole bug.**

---

## The token math is a benchmark sleight-of-hand

§2.1's table is the load-bearing evidence. Read it again:

> *Phase 0 benchmark (5 small tasks, cl100k tokenizer)*

**N=5. Toy programs. cl100k.**

The five tasks are `hello`, `add`, `fizzbuzz`, `divide`, `is_prime` (inferred from §13's examples). These are the worst possible case for IDs because they have a high ID-to-logic ratio. A 6-line `hello` program has one module ID, one function ID, and no logic. Of course IDs dominate the token count. They dominate by construction.

Now extrapolate the "20% savings" to a 400-line `OrderFlow.Persistence` module with 8 classes, 40 methods, and real business logic. The ID-to-logic ratio collapses. The savings collapse with it. The RFC does not run this measurement. It cites N=5 toy programs and projects to "all programs." That's the same statistical malpractice the v1/v2 critiques flagged in earlier plans. Six plan revisions ago we were criticizing this. Now it's in a Path 2 RFC and we're pretending it's evidence.

The honest version: *we measured ID cost on 5 toy programs where IDs are the worst possible case. We did not measure on realistic programs. The 20% figure is an upper bound that does not generalize.*

Then ask whether even the upper bound matters. 96 tokens out of an 8k context window is 1.2%. Out of a 200k Claude context, 0.05%. The "token tax" was rhetorical scaffolding for a decision that was made on other grounds.

---

## The "cognitive tax" claim is unmeasured

§2.3:

> *In practice, the agent reads the file, finds the function by name, and then has to copy the ID character-by-character. That's not a feature; that's a tax on every edit.*

Where is the measurement? Where is the agent run that shows edit failures attributable to ID-copy errors? OrderFlow Phase 0 attributed the Cell 3 win to *removing MCP tools*, not to *removing IDs*. Track B (the Python surface) tested syntax, not identity. There is no in-tree benchmark, no logged failure mode, no agent telemetry that supports "IDs cost cognition." The claim is plausible. Plausibility is not evidence.

If you want to make this argument honestly, run the experiment:
- Pick N≥20 agent tasks involving multi-edit sequences (rename, extract, move).
- Run each with ID-bearing Calor and with name-only Calor.
- Measure: turn count, success rate, identity-preservation errors per task.
- Report median + range.

The RFC does none of this. It asserts cognitive cost as obvious and proceeds. **This is the exact rhetorical move the v1–v6 critiques have been hammering for two weeks.** I expected better.

---

## The rejected alternatives are biased toward the conclusion

§11 rejects four alternatives. Look at the reasoning:

- **§11.4 (BPE-friendly short IDs):** rejected because *"the cognitive tax is independent of the token tax."* But cognitive tax is the unmeasured claim. The RFC dismisses the alternative on the same unproven grounds it uses to motivate Path 2. Circular.
- **§11.3 (compiler-generated inline IDs):** rejected because *"this trains the agent to ignore IDs, then forces the agent to re-read them whenever it edits."* If true, this is also true of every other language's symbol IDs in debug info, type metadata, and so on — and they don't seem to break agents.
- **§11.2 (optional IDs):** rejected because *"the middle path is the worst path."* This is an aesthetic claim dressed as engineering. The honest version: the author preferred a clean rule over a calibrated one.
- **§11.1 (sidecar file):** rejected because *"the user explicitly disliked this option."* That's not an engineering argument. That's deference to a preference. Fine — but call it what it is.
- **§11.5 (status quo):** rejected because *"verification dividend is only on divide-like programs"*. The RFC's own §2.1 says the residual 1.46× Calor-Path-2 tax *also* "buys" Z3 proofs, taint analysis, hosted-capability tracking — i.e., the verification dividend the RFC just dismissed. Pick a position.

Every rejection leans on either an unmeasured cognitive claim or an aesthetic preference. The RFC has not done the work to know whether any of the rejected alternatives outperform Path 2.

---

## Path 2 contradicts the project's own strategic direction

This is the deepest objection and the one that should block this RFC outright.

We just spent the last two weeks (v1 through v6 of `pivot-execution-plan-*.md` in this same directory) arguing about how to position Calor as the **semantic execution and verification layer for autonomous coding agents** — an IR-shaped product whose load-bearing assets are diff, merge, coordination, and memory. Every one of those subsystems was designed to be **keyed on stable identity that survives across edits**.

From v5/v6 explicitly:
> *"Critical: the deltas must reference stable node IDs, not line numbers, so they survive reformatting. Existing IdScanner (Ids/IdScanner.cs, 330 LOC) gives us the substrate."*

The IR thesis dies the moment names are the canonical identity, because:

- Diff over names cannot distinguish "function renamed" from "function deleted and new function added." Path 2 §5.4 says the rename refactoring emits a single commit recording both names — but **only if the agent uses the refactoring tool**. If the agent issues raw edits (which it will), names-based diff sees deletion + addition, and the diff/merge subsystem corrupts.
- Merge over names re-introduces the merge-hell problem the IR thesis was supposed to solve. Path 2 §2.2.3 calls this "a solved problem (every other language)" — but every other language is exactly what coding agents struggle with. Saying "every other language has this problem and is fine" is saying *Calor's competitive moat against every other language just evaporated.*
- Coordination across agents on the same code requires region identity. With names, two agents renaming the same method to different names *both succeed* — and now there are two regions with two names, and the coordination protocol has nothing to lock against.
- Memory keyed on names rots whenever any agent renames. Path 2 §5.4 says the rename tool updates references in one atomic operation — but the memory store is *external* to the source files. Either every memory entry gets re-keyed on every rename (expensive and racy) or the memory store dangles.

The RFC does not address any of this. §2.2.2 dismisses "survives rename" as "a refactoring problem, not a programming problem." For a language whose pitch is *agents continuously evolve software using us as the substrate*, refactoring IS the programming problem. Path 2 picks the wrong side of that.

**Approving Path 2 and approving the pivot to "semantic IR for agents" are mutually exclusive.** Pick one. You cannot have both. The RFC pretends this conflict doesn't exist by §1.1's claim of "no semantic change" — but identity *is* semantics for the use case Calor was repositioned to serve.

---

## The "parity with zerolang" framing is the confession

§2.1, emphasis mine:

> *On real-logic tasks like fizzbuzz, Path 2 brings Calor to ~parity with zerolang (1.04× tokens) while **preserving Calor's effect declarations, contracts, and typed I/O**.*

Read what this sentence is actually celebrating. Calor — after Path 2 — costs 1.04× zerolang tokens. Zerolang is a stripped-down text language with no effects, no contracts, no Z3, no nothing.

The author thinks the win is "we got close to zerolang's cost." The actual question is: *why would an agent operator pick 1.04×-cost-Calor over 1.00×-cost-zerolang?* The answer the RFC offers is "effects, contracts, typed I/O." Those are real features. But the RFC also just established that the cost of using Calor is now ~parity with zerolang — and parity means **the only differentiator is the verification stack**, which the RFC itself dismisses as "only on divide-like programs."

Run that argument to its conclusion: Path 2 reduces Calor's token cost to near-zerolang, leaves only the verification stack as the differentiator, and the verification stack only matters on a narrow class of programs. **Path 2 reduces Calor to a small library of verification primitives on top of a zerolang-like text surface.** That's not a language. That's an analyzer.

I built an analyzer-vs-language distinction into Calor for a reason. Path 2 erases it.

---

## What the RFC quietly throws away

Item by item, from the surface table in §2.4 and §6.6:

1. **`[CalorId]` → `[CalorSymbol]`.** This is downgrading a globally-unique opaque identifier to a name-based string. Round-trip is degraded silently — the RFC §6.6 admits *"this is enough for round-trip stability because names are the new identity"* but never tests it. Names break on rename. Round-trip is now rename-fragile. Every C# → Calor → C# cycle that involves a renamed member will lose identity. The 99% round-trip success rate on 14.7k files is now contingent on no renames happening anywhere. That contingency was never required before.
2. **Diagnostic codes Calor0800–0805.** Deleted from the default pipeline (§9.1). These were the ID validation surface. Deleting them is fine if you're deleting IDs. But they were *also* the integrity layer for `IdScanner`, which the pivot plans (v3+) called "the stable-ID substrate for diff/merge/memory." Path 2 deletes the substrate while the pivot plan depends on it. Internal contradiction.
3. **`IdScanner`, `IdValidator`, `IdGenerator`.** §6.4 demotes to "optional metadata producers," §7.1 deletes in 1.0. Pivot plan v6 requires them as part of `Calor.SemanticIR`'s foundation. After Path 2 ships, the foundation is rubble.
4. **The "everything has an ID" guarantee.** This was the most-cited differentiator vs every other language. It is the *one sentence* that distinguished Calor's value proposition. The RFC repeals it casually.

The RFC frames these as cleanup. They are amputations.

---

## Things the RFC gets right (so this isn't a hit piece)

Credit where due:

- §2.3's observation that random ULIDs tokenize roughly one-char-per-token is **correct and important**. This is a real cost. The right response is not to delete IDs; the right response is to use a tokenizer-friendlier ID format (which §11.4 rejected on unmeasured grounds).
- §6.4's incremental migration through nullable IDs is technically clean. If we were going to ship this — we should not — the engineering plan is competent.
- §9.3's diagnostic-with-qualified-name format (`Calor0501: division by zero in Calculator.Divide at line 42`) is genuinely more readable than the ULID form. This *can* coexist with IDs — emit qualified name in diagnostics, keep ID in identity store. That's a 1-day change, not a 3-week migration.
- §2.3 correctly identifies that the agent rarely *uses* the ID for reasoning. True. But "rarely uses for reasoning" is not the same as "doesn't need to exist." The agent doesn't reason about line numbers either, and we don't propose deleting line numbers.

The RFC has a real grievance (ULID tokenization cost, diagnostic readability) wrapped around a wrong conclusion.

---

## What an honest version of this RFC looks like

If the goal is reducing ID-related token cost without destroying the identity model:

**Alternative A — Tokenizer-friendly IDs (the §11.4 alternative, done seriously):**
- Replace 26-char Crockford Base32 ULIDs with 6–8 char BPE-friendly identifiers.
- Use a deterministic mapping from existing ULIDs to short IDs (idempotent migration).
- Measure on N=20 real programs (not 5 toy programs), report median + range.
- Expected savings: 50–70% of current ID-token cost, with identity model fully preserved.

**Alternative B — Surface elision (compromise that preserves identity):**
- IDs remain in the AST and on disk metadata, but are *elided from the human/agent-facing surface render* by default.
- The compiler maintains identity in `*.calr.identity.json` sibling files (git-tracked).
- Agent reads ID-free source; compiler reads ID-bearing source; round-trip preserves identity.
- §11.1 rejected sidecar because "the user disliked it." That's not a sufficient reason if the alternative is destroying the identity model.

**Alternative C — IDs only where they pay (the §11.2 alternative, done seriously):**
- IDs on `pub`-cross-module declarations only (the units where rename costs are real and refactoring crosses file boundaries).
- No IDs on locals, intra-module helpers, or sub-blocks.
- This concedes the "everything has an ID" principle but only where IDs cost more than they earn.
- This is the calibrated middle path the RFC dismisses as "the worst path." It is not. It is the *only* path that preserves the agent-substrate thesis while addressing the token cost.

Any of these would be a 1–2 week change with a measurable improvement and no thesis-level cost. Path 2 is a 3–4 week change that costs the thesis. The RFC does not compare effort against alternatives. It also does not measure on realistic programs.

---

## What I think actually happened in writing this RFC

Be honest. This RFC was written because:

1. The Phase 0 benchmark showed Calor at 1.82× zerolang.
2. That number was uncomfortable, so the project looked for a way to reduce it.
3. The fastest reduction available is removing IDs (because IDs are the most-visible Calor-specific overhead).
4. The RFC was written to justify what was already decided.

That's not how design decisions get made on a project that wants to last. The Phase 0 benchmark is a *signal*. Signals deserve investigation. The investigation that this RFC documents is shallow: 5 toy programs, no measurement of cognitive cost, no measurement on realistic programs, no head-to-head against the alternatives in §11, no consideration of the conflict with the pivot plan.

If the project is going to make breaking changes that repeal design principles, the design principles deserve more than this. **They deserve adversarial review and a deeper benchmark.** Path 2, as drafted, would not survive its own §11 if §11 were honestly executed.

---

## Recommendation

**Reject Path 2 as drafted.** Specifically:

1. **Do not proceed with §7.1's deprecation timeline.** The 1.0 hard-removal commitment is irreversible and the RFC has not earned the right to commit the project to it.
2. **Approve §9.3 (qualified names in diagnostics) as a standalone change.** This delivers 60% of the perceived UX win at zero thesis cost. ~1 day of work.
3. **Run the actual measurement.** N=20 realistic programs (not 5 toy programs). Compare current Calor, Alternative A (short IDs), Alternative B (sidecar), Alternative C (cross-module IDs only), and Path 2. Report median + range, both first-write cost and multi-edit cost. **The "multi-edit cost" measurement is the one the RFC dodges entirely.**
4. **Resolve the conflict with the pivot plan.** The pivot plan (`pivot-execution-plan-v6.md`) requires stable IDs as the substrate for diff/merge/coordination/memory. Path 2 deletes them. One of these two documents has to give. Decide which, in writing, before either lands.
5. **Audit §11 honestly.** Re-examine each rejected alternative against measured data rather than asserted intuitions. Two of them (A and C) are plausibly better than Path 2 and have not been compared against it.

If you are the maintainer and you read this and think *"but the token cost is real and we need to address it"* — you are right that the token cost is real. You are wrong that Path 2 is the right address. The right address is one of the alternatives in §11 that the RFC dismissed without measurement. **Do the measurement. Then decide.**

---

## One-line summary

Path 2 saves 96 tokens by deleting the property — identity that survives the edit history — that made Calor worth choosing over zerolang in the first place, and it does so on the strength of a 5-program benchmark while contradicting the pivot strategy this project just spent two weeks designing; the right response is to fix the tokenizer-unfriendly ID format (§11.4 done seriously) or elide IDs from the surface render (sidecar, §11.1 with real engineering), not to repeal the design principle the project was built on.

---

*Full path: `C:\Users\juanrivera\sources\repos\juanmicrosoft\calor-2\docs\plans\path-2-drop-ids-critique.md`*
