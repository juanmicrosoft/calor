# Devil's Advocate Review: `path-2-drop-ids-v3.md`

**Target:** `docs/plans/path-2-drop-ids-v3.md`
**Voice:** External technical auditor.
**Stance:** Charitable but firm. v3 is the strongest version of this RFC by a
substantial margin. The job of this document is to find the load-bearing claims
that still do not hold up, so they can be fixed before merge — not to relitigate
what v2 fixed.
**Companion to:** `path-2-drop-ids-devils-advocate.md` (v1 audit),
`path-2-drop-ids-v2-devils-advocate.md` (v2 audit),
`path-2-drop-ids-critique.md` (v1 designer-voice critique),
`path-2-drop-ids-v2-critique.md` (v2 designer-voice critique).

---

## TL;DR

v3 is close to ready and the authors should feel that. They responded to nearly
every concrete objection in the prior two audits: the migrator is a single
coherent strategy now, the collision math is correct, Phase 1 is honestly
reframed as cleanup, the principle narrowing is called by name, the gate is
pre-registered with a real statistical test, and the bundled-release cadence
avoids the two-migration UX failure mode. The verdict on the document as a
whole is **approve with two fixes**, not the rewrite-or-reject of the prior
rounds.

The two fixes are not cosmetic. One is **factual**: §6.4 still describes a Z3
proof-cache architecture that does not exist in this repository. The cache is
content-hashed (SHA-256 of the contract expression and its parameters); it
contains no symbol or obligation identifier; there are no `*.calr.cache` files
to "remap in place" because the cache uses a per-hash `{prefix}/{hash}.json`
content-addressed layout. v3 inherits this error from v2 and adds new fabricated
detail on top of it. The other is **specification**: the AST-diff verifier
described in §5.6 is the safety story for the entire migration, and §13 #4
already admits the equivalence predicate is non-trivial — yet v3 never defines
it. A migrator whose safety is provided by an undefined oracle is not yet a
migrator. Beyond those, the gate threshold (Cliff's δ ≥ 0.2) is set at the
"small effect" mark and the 6-week bundled calendar has no buffer for the gate
failing late. Everything else is small. Fix §6.4 and define the verifier
predicate before code lands and v3 can ship.

---

## §1. What v3 got right

The v2 audit had eleven items. v3 cleanly resolved six of them and made
defensible progress on three more. The improvements are real, not cosmetic, and
the review owes the authors an explicit scorecard before turning to what is
left.

**1.1 Collision math.** v2 proposed 9-character base36 IDs and called a 0.5 %
collision probability "acceptable for an internal codebase." v3 §6.1 + §16.D
move to 12-character Crockford base32 with a generate-until-unique loop. The
collision figures in §16.D reproduce on a calculator: at 10⁶ live IDs, the
9-character base36 design has ~0.5 % probability of at least one collision
(catastrophic for an IR substrate); at 10⁷, it climbs to ~39 %. The
12-character base32 design holds at ~4×10⁻⁷ at 10⁶ and ~4×10⁻⁵ at 10⁷ — a
seven-order-of-magnitude improvement at the project's expected scale. The
generate-until-unique loop makes the collision a non-issue even in the tail.
This is the strongest single change in v3, and it directly answers the v2
audit.

**1.2 Migrator coherence.** v2's §5.6 described the migrator as both
"AST-edit-and-print" and "regex-guided pass anchored on tokens from the lexer."
v3 picks one approach (lexer-anchored text edit, paired with an AST-diff
verifier as the safety net) and stays with it. The migrator is now a single
artifact a single person can own.

**1.3 Phase 1 framing.** v2 framed Phase 1's ~5–9 % token reduction as a win
worth shipping; v3 §4 explicitly reframes Phase 1 as "cleanup with incidental
token savings" and accepts that the measurement is not load-bearing for it.
That is the right honest framing. The v2 audit attacked v2 for shipping Phase 1
unmeasured under v2's own evidentiary standard; v3 sidesteps this by lowering
the rhetorical claim for Phase 1, not by adding measurement. That is a
legitimate move.

**1.4 Principle narrowing is called by name.** v2's §5.3 listed
"Identity-preservation principle" as something the proposal "preserved" — a
piece of rhetoric the v2 audit attacked directly. v3 §3.2 owns that the
principle narrows from "every construct is addressable" to "every declaration
is addressable, and statements are addressable only when addressing is
required." This is the right framing and earns the authors significant credit.

**1.5 Pre-registered measurement.** v3 §10 specifies the statistical test
(Wilcoxon signed-rank for paired turn-count comparisons, McNemar for paired
success-rate comparisons), the per-comparison α (Bonferroni-corrected to
0.0125), the sample size (N=750 runs across 30 tasks × 5 seeds × 5 conditions),
and the pre-registration step (snapshot the protocol before any data is
collected). This is a real measurement plan, not theater. v2 §10.2 had a
disjunctive "either turns or tokens" success criterion with N=60 and no
specified test; v3 fixes all of that.

**1.6 Bundled-release cadence.** v3 §11 ships Phase 1 + Phase 2 + diagnostic
addressing together in the same release. v2 had Phase 1 in one release and
gated Phase 2 for "next release" — which would have meant two migrations for
every user. The bundled cadence is the correct call and addresses a v2-audit
concern the authors picked up unprompted.

**1.7 §8.3 diagnostic addressing promoted to definite.** v2 left this as
"recommended standalone." v3 §8 makes it a first-class deliverable that ships
even if Phase 2 fails the gate. That is the right call: diagnostic addressing
is the only piece of this whole effort whose end-user benefit does not depend
on the principle narrowing.

**1.8 Calor0820 chicken-and-egg surfaced.** v2 buried the diagnostic-during-
edit story; v3 §7.5 says explicitly that `Calor0820` (missing structural ID)
must not fire mid-edit when the ID is being generated lazily — and offers a
concrete suppression rule. That is exactly the kind of operational detail the
v2 audit said was missing.

**1.9 §14 owns the pivot-plan degradation.** The v1 audit raised diff/merge
behavior on sub-blocks as a real cost; v2 acknowledged it; v3 §14 owns the
degradation, characterizes it ("structural sub-blocks lose stable identity
across edits, so the pivot tool falls back to positional matching for
non-declaration constructs"), and commits to documenting the limitation. Not
ideal, but honest.

**1.10 Error-recovery precision listed as a real cost.** v3 §13 #3 admits that
parser error recovery on un-IDed sub-blocks degrades slightly because the
parser loses one of its anchors. This is a small but genuine cost. v2 did not
mention it. Listing it earns trust.

**1.11 Fixture count is accurate.** v3 §11 cites "24 existing fixtures" in
`tests/E2E/agent-tasks/fixtures/`. Verified by directory enumeration: there are
exactly 24 fixture subdirectories. v2's "~30 fixtures" estimate was off by a
factor of ~17 because it conflated fixtures with snapshot test files (501
files, separately). v3 distinguishes them and the new number is right.

---

## §2. The Z3 proof-cache claim is fictional — again

This is the single most important issue in the review and v3 inherits it
verbatim from v2, plus new fabricated detail. §6.4 reads:

> The Z3 proof cache stores entries keyed by `(symbol_id, obligation_id,
> body_hash)`. Both `symbol_id` and `obligation_id` are symbol-level IDs that
> the migrator remaps deterministically when it rewrites the source file. The
> migrator walks every `*.calr.cache` file and updates the keys in place; no
> re-verification is required.

Every clause of that paragraph is wrong in this repository.

**2.1 The cache key contains no IDs.** `Verification/Z3/Cache/ContractHasher.cs`
defines two key-generation methods — `HashPrecondition` and `HashPostcondition`
— and both compute a SHA-256 over the **serialized parameters and contract
expression**, with no field of any kind referring to the symbol that owns the
contract or to an obligation identifier. The cache key is, definitionally,
content-only. Same contract expression with different surrounding symbol IDs
produces the same key. Different symbol IDs with the same contract expression
**already hit the same cache entry today**. v3's claim that the migrator must
remap `symbol_id` and `obligation_id` is remapping fields that do not exist.

**2.2 There are no `*.calr.cache` files.** Directory enumeration across the
entire repository (excluding `bin`, `obj`, `node_modules`) returns zero files
matching `*.calr.cache`. The cache is stored on disk as `{cacheDir}/{prefix}/
{hash}.json`, where `prefix` is the first two hex characters of the SHA-256 and
the filename is the full hex hash — see `VerificationCache.GetCacheFilePath`,
which is one line: `Path.Combine(_cacheDirectory, prefix, hash + ".json")`. v3
describes a file format that does not exist.

**2.3 The cache is unaffected by ID format changes.** Because the cache is
content-hashed on the contract expression and parameter list, and because Calor
IDs do not appear in the contract expression or parameter list, the entire
discussion of cache invalidation, key remapping, and "no re-verification
required" is moot. The cache will hit on the first compile after migration for
any contract whose expression text is unchanged. The cache will miss on the
first compile for any contract whose expression text changes — for example, if
the migration normalizes whitespace inside a `§Q(...)` form. Neither of these
behaviors has anything to do with IDs.

**2.4 v3 made the error worse than v2.** v2 §5.3 listed "Z3 cache key story"
under "Resolved" with no architectural claim. The v2 audit attacked that as
rhetoric. v3 responded by adding **specific false detail** — the
`(symbol_id, obligation_id, body_hash)` tuple and the `*.calr.cache` file
walk — that makes the claim auditable, and the audit fails. This is a
regression in document quality, not a fix.

**2.5 The right paragraph is shorter and correct.** The honest version of
§6.4 is two sentences:

> The Z3 proof cache is content-addressed on the contract expression and
> parameter list. Calor IDs do not appear in either, so the migration does not
> invalidate any cache entries.

That paragraph also happens to be a stronger result for the proposal than the
fictional one — there is literally nothing to do, no migrator code, no walk,
no remap. v3 manufactured complexity to take credit for handling it.

**Recommendation:** Replace §6.4 with the two-sentence form above. Remove the
companion claim in §10's table that says "Cache invalidation: Migrator remaps
cache keys in place. No re-verification." It is repeating the same error.

---

## §3. The AST-diff verifier predicate is undefined

§5.6 step 4 reads:

> After rewriting, parse both the original and rewritten source. Build the
> ASTs. Compare them for structural equivalence. If they differ in any way
> other than removed structural IDs, fail the migration and report the
> location.

§13 #4 then says:

> The "structural equivalence" predicate is non-trivial. Whitespace, comments,
> and the structural ID nodes themselves must be ignored; everything else must
> match. Defining this predicate precisely is part of Phase 2 implementation.

This is the safety story for the migration. Every claim of the form "the
migrator is safe because the verifier catches deviation" depends on this
predicate being correctly defined. v3 ships the safety story before the
predicate exists, which is the wrong order.

**3.1 The predicate space is larger than "ignore IDs."** A complete enumeration
of what the predicate must say:

- **Structural ID nodes ignored.** This is the obvious case.
- **Whitespace and comments ignored.** Stated in §13.
- **Trivia attached to comparable nodes must be reattached compatibly.**
  Comments attached to a `§IF` block before migration must reattach to the
  same `§IF` block after migration. The migrator removing `{if1}` may
  change which comment binds to which node if the parser uses anchor-based
  trivia attachment (which the legacy parser plausibly does).
- **ID references inside diagnostic text or proof annotations.** Some
  contracts reference structural IDs in messages or in proof scaffolding. If
  the predicate naively ignores all `IdNode` instances, it may also ignore
  intentional ID references that were not supposed to change.
- **Reordering inside collection nodes.** If the migrator's text rewrite ever
  causes a slight statement-ordering change inside a block (e.g., due to
  multi-line `§IF` blocks being collapsed), the predicate must catch that.
  Whether it catches it depends on whether the predicate compares ordered or
  unordered children.

**3.2 "Defining this predicate precisely is part of Phase 2 implementation"
is the wrong sequencing.** Phase 2's measurement gate (§10) assumes the
migrator has run on all 24 fixtures + 501 snapshot tests + every source file in
the project before the gate experiment begins, because the gate experiment
runs agents against migrated source. If the verifier predicate has a bug, the
gate runs against silently-corrupted code and the gate result is unreliable
regardless of what it shows. The predicate must exist and be itself reviewed
before any migration runs anywhere.

**3.3 The predicate is also the right place to specify failure modes.** What
happens when the predicate detects a difference at a single site in a single
file? Does the migrator skip that file? Skip the project? Abort the run? v3
says "fail the migration and report the location," but does not say at what
granularity. Per-file is too granular (you ship a half-migrated codebase);
per-project is too coarse (one bad file blocks everything). The right answer is
likely per-file with a project-level abort if any file failed, but v3 does not
say.

**Recommendation:** Add a §5.7 (numbered after the existing §5.6) titled
"AST-diff verifier specification" that enumerates the predicate clauses
exhaustively, lists the trivia-reattachment cases the verifier is required to
catch, and specifies file-vs-project failure semantics. Reviewers can then
audit the predicate against the AST node taxonomy. Ship this before any
migrator code.

---

## §4. The 6-week bundled calendar has no buffer for late gate failure

§11 budgets Phase 1 + Phase 2 + diagnostic addressing as a 6-week bundle in
the 0.x+1 release. The gate experiment (§10) runs for 3–4 calendar days at the
end and consumes ~$450 of LLM credits. The implicit sequencing is:

1. Weeks 1–2: Phase 1 implementation (migrator, lexer/parser, snapshots
   regenerated).
2. Weeks 3–4: Phase 2 implementation (sub-block ID removal, IdRegistry, lazy
   generation, Calor0820 suppression).
3. Week 5: Diagnostic addressing format.
4. Week 6: Gate experiment + release prep.

The risk: the gate experiment runs in week 6 against a release branch that
includes both Phase 1 and Phase 2. If the gate fails — and pre-registered
experiments fail more often than people expect, because the criterion was
agreed in advance and can no longer be rationalized away — the team must
decide between:

- **Revert Phase 2 from the release.** This means re-cutting the release branch
  to exclude Phase 2's changes, re-running CI on the reverted branch,
  re-validating that Phase 1 still works without Phase 2's IdRegistry, and
  shipping a release that was advertised as the principle-narrowing release
  without the principle narrowing. The "bundled cadence" advantage disappears
  the moment this happens.
- **Delay the release.** The team has presumably already announced the cadence;
  delay has organizational cost.
- **Ship Phase 2 over the gate's objection.** This is the failure mode the
  pre-registered gate exists to prevent. If the team ships anyway, the gate is
  cargo cult.

**4.1 The right hedge is to run the gate earlier.** If the gate runs in week 4
on a Phase 2 prototype against Phase 1 production, the team has two weeks of
buffer to revert, redesign, or escalate. v3's week-6 placement is the
worst-case placement.

**4.2 The cost of running the gate twice is small relative to the cost of late
revert.** $450 × 2 = $900 and 7 calendar days is a tiny insurance premium
against a botched release. v3's "$450 budget" framing treats the gate as a
one-shot deliverable. It should be treated as a measurement instrument that may
need to be re-run.

**4.3 Six weeks for a change of this scope is optimistic in the absolute.**
Comparable refactors in this repository have a track record. The class-member
binder work
(`implementation-summary-binder-class-members.md`,
`extend-binder-to-class-members.md`) consumed substantially more than six weeks
of calendar time. Phase 1 + Phase 2 + diagnostic addressing + gate + release
prep with no buffer is an aggressive budget. v3 should add a 1–2 week buffer or
descope.

**Recommendation:** Move the gate experiment into week 4. Add a week-5
contingency block. Treat the gate budget as $900 (two runs) not $450 (one run).

---

## §5. Cliff's δ ≥ 0.2 is the "small effect" threshold

§10.3 specifies the gate's effect-size threshold:

> Phase 2 ships if and only if both: (a) Wilcoxon signed-rank test on turn
> counts shows p < 0.0125 (Bonferroni-corrected), and (b) Cliff's δ ≥ 0.2.

Cliff's δ of 0.2 is the standard threshold for a **small** effect. The standard
thresholds in the literature are:

- δ < 0.147: negligible
- 0.147 ≤ δ < 0.33: small
- 0.33 ≤ δ < 0.474: medium
- δ ≥ 0.474: large

v3 sets the bar at the lower edge of "small." Combined with N=750 runs, the
test will reliably detect effects this small and ship Phase 2 on the strength
of a small effect.

**5.1 The proposal is the principle narrowing, not a parameter tweak.** Phase 2
narrows the addressability principle of the language. That is a one-way door
for the language's identity story — the v1 designer-voice critique attacked
this explicitly and v3 §3.2 owns it. A one-way door is not a small-effect
change. The bar to walk through it should be at least "medium effect," i.e.,
δ ≥ 0.33.

**5.2 A small effect on agent runs of ~30 turns is ~3 turns saved.** Agent
turn counts in the gate experiment are likely to be in the 20–40 range
(based on `tests/E2E/agent-tasks/tasks/` — the corpus has 258 task files of
typical agent complexity). A small effect on a 30-turn run is ~3 turns. Three
turns saved per task at the cost of permanent principle narrowing is a thin
trade. The team should ask whether that is what they want to ship.

**5.3 The threshold was lowered without explanation.** v2 §10.2 used a
disjunctive 10 % / 15 % criterion. v3 moved to a per-test framework, which is
better, but lowered the practical bar to δ ≥ 0.2, which is weaker than v2 in
expected value. The document does not justify the lowering.

**5.4 The honest version of the criterion makes the bar higher.** If the
authors believe the principle narrowing produces a real improvement, they
should be willing to bet on δ ≥ 0.33. If they are not, the proposal is asking
the project to take a one-way door for a small expected gain.

**Recommendation:** Raise the gate threshold to Cliff's δ ≥ 0.33. Document the
rationale in §10.3. Accept that this may require larger N or more tasks if the
true effect is small — that is the correct trade for a permanent change.

---

## §6. `IdRegistry` thread-safety is unaddressed

§6.3 introduces an in-memory `IdRegistry` consulted by:

- `IdGenerator` (writes), when allocating a new ID.
- `IdScanner` (reads), when validating uniqueness.
- The compiler's symbol-binding pass (reads), when looking up an ID by symbol.

The repository's build is parallel — `dotnet build` runs project compilations
concurrently, and inside the compiler there is parallel binding for
independent compilation units. The registry is shared mutable state.

v3 says nothing about concurrency. The options are:

- **`ConcurrentDictionary`-based registry.** Simple but the generate-until-
  unique loop requires `TryAdd` with retry on conflict. This must be specified
  because the loop's correctness depends on it.
- **Lock-per-project registry.** Works for parallel project compilation but
  serializes within-project parallel binding.
- **Lock-free with CAS retry.** Highest performance but requires careful design.

The choice has implications: a registry that serializes ID generation across
threads can dominate build time for large projects with many declarations.

**6.1 The generate-until-unique loop is the concurrency hotspot.** Every ID
generation reads the registry, generates a candidate, and writes back if the
candidate is unique. Under concurrent generation, the loop will retry on
collision; under high collision rates (which v3 §6.1 establishes are low) the
amortized cost is fine, but the registry's lock granularity determines whether
that is true in practice.

**6.2 Persistence semantics across compile sessions.** Is the registry
project-scoped (rebuilt per compile) or persisted (loaded on compile start
from a manifest)? v3 implies persistence (the IDs must be stable across
compiles for diagnostic addressing to work), but does not say where the
manifest lives or how it is updated.

**Recommendation:** Add a §6.3.1 covering the registry's concurrency model
and persistence layer. Specify whether the registry is per-project or
per-source-file, where it is persisted on disk, and what the locking discipline
is. If the registry persists, also specify what happens when two compile
sessions modify it concurrently (writer locks, last-writer-wins, etc.).

---

## §7. Phase 1 still ships unmeasured, by acknowledgment

§13 #1 reads:

> Phase 1's token reduction is not separately measured. We rely on the
> Phase 0 micro-benchmark (N=5) for the headline figure. The gate experiment
> measures Phase 1+2 jointly. If Phase 2 fails the gate but Phase 1 is
> already shipped, we lack a direct measurement of Phase 1's contribution.

v3 acknowledges this and accepts it on the basis that Phase 1's risk surface
is low and its reversibility is high (§9.2). The v2 audit attacked this exact
shape under v2's own evidentiary standard.

**7.1 The bundled cadence resolves the user-facing version of the problem.**
If Phase 1 and Phase 2 ship together, the user sees one migration and either
the bundle's benefit is real (gate passes) or it isn't (gate fails, Phase 2
gets pulled, user is left with Phase 1's cleanup and the smaller token
savings). The bundled cadence is the right call for the user.

**7.2 The internal acceptance criterion is still missing.** What does "Phase 1
shipped successfully" mean operationally? The team needs an answer to "what
metric would have to regress for us to revert Phase 1?" Without one, the
revert decision in §9.2 is judgment-only. v2 had the same gap; v3 inherits
it.

**7.3 The fix is small.** Add to §10 a Phase-1-specific post-ship monitoring
clause: "Within 30 days of 0.x+1 ship, we will compare turn counts on the
agent-task corpus against the most recent pre-release tag. If turn counts
regress by Cliff's δ ≥ 0.2, we will revert Phase 1." That gives Phase 1 the
same evidentiary discipline as Phase 2.

**Recommendation:** Add the post-ship Phase 1 monitoring clause to §10. The
authors already have all the infrastructure in place from the Phase 2 gate.

---

## §8. The pre-registration retry budget is missing

§10.4 budgets the gate experiment at $450 (~3,750 turns × $0.12/turn average)
and 3–4 calendar days. The budget is single-shot.

Pre-registered experiments fail in two distinct ways:

- **Gate criterion fails.** The proposal does not ship. This is the intended
  function of the gate and is not a "retry" — it is the outcome.
- **Protocol violation detected mid-run.** A task crashes for unrelated
  reasons, an LLM provider has an outage, the seed-randomization wasn't
  properly isolated between conditions, the task corpus changed during the
  run, etc. Pre-registered protocols typically require the experiment to be
  re-run from scratch with a fresh pre-registration to avoid post-hoc
  data-massaging.

v3 §10 does not distinguish these two cases and budgets for neither
contingency. If the experiment hits a protocol violation in week 6, the team
has zero buffer.

**Recommendation:** Add a §10.5 covering the retry policy. Specifically: what
triggers a re-run, who signs off on the re-run, what the budget cap is before
the proposal is escalated. A reasonable starting point is "one retry covered
by an additional $450 budget; further retries require escalation."

---

## §9. The migrator's lexer ambiguity

§5.6 step 1 reads:

> Tokenize the source file using the existing lexer. The lexer produces
> tokens for `§IF{if1}`, `§/I{if1}`, etc., which the migrator can locate by
> token kind and ID attribute.

The phrase "the existing lexer" is ambiguous. Phase 1 modifies the lexer (§4
adds new token forms for compact IDs and removes the special-case handling for
sub-block ID requirements). So "the existing lexer" could mean:

- **The pre-Phase-1 lexer.** The migrator carries the legacy lexer as a
  private helper. This is the safest interpretation but means the migrator
  links in two lexers (legacy and new) and the team maintains both for as
  long as the migrator ships.
- **The post-Phase-1 lexer.** The new lexer must tolerate legacy input — i.e.,
  it must continue to recognize `§IF{if1}` as a valid token sequence even
  after Phase 1 has dropped the requirement. This is plausible if the new
  lexer simply treats `{...}` as an attribute block at the token level
  regardless of whether the surrounding construct requires it, but v3 does not
  say so.

The distinction matters operationally:

- If the migrator uses the legacy lexer, the migrator's build dependency is
  larger and the legacy lexer must be kept compileable past Phase 1's ship.
- If the migrator uses the new lexer, the new lexer must be specified to
  accept-and-discard legacy ID attributes on sub-block constructs, which is a
  small but specific lexer requirement that should appear in §4.

**Recommendation:** Disambiguate in §5.6 step 1. Either explicitly say "the
pre-Phase-1 lexer, carried as a migrator-private helper" or "the new lexer,
which is required by §4 to accept legacy `{id}` attributes on sub-block
constructs and discard them." Whichever is chosen, document the resulting
requirement on the other phase's design.

---

## §10. Things v3 should add before merge

Concrete edits, in priority order. Most are hours of writing.

**10.1 (Hard requirement.)** Replace §6.4 with the two-sentence form in §2.5
of this audit. Remove the corresponding row from §10's table. This is a
factual correction, not a design change.

**10.2 (Hard requirement.)** Add §5.7 specifying the AST-diff verifier
predicate. Cover at minimum: ignored node kinds, trivia reattachment, ID
references inside diagnostic text, collection-order semantics, and per-file
vs per-project failure granularity.

**10.3 (Strongly recommended.)** Raise the Cliff's δ threshold in §10.3 to
0.33. Document the rationale.

**10.4 (Strongly recommended.)** Move the gate experiment from week 6 to
week 4 of §11's calendar. Add a week-5 contingency block. Re-budget the gate
as $900 (two-run reserve).

**10.5 (Recommended.)** Add §6.3.1 specifying `IdRegistry` concurrency and
persistence.

**10.6 (Recommended.)** Add a post-ship Phase 1 monitoring clause to §10
(see §7.3 of this audit).

**10.7 (Recommended.)** Add §10.5 covering the gate retry policy.

**10.8 (Recommended.)** Disambiguate the lexer in §5.6 step 1.

None of these require redesign. All can land in a single document-only PR
followed by a Phase 1 implementation PR. If §6.4 and §5.7 land before any
code, the migrator can be reviewed against a correct architectural picture
and a defined safety predicate, which is the right starting condition.

---

## §11. What I am not saying

This audit is significantly narrower than the v1 and v2 audits because v3 is
significantly closer to ready. Things the audit deliberately does not say:

- v3's collision analysis is wrong. It is correct; the reproduction in §16.D
  matches a calculator and the generate-until-unique loop closes the residual
  gap.
- The migrator strategy is incoherent. It is coherent and ownable.
- The principle narrowing is rhetorical. v3 calls it by name.
- Phase 1 should not ship without measurement. v3's bundled cadence and
  honest framing make this acceptable — provided the post-ship monitoring
  clause from §7.3 lands.
- The gate is theater. The gate is real, pre-registered, statistically sound
  on N and α, and properly paired across conditions. The only critique of the
  gate is that the effect-size bar should be at the medium-effect threshold,
  not the small-effect threshold.
- The proposal should be rejected. The proposal should ship with the two hard
  requirements in §10.1 and §10.2 satisfied.

The verdict, plainly: **approve with two fixes.** Fix §6.4 to match the
repository. Define the verifier predicate. Then merge. The remaining items
(§10.3–§10.8) are improvements the authors will likely want anyway and can
land in the implementation PRs rather than the spec PR.

---

## §12. Coda

The trajectory v1 → v2 → v3 is the right shape for an RFC. v1 was a thesis
with too much philosophy; v2 was a working proposal with rhetorical sleight-of-
hand; v3 is a working proposal that has internalized the prior critiques and
made the right tradeoffs in nearly every place the critiques pointed. The two
remaining issues — the cache fiction and the undefined verifier predicate —
are both fixable in document edits before any code is written.

A v4 is not necessary if v3.1 lands the §10.1 and §10.2 fixes. If the team
ships v3 as written, the cache claim will get caught in code review of the
migrator (because the reviewer will go looking for the `*.calr.cache` walk and
not find one) and the verifier predicate will get caught the first time the
verifier flags a false positive. The cost of catching them now in the document
is a few hours of writing; the cost of catching them in implementation is a
spec-to-code mismatch that will erode confidence in the rest of the document's
claims. The right place to fix this is in the document, before code starts.

The authors have done good work on v3. The audit hopes they will accept the
two fixes as the price of the third version landing.

---

*End of v3 audit. Prior audits: `path-2-drop-ids-devils-advocate.md` (v1),
`path-2-drop-ids-v2-devils-advocate.md` (v2). Companion designer-voice
critiques: `path-2-drop-ids-critique.md` (v1), `path-2-drop-ids-v2-critique.md`
(v2).*
