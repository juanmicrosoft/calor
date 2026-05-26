# Devil's Advocate Review — Path 2 v2: Compact Stable Identifiers

**Target:** `docs/plans/path-2-drop-ids-v2.md`
**Voice:** an engineering auditor who reviewed v1 and is now reviewing the rewrite
**Stance:** v2 is a substantially better document than v1. It is not yet a document ready to act on.
**Companion to:**
- [`path-2-drop-ids-critique.md`](./path-2-drop-ids-critique.md) (attacks v1's *thesis*)
- [`path-2-drop-ids-devils-advocate.md`](./path-2-drop-ids-devils-advocate.md) (attacks v1's *artifact*)
- This document attacks v2's *remaining artifact weaknesses* and the places where v2 took credit for fixes that did not fix anything.

**Date:** 2026-05-22

---

## TL;DR

> v2 is a real improvement on v1 in five concrete ways (separates the two ID populations, drops the false `[CalorSymbol]` round-trip story, picks the hard-break that pre-1.0 actually permits, gates Phase 2 on measurement, and surfaces residual concerns honestly in §12). I credit all of this in §1 below.
>
> But three of v2's load-bearing claims are still wrong or unsupported, and one item v2 lists as "resolved" was never a real risk to begin with — v2 took credit for fixing it. Specifically: **the snapshot churn estimate is off by roughly 17×, Phase 1's "measured" savings reuse the same N=5 benchmark v2 ruled insufficient for v1, the Phase 2 measurement gate is structured to pass, and the "Z3 cache key story preserved" win is rhetorical because the Z3 cache was never keyed on the IDs in the first place.**
>
> Verdict: **send back for one revision, then ship.** v2 is roughly two days of audit work away from being merge-ready, not weeks. The critique below is mostly an audit checklist, not a rejection.

---

## 1. What v2 actually got right

The v1 devil's advocate review made eleven concrete attacks. v2 resolves six of them legitimately and a seventh by reframing. The honest scorecard:

| v1 review concern | v2 status | Verdict |
|---|---|---|
| Token math is N=5 toy programs | Acknowledged; reframed as "directional signal" | Honest reframing — though see §4 below |
| `[CalorSymbol]` smuggles identifiers back in | Withdrawn proposal entirely; §2.4 admits no such attribute exists today or in v2 | Resolved cleanly |
| Positional sub-block addresses are fragile | Confined to diagnostic *display*, not identity | Resolved by separating concerns |
| Dual-mode parser is permanent tax | Hard-break, single release | Resolved |
| Effort estimate off by 2× | Re-scoped; v2 admits v1's 3 weeks was undercounted | Mostly resolved (see §2) |
| Z3 cache key story | Claimed as preserved | **Not actually resolved; see §3** |
| Refinement-type / proof-obligation naming | Both keep IDs; collisions don't bind | Resolved by keeping symbol IDs |
| Open questions ARE the proposal | §7 resolves five of six | Resolved |
| Grammar enumeration incomplete | §5.1 + §5.4 give a complete table | Resolved |
| Rejected alternatives dismissed by argument | §4 NG5–NG7 explain how v2 incorporates the right parts | Reframed honestly |
| Hard break vs polite deprecation contradiction | Picks hard break unambiguously | Resolved |

This is real progress. A reviewer who landed v1 because "the thesis is interesting" would not have produced v2. The author engaged with the critiques in good faith and the artifact is meaningfully better for it.

That said — six-of-eleven plus one reframe is not eleven-of-eleven, and the remaining items matter.

---

## 2. The snapshot estimate is off by roughly 17×

§5.5 line on snapshot churn:

> Snapshot test updates (~30 fixtures, mechanical) — 2 days

§9.1 same number, propagated into the timeline.

A direct count says otherwise. Searching the `tests/` tree for files containing the structural-ID pattern `§(L|IF|WH|TR|FOREACH|DW|UNSAFE|FIXED|SYNC|USING|MATCH|FORALL|EXISTS)\{[a-z]+\d+`:

```
501 files
```

That's not 30. It's 501. **About 17× the v2 estimate.**

Two clarifications before drawing the implication:

- Not every file is a snapshot file that asserts byte-exact source content; many are just inputs to a test that doesn't check byte equality of the IDs. The mechanical re-emit still touches them, but only some need golden-file regeneration.
- Some of those 501 files are documentation samples in the test tree, not actual snapshot fixtures.

But even after pessimistic discounting (say, only 25% of them are genuine golden files that need regeneration), that's still ~125 fixtures, not 30. And the migrator must process all 501 anyway to confirm idempotence.

The implication for §9.1's "2 days" line is straightforward: it is at minimum 5 days, more likely a working week, and the work is not parallelizable across reviewers because each regenerated fixture needs eyeballs to confirm the diff is structural-only. **Phase 1's "~2 weeks" headline becomes ~3 weeks once this is corrected**, which puts it back into the same territory as v1's underestimate that v2 was right to flag.

The fix is small: re-count and re-budget. But the fact that this number was used unchecked is a warning sign — it suggests §5.5's other line items (CalorEmitter "~40 lines," Parser "~150 lines") may be similarly aspirational.

---

## 3. "Z3 cache key story preserved" is a non-fix dressed up as a fix

§5.3 row:

> Devil's advocate §6: "Z3 cache key story unaddressed" — **Resolved.** Proof obligation IDs are symbol-level and unchanged. Cache keys unaffected.

§6.4:

> Z3 proof cache: invalidated once. Rebuilds on first compile (the cache is already disk-local and the invalidation is one cold cache).

Both statements are non-sequiturs. They presuppose that the Z3 cache is keyed on obligation IDs. It isn't.

Reading `src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs` lines 10–57:

```csharp
public string HashPrecondition(
    IReadOnlyList<(string Name, string TypeName)> parameters,
    RequiresNode precondition)
{
    var sb = new StringBuilder();
    sb.Append("PRE:");
    AppendParameters(sb, parameters);
    sb.Append("::");
    AppendExpression(sb, precondition.Condition);

    return ComputeSha256Hash(sb.ToString());
}
```

The cache key is `SHA256(parameters || expression)`. Not the obligation ID. Not the function ID. Not anything ID-related. `VerificationCacheEntry.cs` stores `ContractHash` (a SHA256), not an obligation identifier. The cache has been content-hashed all along.

This means:

- The v1 devil's advocate §6 concern was, in fact, ill-founded — it assumed an architecture the code doesn't have. (I wrote that concern. It was a fair concern given v1's `stable-identifiers.md` text, which strongly implied ID-keyed cache. The code refutes it.)
- v2's §5.3 "Resolved" claim is technically true (the cache is unaffected) but for entirely the wrong reason. v2 implies the cache *would* have been affected and that preserving symbol IDs is what protects it. Neither is true. Preserving or removing symbol IDs would have left the cache equally unaffected.
- v2 §6.4's "invalidated once, rebuilds on first compile" line for Phase 2 is also wrong. The cache wouldn't invalidate at all under Phase 2 because the cache key doesn't include the ID. The "one cold cache" cost is zero.

This matters more than it sounds. The pattern is: v2 is given credit for a defense, when the honest answer is "this concern doesn't bind in either direction." A reviewer reading §5.3 walks away believing v2 has done thoughtful preservation work on the Z3 system. The thoughtful work is the system being content-hashed, which predates v2 by a wide margin.

Two specific corrections v2 should make before merge:

1. §5.3 row should read: *"v1 devil's advocate §6 concern was based on a misreading; the cache is content-hashed (`ContractHasher.cs`), not ID-keyed. Neither v1 nor v2 affects it. The architectural choice that protects this — not the v2 proposal — is content-hashing."*
2. §6.4 should drop the "invalidated once" line. Replace with: *"No effect on Z3 cache (content-hashed)."*

The cost of these changes is ~3 lines. The benefit is that v2 stops carrying a phantom defense as if it were load-bearing.

---

## 4. Phase 1 ships unmeasured under v2's own evidentiary standard

§2.1 (talking about v1):

> The v1 Phase 0 benchmark measured 5 small tasks in *test-form* Calor … This cuts both ways. It makes the *opportunity* larger … and the *risk* of "v1's number doesn't generalize" smaller for the symbol-ID compaction question — but it also makes the v1 *recommendation* (drop IDs entirely) less defensible.

§1 (talking about Phase 1):

> **Measured savings (Phase 0, N=5): ~5–9% on test-form sources, concentrated on programs with sub-blocks** (16–18% on `fizzbuzz`/`is_prime`, 0% on `hello`/`add`/`divide`). Effort: ~1 week.

This is the same benchmark, with the same N, on the same five tasks. v2 spent §2.1 explaining why this evidence cannot justify a recommendation. Then in §1 it uses the same evidence to justify Phase 1's recommendation.

v2's defense, in §12.1:

> Phase 1 measurement is still not done. Phase 1 ships on engineering merit (zero identity impact, mechanical change, near-zero rollback cost).

This is a different argument: *don't measure because rollback is cheap.* That's a fine argument on its own. The problem is that it tacitly admits Phase 1 is being shipped on rollback-cost analysis, not on measured benefit — exactly the move that v2's own §2.1 rules out.

The fix is to pick one stance:

- **Option A:** "Phase 1 ships because cost-of-error is near zero; we're not measuring because there's nothing to measure against. The 5–9% savings figure in §1 is illustrative, not justifying." Then drop §1's "Measured savings" framing.
- **Option B:** "Phase 1 ships because cost-of-error is near zero *and* there is a small but real token win. We acknowledge the win is N=5 and offer that as illustrative, not as Phase 1's load-bearing argument." Then re-word §1.

The current §1 wants the framing of evidence-based without doing the evidence work. v1 was attacked for exactly this. v2 should not inherit the bug.

A note on scale that makes this less serious than for v1: Phase 1 *is* genuinely lower-risk than v1's full proposal. The rollback-cost argument really is strong (delete the sub-block IDs, learn nothing breaks, move on). So this is a cosmetic-but-real revision, not a structural problem with Phase 1.

---

## 5. The Phase 2 measurement gate is structured to pass

§10.2:

> Phase 2 ships only if **all four** are true:
>
> 1. Success rate on Phase 1+2 ≥ today's success rate (no regression).
> 2. Identity-preservation errors on Phase 1+2 ≤ today's count (no regression on the property symbol IDs exist to protect).
> 3. **Either:** turn count median on Phase 1+2 < today's median by ≥ 10%, **or** total output tokens median on Phase 1+2 < today's median by ≥ 15%.
> 4. Phase 1+2 result is statistically distinguishable from Phase-1-only (otherwise: ship Phase 1 only and stop).

A measurement gate that always passes is not a measurement gate. Three problems:

**5.1.** **Criteria (1) and (2) are no-regression criteria.** They pass on tie. No-regression is the *floor*, not a positive case for the change. A serious gate would say "success rate increases by ≥ X%" and define X.

**5.2.** **Criterion (3) is disjunctive.** Either turn count OR tokens improving by the stated margin passes the gate. Token output is the easier metric to move (Phase 2 mechanically removes ~20 tokens per ID occurrence; even if agents perform identically, the *output* token count drops just from the syntactic change). Phase 2 satisfies (3) by construction. The gate has no chance of failing on this clause.

**5.3.** **Criterion (4) is unfalsifiable as stated.** "Statistically distinguishable" — by what test? At what α? Powered for what effect size? N=20 tasks × 3 runs = 60 observations per arm. For a binary outcome with a baseline success rate around (let's guess) 70% and a hoped-for improvement of 5–10 percentage points, two-proportion z-test power at α=0.05 is well below 50%. **The gate will frequently fail to detect a real improvement and also fail to detect a real regression** purely from underpowering. The phrase "statistically distinguishable" without a power calculation is decoration.

What a serious gate looks like for this change:

```
Phase 2 ships only if all four are true:
1. Success rate on Phase 1+2 ≥ today's success rate by ≥ 3 percentage points
   (one-sided test, α=0.05, power ≥ 0.80, requiring N ≈ 400 observations/arm).
2. Identity-preservation error rate on Phase 1+2 ≤ today's rate (no regression);
   tested with one-sided non-inferiority margin of 1 percentage point.
3. Median turn count on Phase 1+2 < today's median by ≥ 10%,
   tested with Mann-Whitney U at α=0.05 (token criterion dropped — it's mechanically
   satisfied and adds no information).
4. Effect size on (1) or (3) is at least 1.5× the corresponding Phase-1-only effect size
   (so Phase 2 must show measurable additional benefit beyond what Phase 1 already
   delivers; otherwise Phase 1 alone is the right ship decision).
```

That gate could fail. The current §10.2 gate can't. **A gate that can't fail does not control quality; it just legitimizes the decision the author already made.**

Cost of the fix: rewrite §10.2 with statistically sound criteria, accept that N must increase to make those criteria detectable, re-budget §9.2 to reflect the larger experiment. Maybe an extra week.

---

## 6. The commitment is asymmetric on the wrong axis

§10.2 closing clause:

> v2 commits to revert Phase 2 if shipped and the post-ship data shows regressions on (1) or (2).

§5.7 (Phase 1):

> No dual-mode parser. No multi-release window.

There is no comparable revert clause for Phase 1.

This is backwards. Phase 1 touches every `.calr` file with a sub-block (501 files in the test tree alone, per §2). It rewrites samples, docs, and the language grammar. Phase 2 changes the format of a string in declarations. Phase 1 is the larger surface change with the broader blast radius.

A symmetric commitment would say:

- Phase 1 ships in 0.x+1. If user-reported regression rate in 0.x+1 exceeds {threshold}, revert in 0.x+2 with a feature-flag bridge.
- Phase 2 ships gated by §10's experiment. If post-ship regressions appear, revert.

The hard-break-without-revert posture only works if "no users" is true. v2 §G6 leans on this:

> Single-release hard break. Pre-1.0 envelope allows this (CLAUDE.md is explicit).

"Pre-1.0 envelope" is permission to break, not absence of users. The repo has external sample consumers, a VSCode extension, MCP tool integrations, an evaluation harness, and a documentation site. Each of those is a population that hard-break affects. Asymmetric commitment means the bigger-blast-radius change is the one without the rollback story.

**Fix:** add to §5.7: *"Phase 1 ships in 0.x+1. If feedback in the first two weeks indicates the migrator failed on real codebases, 0.x+2 reintroduces a `--legacy-warn` flag that accepts sub-block IDs with a deprecation warning, extending the transition by one release. This is a contingency, not a plan."* Cost: ~1 hour of writing, ~2 days of code if the contingency is exercised.

---

## 7. The compact ID collision math is hand-waved

§6.1:

> **Format proposal:** 9-character alphanumeric (lower-case + digits, no ambiguous characters): `[a-z0-9]{9}`. Alphabet size 36, total IDs = 36⁹ ≈ 1.0×10¹⁴. **Collision probability for 10⁶ IDs ≈ 5×10⁻³.** Acceptable for an internal codebase; not acceptable for a global registry.

Let me check the math. Birthday-bound approximation for probability of at least one collision with k items drawn from N possibilities: `1 - exp(-k(k-1)/(2N))`. For k=10⁶ and N=10¹⁴:

```
k(k-1)/(2N) ≈ 10¹² / 2×10¹⁴ ≈ 5×10⁻³
P(any collision) ≈ 5×10⁻³
```

The math checks out. The conclusion does not.

**0.5% probability of *some* collision in a 10⁶-ID project is not "acceptable for an internal codebase" for a feature framed as the project's identity moat.** §2.3:

> Symbol IDs deliver: …
> - IR substrate for diff / merge / coordination / memory (the pivot-plan requirement called out by [thesis critique]).

If the IR substrate has a 1-in-200 chance of producing a collision in a sufficiently large project, and the collision silently aliases two distinct entities into one, the consequence is *catastrophic*: the verifier reasons about the wrong entity, the diff layer merges code that shouldn't merge, the agent's memory of "function X" maps to two different functions. The kind of bug that escapes review and surfaces months later.

The fix is cheap. Two options:

- **A: Bump to 12 characters.** 36¹² ≈ 4.7×10¹⁸, collision probability at 10⁶ IDs ≈ 10⁻⁷. That's at the cost of 3 tokens per ID vs Phase 2's proposal.
- **B: Keep 9 characters but specify a collision-detection-and-resolution strategy.** `IdGenerator` checks against a project-scoped existing-IDs set on every generation. Conflict → regenerate. The check is in-memory and O(1) with a hash set.

§6.1's `hash(ulid)[:9]` migration plan has the same issue. Truncating a hash deterministically maps every ULID to a 9-char compact ID, **but truncation collapses the 10⁶ → 10¹⁴ mapping at the same Birthday-bound rate as random generation does.** Without collision handling, the migrator can produce duplicate IDs that the validator will then flag as errors.

The migrator must:

1. Compute the deterministic remap.
2. Detect collisions in the remap output.
3. For each collision, deterministically tie-break (e.g., append `_a`, `_b`, or rehash with a salt).
4. Emit a warning listing which ULIDs were tie-broken, so cross-system references (if any) can be updated.

§6.1 doesn't say. The phrase "deterministic" carries weight it can't hold once collisions appear.

A separate observation, less serious but worth noting: only **9 files in the entire repository contain production-style ULIDs**. The codebase is overwhelmingly test-form IDs (`f001`, `m001`). The "Phase 2 saves ~46% on production form" framing in §15 is calibrated against a form that is, today, essentially absent from this repo. Whether that argues for or against Phase 2 depends on whether the project plans to migrate to production ULIDs broadly — and v2 doesn't say.

---

## 8. The migrator description is internally tense

§5.6:

> Phase 1 uses an *AST-edit-and-print* migrator strategy, not full re-emit. The migrator operates on the source text directly: locate each opening sub-block tag, locate its matching ID-bearing close tag, and surgically remove only the `{…}` block in each. Other source bytes (comments, whitespace, blank lines, ordering) are preserved exactly. **This is implementable as a regex-guided pass anchored on tokens from the lexer, not a parse-and-re-emit pass.**

§12.4:

> Migrator regex-guided edit (Phase 1, §5.6) is not parse-perfect. Multi-line tags split across `\r\n` boundaries, tags inside string literals (rare but possible), and tag-like sequences in comments are edge cases the regex must handle correctly.

§5.6 says "AST-edit-and-print." Then §5.6 says "implementable as a regex-guided pass." Those are different things. The first is parse-based (correct, slower, may not preserve byte-identical comment placement). The second is lexer-based (faster, fragile on the edge cases §12.4 names).

Concretely:

- A **true AST-edit-and-print** migrator parses each `.calr`, mutates the AST, prints back. It is parse-perfect by definition but does not preserve comments unless the AST carries trivia (Calor's AST may or may not — v2 doesn't say). It may also reformat whitespace, depending on how the printer is wired.
- A **regex-guided source-text edit** preserves bytes around the edited region exactly. It must correctly identify each sub-block tag and its matching close tag, despite tag-like sequences in comments and strings. This requires at least a lexer pass to identify tag tokens vs comment/string tokens; pure regex over raw bytes will mis-identify.

v2's phrasing ("regex-guided pass anchored on tokens from the lexer") gestures at the right hybrid (lex first, edit raw bytes within the spans the lexer identifies as tags) but doesn't commit to it. Without committing, the implementer reading §5.6 picks whichever interpretation seems easier today and produces a fragile migrator.

**Fix:** §5.6 should commit explicitly:

> The migrator runs the lexer to tokenize the source. For each token sequence matching an opening sub-block tag with a `{<id>:<rest>}` attribute block, it surgically rewrites the byte range covered by `{<id>:<rest>}` to `{<rest>}` in the original source. For each matching close tag, it rewrites `§/X{<id>}` to `§/X`. The lexer provides token spans; comments and strings are pre-identified and skipped. After rewrite, the migrator re-parses the result and diffs the AST against the AST of the original (with IDs nulled) as a sanity check; any AST mismatch outside the targeted nodes aborts the file with an error.

That's the right design. v2 doesn't quite get there.

---

## 9. The standalone §8.3 quietly trades the cost it was trying to reduce

§8.3:

> **Standalone change recommended by [thesis critique] §"Things the RFC gets right" #3:** the qualified-name diagnostic format is more readable than the ID form regardless of any other change.
>
> ```
> Today:    Calor0501: division by zero in f_01J5X7K9M2NPQRSTABWXYZ12 at file:42
> Better:   Calor0501: division by zero in Calculator.Divide (f_01J5X7…) at file:42
> ```
>
> The ID is retained in parentheses for tooling, the qualified name is shown for humans/agents. ~1-day change. Recommended to ship before Phase 1 as its own small RFC.

§13:

> **Approve §8.3 (diagnostic addressing) as a separate small RFC** to ship before Phase 1.

The "Better" line is *longer*. Adding `Calculator.Divide ` to every diagnostic message increases output tokens by roughly 4–6 tokens per diagnostic, multiplied by the number of diagnostics emitted in an agent session. For a session that hits 100 diagnostics (warning-heavy compile), that's ~400–600 tokens of additional output cost.

v2's central thesis is that token cost matters and should be reduced. Phase 1 saves an estimated 5–9% on source tokens. The diagnostic change adds tokens to every compile output. The net effect on agent token cost — which §10.2 metric (3) actually measures — depends on the ratio of source-tokens-read vs diagnostic-tokens-emitted in a typical session, and that ratio is not measured.

This is not a fatal objection. It is the same class of bug the rest of the review has flagged: an unmeasured token cost being added in a document about reducing token costs. The "1-day, zero risk" framing is honest about effort, dishonest about token impact.

Either drop the "Better" example's longer form (just use the qualified name, drop the parenthesized ID since v2 §6.1 also makes the ID much shorter), or measure the diagnostic-token cost before recommending the change.

---

## 10. Hard break + non-parse-perfect migrator = one user-bricked codebase away from a revert

Putting §5.7 ("hard break") next to §12.4 ("migrator is not parse-perfect"):

- 0.x+1 ships. Legacy form is rejected with `Calor0820`.
- A user runs `calor fix --drop-structural-ids` on their project.
- One file contains a tag-like sequence in a string literal that the regex-guided migrator misidentifies. The migrator either crashes, skips the file silently, or corrupts the file.
- The user can't compile their project, can't rerun an automated migrator, and is one release behind.

The mitigation v1 used (three-release deprecation) was wasteful but it gave the user one release of `--allow-legacy-ids` to fall back to while figuring out the migrator failure. v2's hard break removes this safety net.

Pre-1.0 doesn't mean "no users." It means "users who accepted the breaking-change envelope when adopting." A user who accepted that envelope can still expect that *when* a break ships, the migration path actually works. v2 §5.6 promises the migrator is mechanical and §12.4 admits it isn't quite. The combination is brittle.

**Fix:** add a `--legacy-warn` mode for one release.

- 0.x+1 default: error on legacy form (per current §5.7).
- 0.x+1 with `--legacy-warn` flag: warn on legacy form, accept it. Migrator failures fall back to this flag.
- 0.x+2: flag removed; hard break complete.

This is the [devil's advocate §10 recommendation](./path-2-drop-ids-devils-advocate.md) for v1 applied at a smaller scale. The cost is one parser-level boolean and one diagnostic-severity branch. ~30 LOC. Two more releases of carrying it. Cheap insurance for the case where the migrator fails on a real codebase.

---

## 11. What v2 should add before merge

Concrete, ordered by importance:

**11.1.** Re-count snapshot fixtures. Update §5.5 and §9.1 with the real number. Rebudget Phase 1 timeline. (1 hour.)

**11.2.** Rewrite §5.3 Z3-cache row and §6.4 Z3-cache paragraph to reflect that `ContractHasher.cs` is content-hashed, so the concern doesn't bind in either direction. Stop claiming credit for a non-fix. (~10 lines of text.)

**11.3.** Pick one stance on Phase 1's evidence basis. Either drop "Measured savings" from §1 (replace with "rollback cost is near zero, ship on engineering merit") or re-measure on more than 5 toy programs. (~30 minutes of writing or weeks of measurement; pick.)

**11.4.** Rewrite §10.2 Phase 2 gate with statistically sound criteria. Drop the disjunctive OR. Specify the test, the α, the power. Re-budget §9.2 to reflect the larger N needed. (1 day of stats consultation; 1 week of additional benchmark engineering when Phase 2 actually gets to that point.)

**11.5.** Add a Phase 1 revert/contingency clause to §5.7. (`--legacy-warn` for one release. ~30 LOC and two more release cycles of carrying it as insurance.)

**11.6.** Specify the compact-ID collision strategy in §6.1: either bump to 12 chars, or specify `IdGenerator` collision detection, or both. Specify how the `hash(ulid)[:9]` migration handles colliding outputs. (~15 lines of text plus ~50 LOC in `IdGenerator`.)

**11.7.** Commit to the lexer-anchored hybrid in §5.6 explicitly. Add the post-rewrite AST-diff sanity check. (~10 lines of text plus ~100 LOC in migrator.)

**11.8.** Either drop the parenthesized-ID form in §8.3 (use just qualified name) or measure the diagnostic-token cost before recommending the change. (~3 lines of text or a separate measurement.)

Of these, 11.1, 11.2, 11.3, and 11.5 are pure-writing fixes — a couple of hours of edits. 11.4 and 11.6 require some additional engineering thought but not much. 11.7 is the design commitment that affects the migrator implementation.

**None of these blocks Phase 1 ship.** They block "ship without revision" — they don't block "ship after one editing pass."

---

## 12. What I am *not* saying

I am not saying Phase 1 is a bad idea. It is the cleanest small change v2 proposes and it removes obvious noise. I would ship it after the snapshot recount and the legacy-warn contingency.

I am not saying Phase 2 is a bad idea. It is a well-bounded experiment with a defensible kill criterion structure, even if §10.2's actual criteria need tightening. I would ship it after the gate is rewritten.

I am not saying v2 is dishonest. It is dramatically more honest than v1. The §12 "Honest residual concerns" section is the right convention for any future RFC in this repo. The Z3-cache claim in §5.3 is the only place where v2 takes credit it shouldn't; everywhere else, v2 calls its limits accurately.

I am saying: v2 is closer to merge-ready than to draft-stage. With a focused audit pass — ~2 engineer-days of writing and re-counting — it becomes a document a maintainer can act on confidently. Without that pass, the snapshot estimate, the Z3-cache theater, the unmeasured Phase 1, and the toothless Phase 2 gate together compound into the same kind of "we shipped on optimism" risk v1 was criticized for.

The fix is the audit pass. The verdict is **revise once, then ship.**

---

## Coda

v1 deserved to be sent back. v2 deserves to be sent back *one more time*, for an audit pass that takes a small fraction of the work v2 already represents. The companion [thesis critique](./path-2-drop-ids-critique.md) and [v1 devil's advocate](./path-2-drop-ids-devils-advocate.md) both engaged seriously with v1 and produced v2. This review is the next iteration of the same engagement.

If the author rewrites §1, §5.3, §5.7, §6.1, §6.4, §8.3, §9.1, and §10.2 along the lines in §11 above, v3 is the ship document. That's a one-day rewrite. The improvement from v1 to v2 was much larger. The improvement from v2 to v3 should be much smaller — because v2 already did the hard work.

Approve revisions. Hold ship.
