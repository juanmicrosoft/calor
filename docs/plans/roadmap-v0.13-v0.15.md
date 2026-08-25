# Roadmap — v0.13 / v0.14 / v0.15

**Date:** 2026-08-10
**Status:** Draft v4 — Draft v3 (2026-08-10, two adversarial rounds, §8–§9) with §4 rebuilt and
§5.2 refreshed against the source at v0.14.3 on 2026-08-24, then revised under three adversarial
lenses (evidence, consistency, test-lens) — review round 3, §10. §1–§3 are the historical record of
0.13/0.14 as planned; §4.0 is the measured inventory of what they left; §4.5 names what §4 supersedes.
**Governing inputs:** `call-s-adjudication.md` (the v0.12 → v0.13 gate: "v0.13 leads with product, not
measurement"; quantitative re-entry conditions), `calor-direction.md` (safety direction commitment,
including its TIER2D design-doc requirement), `substrate-plan-v0.12.md` §9 (semantic index flagged at
third consecutive deferral), `tier1a-postmortem.md` §6 (the registered flow-checker experiment),
`verification-modeled-forms.md` (the modeled-forms whitelist), and the open-issue backlog (#760–#793
audit epic and successors — dispositioned exhaustively in §5.2).

---

## 0. Where the last releases left us

- **v0.9** — the agent loop became materially cheaper and faster (PP-L5 HIT).
- **v0.10** — verification became honest: explicit seven-status vocabulary, assumptions, vacuity,
  body-aware proofs, guard retention. PP-G3 HIT (earliness/attribution/counterexamples) — at
  **fixture scale only**.
- **v0.11/v0.12** — effect enforcement **default-on** (fail-closed at the manifest/`calor import`
  surface; walker-level enforcement was still fail-open at the time — #785 closed it on
  2026-08-13 via PR #968, see §4.0); `calor
  import`, `calor review-packet`, type checker default-on; seven false-`Proven` elide vectors closed;
  conversion honesty (loss ledger, FeatureSupport census).
- **Call S** — the real-scale measurement venue is **retired**. Supply was 8 tasks against a bar of
  70; the census established converter fidelity is structural at v0.12 maturity (top-3 causes 40.4%
  across 33 causes), and the epoch's "Calor arm" contained no Calor. Re-entry requires **both** a
  repaired supply story and an arm that actually invokes the Calor compiler.
- **v0.12.1** — language-equivalent to v0.12.0; packaging remediation. NuGet is live and verified,
  including the **first `Calor.Sdk` publish** (a v0.12.1 event — v0.12.0 was never installable). The
  VS Code extension remains a supported built artifact; the Marketplace listing is historical and
  updates only opportunistically (decision amendment in §2.4).

The published 1.32× benchmark number remains a **regression indicator, not a roadmap driver** — its
30 runs are deterministic repetitions and the real-scale comparison never produced a valid Calor arm.

## 1. The staircase

| Version | Theme | Core outcome |
|---|---|---|
| **0.13 — Trustworthy Project Model** | Make Calor usable — and honest — at project scale | MUST: total binding, stable SymbolIds, reliable builds, delivery/docs surface. SHOULD (§2.2): persistent semantic index, `calor query` |
| **0.14 — Null-Safe .NET** | Make the type system's safety claims real across interop | Metadata-aware binding, enforced non-null references, typed CFG null-state, explicit nullable boundaries — `§SEMVER{2.0.0}` with a self-migration workstream |
| **0.15 — Composable Effects** | Remove the first-order ceiling; earn the claim back honestly | Effect-typed functions, safe delegates/lambdas, effect polymorphism behind a design gate, project-wide impact analysis |

**Sequencing, stated precisely.** The load-bearing prerequisite chain is narrow: **#762 (total
binding) + stable SymbolIds** are what 0.14's metadata-backed binding and 0.15's effect rows stand
on. The persistent index, `calor query`, and the LSP re-platform are 0.13 *product* — consumers of
the spine, deferrable without breaking 0.14/0.15. The plan says this openly so that if 0.13
overruns, the cut falls on the deferrable product (per the §2 cut lines), not on the spine — the
repo's precedent is that overruns get folded into the next version rather than cut (there is no
v0.11.0 tag), and folding the spine would put the 0.14 semantic break on top of an unshipped
foundation.

The deliberate ordering of the 0.14 semantic break *before* the 0.15 adoption push is a choice, not
an accident: break the language while its only users are this repo and its dogfooded utility, then
invite adopters onto 2.0.0 semantics.

**Draft v4 amendment to §1 (2026-08-24).** The 0.14 row's "self-migration workstream" did not
execute (no committed `.calr` declares a version; §4.0, §4.5, #1084), and the load-bearing chain for
0.15 is not only #762 + SymbolIds: effect rows additionally stand on the never-merged metadata-binding
S6, now §4.2 E1. The rest of this section is left as written.

---

## 2. v0.13 — Trustworthy Project Model

**Thesis:** an agent should be able to ask Calor what a project means — symbols, callers, contract
outcomes, assumptions, change impact — and trust the answer. "Trust the answer" extends to the
delivery chain and the published docs.

Scope is tiered. MUST items gate the release; SHOULD items ship if they fit and defer to 0.13.x
without renegotiation; DEFERRED items are named now so their absence is a decision, not a silence.

### 2.1 MUST — the spine and the truth floor

- **Structurally total binding (#762)**: every accepted expression retains type, children, and
  symbol identity, or produces an explicit "analysis incomplete" result, eliminating the zero-child
  `<unsupported:...>` fallback. #762's definition-of-done is deliberately universal (100% of
  accepted expression kinds); the release gate's bounded denominator (§2.5 gate 1) is a
  construct-class list that **does not yet exist as a pin** — registering it (a gate-precision
  sharpening of the issue's "Required implementation" families: conversion, type/pattern tests,
  arrays/indexes, collections, lambdas, await, coalesce, match, quantifiers, string operations,
  interop) is 0.13's first to-do, frozen **before binder work merges**. This is a binder rebuild,
  not a fix — today `BindExpression` handles ~20 of 60 concrete expression node classes — and it is
  the single item 0.14 and 0.15 cannot proceed without.
- **Stable bound SymbolIds + exact identifier spans.**
- **Truth-floor defects**: structural return/postcondition lowering (#764), keyword-arg verifier
  crash (#874), method elision cursor / wrong function id (#879), incremental IL-analysis hashing
  (#883), and **exhaustive semantics-versioned verification cache keys (#778)** — moved into 0.13
  from the original 0.14 slot because the full-vs-incremental identity gate (§2.5) and
  review-packet's "invalidated proofs" facet are unsound without trustworthy cache keys.
- **Guard-elision policy: resolved now, by decision rather than by gate.** Proof-based guard elision
  becomes **explicitly opt-in** in 0.13; verification remains diagnostic by default. *(Default-on
  since PR #1088, 0.15.0 — the condition below was met.)* The
  solver-vs-runtime differential suite (#779) is then built as a *program* rather than a release
  blocker: its denominator is the **`verification-modeled-forms.md` whitelist pinned by content
  hash** (not "whatever currently elides" — the shipped closure mechanism is demotion to `Assumed`,
  which never elides, so an elides-today denominator is shrinkable to green forever), exercised by a
  pre-registered generator (all whitelisted forms × both contract positions × bounded nesting
  depth), with the D15 emitter-lowering class explicitly in scope (D15 itself is the seventh closed
  vector, fixed before it shipped — what stays open is its *class*), the **`§PROOF`
  obligation-elision path in the denominator** (obligations elide on `ObligationStatus.Discharged`,
  a third route outside the contract-guard path), and the **elision-coverage fraction (forms
  eliding ÷ forms whitelisted) published** so demotion-shrinkage is visible.
  Elision re-enables by default only when that gate is green — target 0.14 (§3.4).
- **PP-S4 fixture registry — a release gate, not a bullet** (§2.5), discharging the Call S debt at
  all three of A-1.5.7's frozen parts: location, **schema**, and CI entry point.

### 2.2 SHOULD — project-model product

- **Persistent semantic index** under `obj/calor/`: declarations, occurrences, call graph, contract
  outcomes, assumptions, semantic hashes. (Third-deferral item from substrate-plan §9 — executed,
  not deferred a fourth time.)
- **`calor query` v1**, facets scoped to what the 0.13 compiler can answer *soundly*: symbol /
  callers / callees / contract-outcomes / assumptions / impact. **The effects facet is excluded from
  v1** (or hard-marked `Assumed` if exposed): today's effect resolution string-guesses receiver
  types — exactly what 0.14 replaces and what #785 (0.15) makes symbol-resolved — and a release
  titled "trustworthy" does not ship a query facet the same roadmap declares unsound two sections
  later. Effects join the index in 0.15 (§4.2), derived from 0.14's typed signatures.
- **`review-packet` reads the index**: a changed declaration reports affected callers, invalidated
  proofs, unproven residuals (effects join later, as above).
- **Website adoption surface, first tranche**: `cli/verify.mdx` and the verdict-vocabulary page
  (which statuses elide, which never do). **Public soundness log**: the seven closed elide vectors
  with their `run` vs `--verify` reproductions and the explicit "surface not exhausted" statement.
- **Dogfood utility**: one nontrivial in-repo utility as real `.calr` built through `Calor.Sdk`,
  enforced mechanically, not aspirationally — generated C# untracked (gitignore + CI assertion that
  no generated output under the utility is committed), built from `.calr` via `Calor.Sdk` in CI, and
  PRs touching the utility touch only `.calr`. In 0.14 this utility becomes the first migration
  subject of the 2.0.0 migrator (§3.3); if it defers to 0.13.x under the SHOULD rule, landing it
  becomes a **0.14-entry precondition** — the migrator needs its subject.
- **Instrument debt**: the Z3 CI-native flake (#884/#859); telemetry docs re-verification (#792 is
  already **closed** — the code is verified opt-in via `CALOR_TELEMETRY=1`; the residual is
  re-checking the docs inventory against shipped payloads, not a reopening).

### 2.3 DEFERRED from 0.13 — named, with destinations

- **LSP re-platform (#765) + LSP hardening (#767)** → 0.13.x/0.14. The LSP is a *consumer* of
  SymbolIds and a prerequisite to nothing downstream; it rides once the spine exists. The rejected
  declaration-addressed editing program stays rejected.
- **MCP query surface** → ships with or after the LSP re-platform, same reasoning.
- **`cli/import.mdx`, `cli/review-packet.mdx`, adoption-playbook mirror** → 0.13.x docs tranche 2.

### 2.4 Delivery chain

- **Decision amendment — 2026-08-11 (supersedes this section's original VSCE_PAT escalation
  rule).** Marketplace publishing is demoted from a release commitment to an opportunistic
  channel. The extension and
  LSP remain first-class investments: every release builds six platform VSIX packages, attaches
  them to the GitHub release, and keeps the PR `vsix-single-file-publish` guard. The Marketplace
  listing deliberately remains at v0.3.8 unless a publisher token is minted and a maintainer
  chooses to publish. Revisit only on a demand signal: a user issue requesting Marketplace
  installation or download counts that justify the recurring token and publish-chain maintenance.
  Rationale recorded rather than inferred: publishing has failed since v0.4.0, five months of a
  stale listing produced zero complaints, and Calor's premise needs the LSP for agents and human
  reviewers but does not require Marketplace freshness.
- **Z3 asset chain finished**: resolve the mislabeled `osx-x64` dylib (upstream ships an arm64
  binary under the x64 label — Intel Macs install successfully and *silently lose verification*),
  switch `build-z3.yml`'s `osx-arm64` leg off the source build, then land the native arch-vs-RID
  assertion that is deliberately held back until those two are decided. One repin/republish.
- The v0.12.1 changelog correction **already landed** (PR #887, Unreleased section) and is not 0.13
  work; it is cited here so the record's numbers (every release since v0.4.0, not "three") are the
  ones this plan carries.

### 2.5 Release gates — each names instrument, denominator, and freeze point

Gates on SHOULD-tier deliverables are **conditional**: if the deliverable defers to 0.13.x under
§2's rule, its gate moves to 0.13.x with it — deferral is a tier decision, not a gate failure.
Conditional in this sense: gate 2's index and review-packet legs, gate 3, and gate 8. Unconditional:
gates 1, 4, 5, 6, 7, and gate 2's diagnostics leg.

1. **Binding totality**: on a pinned measurement corpus (in-repo `.calr` fixtures + the three
   A-1.5.3-pinned conversion subjects), **zero analysis-incomplete occurrences (`Calor0259`) for
   the registered construct-class list** (F-1; the marker moved from the silent `<unsupported:>`
   tree string to a counted diagnostic by F-2's 2026-08-10 additive amendment — same denominator,
   strictly better instrument). The escape category is thereby bounded: the gate cannot be
   passed by marking registered constructs "incomplete". **Status: measured green after the
   PR #900 structural-totality completion (2026-08-11)** — all 60 expression classes are
   registered Tier A, with zero `Calor0259` occurrences on both recorded F-2 legs
   (in-repo 0 / 4,612 bound expressions; conversion 0 / 19,348).
2. **Full-vs-incremental identity**: byte-identical diagnostics, index contents, and review packets
   (after canonical ordering) across a **registered edit-script corpus** that includes the #883
   reproduction — plus an **incrementality witness** (the incremental path demonstrably reuses the
   index; a silent full rebuild fails the witness, so identity cannot be bought by never being
   incremental).
3. **Index/query correctness**: a golden query-answer corpus (callers/callees/impact ground truth on
   a pinned project) as a CI gate. Identity-between-modes (gate 2) alone would pass an
   identically-wrong index; this gate is the correctness anchor for §7's "queryable project model"
   claim.
4. **Rename**: edits target exact identifier tokens and survive **apply-recompile-and-test** (a
   behavior oracle, not compile-success — capture/collision renames compile) on a pinned rename
   corpus including shadowing cases. Instrument: a harness command applying SymbolId-addressed
   renames, shipping with the spine — the deferred LSP (§2.3) later consumes the same identities;
   this gate does not wait for it.
5. **Differential program (elision opt-in; default-on since PR #1088)**: the #779 suite exists, runs in CI against the pinned
   `verification-modeled-forms.md` denominator, and the elision-coverage fraction is published.
   Zero-mismatch is the 0.14 re-enable bar, not a 0.13 blocker (§2.1).
6. **PP-S4 registry**: exists at its A-1.5.7-registered location, schema, and CI entry point, and
   the CI job is **demonstrated to fail on a fixture-less `SupportLevel` promotion** (a
   discriminating pin — revert the fixture, watch it fail).
7. **Clean-consumer install, per-RID**: on a frozen RID × artifact matrix (win-x64, linux-x64,
   osx-arm64: CLI + Sdk + release VSIX), a clean consumer runs `calor verify` on a
   Z3-requiring fixture and gets a **solver verdict** — exit-0 install is not the bar, because the
   pre-closure osx-x64 state was precisely "installs successfully, silently loses verification".
   **Gate amended with the Z3 chain closure (2026-08-11, #916 review F3): the osx-x64 RID is
   dropped, so its leg's oracle changes rather than disappearing** — a clean Intel-mac consumer
   must get the *documented degradation* (a loud "Z3 unavailable"/Calor0710 signal, no crash, no
   silent pass), which turns the drop decision itself into a tested claim. NuGet registries and
   GitHub release assets are verified **after** publishing (a workflow firing is not a publish).
8. **Performance envelope (project scale needs a number)**: index build ≤ 30s and warm `calor query`
   ≤ 500ms on the largest pinned conversion subject, measured in CI. Generous by design; the point
   is that "usable at project scale" is adjudicable at all. Frozen here, before the index exists.

## 3. v0.14 — Null-Safe .NET

**Thesis:** if Calor says a value is non-null, that must remain true even when it crosses a C# or
NuGet boundary.

This is a genuine semantic break for reference types: it ships as **`§SEMVER{2.0.0}`** even though
the compiler stays pre-1.0 — and a break is only honest if the plan budgets for breaking *itself*
(§3.3).

### 3.1 Entry spike — metadata-backed binding is the risk, so it gets the gate

The component that can sink 0.14 is not the flow checker (which has the TIER1A gate) but
**metadata-backed .NET binding** — exact receiver type, overload resolution, parameter/return
types, generic substitution, nullable annotations — a slice of Roslyn's binder, and the exact area
where this repo's own postmortem records systematic underestimation (tier1a-postmortem §7.1 V4:
overload resolution — 18 overloads on `Console.WriteLine` — absent from the original estimate; V3's
"3–5 days, not 1–2" spike-size correction is the same lesson on the adjacent TIER2D spike). Before
0.14's gates freeze:
bind a **pre-registered set of BCL call shapes** (generic substitution, extension methods, `params`
arrays, nullable-annotated signatures, `ref`/`out`) through real metadata. **Exit ramp, planned
rather than accidental**: ship exact-signature binding for the resolved subset with explicit
fail-safe "unresolved" for the rest — §3.5's resolution gate is already phrased to permit exactly
that degradation.

### 3.2 Ship

- **Metadata-backed .NET binding** replacing string-based type guesses (per the §3.1 spike).
- **Typed semantic representation** shared by binder, type checker, CFG, verifier, effect resolver.
- **Typed CFG null-state slice** on stable symbols: assignments, branches, reassignment
  invalidation, returns, loops, exceptional flow (#783, the null-state slice; the full analyzer
  re-platform #786 and CFG taint #784 move to 0.15 — neither is a prerequisite for effect rows,
  which attach to typed signatures, not the CFG). Refinement/obligation guards gain program-state
  awareness where the null-state slice enables it (#782).
- **Enforced non-nullable reference types**: `str` (#875 — the root-cause fix for divergence D3;
  lifts the string demotion; **the issue covers `str` only — arrays and user reference types are
  this plan's extension of it**). Absence uses `?T`/`Option<T>` or an explicit boundary adapter.
- **Conservative interop**: nullable-oblivious or dynamic .NET APIs get an explicit assumption /
  adapter / interop boundary — never silent non-null.
- **Migration support (C# → Calor)**: convert C# nullable metadata and common null-flow idioms into
  `Option`/match forms **where semantics are clear**; otherwise preserve as interop. No invented
  non-nullability. **Sequencing constraint (PP-S4): no `FeatureSupport` promotion or loss-ledger
  removal merges before the §2.5 gate 6 registry is green** — this is exactly the
  `SupportLevel`-promoting work that re-arms the Call S blocker.
- **TIER1A experiment, adopted wholesale by reference** — `tier1a-postmortem.md` §6, including its
  **full five-row outcome matrix** (§6.3: ≥10 TPs at <10% FP → rebuild and ship; 3–9 → inconclusive,
  scale to 300; ≥10 at ≥10% FP → rework, not rebuild; etc.), not just the ship row. Guards carried
  from the postmortem: the shape-Calor corpus is **generated once and committed with its generation
  prompts before checker reconstruction begins** (no regenerate-until-pass), the **non-implementer
  countersigner** requirement stands, and the prerequisite is budgeted (the TIER1A checker is not in
  git and must be reconstructed before the experiment can run).
- **Verification hygiene the differential program depends on**: Z3-translation/C#-semantics
  alignment (#780), unsigned-obligation modeling (#845), .NET string/numeric modeling corrected
  through differential tests. String proofs regain elision only when UTF-16 and null semantics
  match generated code exactly. (#778 moved to 0.13.)

### 3.3 The 2.0.0 self-migration workstream — the break's own bill

The blast surface in this repo alone: ~1,500 `.calr` files on disk, of which **~800 are committed**
(tests 424, bench 359, samples 11, the 10 SelfTest resources that ship inside the tool, benchmarks
7 — the on-disk remainder is gitignored epoch fixtures), 869 golden files under `tests/TestData`,
~145 parse-checked ```` ```calor ```` blocks across docs and website, the 217-program benchmark
corpus behind the published 1.32× number, and the 0.13 dogfood utility. Four decisions, made here:

1. **Version mechanics**: `SemanticsVersion.cs` today hard-codes `Major = 1` and only rejects
   files declaring a *newer* version — a 2.0.0 compiler would **silently reinterpret** 1.x files.
   0.14 makes the compiler refuse `§SEMVER{1.x}`-declared files with a migration pointer
   (fail-closed; no silent reinterpretation, and no dual-semantics mode to maintain). Files
   declaring nothing get the compiler's major with a diagnostic nudge to declare.
2. **An automated `.calr` migrator** (nullable-annotation insertion), whose **first gate is
   migrating the 0.13 dogfood utility** — the plan's only real "user" is also its first migration
   subject. (If the utility deferred out of 0.13, landing it is a 0.14-entry precondition, per
   §2.2.)
3. **Golden-file regeneration** for `tests/TestData` and the SelfTest resources is budgeted as its
   own PR series, mechanically regenerated + spot-audited, not hand-edited.
4. **The benchmark corpus migrates**, and the 1.32× number is re-baselined at 2.0.0 semantics with
   the discontinuity disclosed in the CHANGELOG — a frozen 1.x corpus would make the headline
   benchmark measure the old language forever.

### 3.4 Converter posture — no broad campaign (unchanged from Draft v1, now with the demotions cited)

**Fidelity beats coverage.** The census (PP-S1 = MISS, 33 causes, structural) says broad converter
fidelity is not a work-list; unsupported C# remains explicit interop. This knowingly demotes the
conversion-family P0s — **#760, #769, #770, #771, #772, #775, #776, #777** (and P1s #766, #768,
#847) — from "campaign" to "demand-driven": a converter change is in scope only when a **named
failing migration fixture is registered first** (in the §2.5 gate 6 registry) — "demand" has an
instrument, not an adjudicator's mood. `NativeFraction` stays **report-only and published per
release** so drift is visible (A-1.6(b)); any future fidelity *target* requires a new pre-registered
threshold — the fired census rule may not be re-decided. Elision re-enables by default when §2.5
gate 5's differential suite is zero-mismatch across the pinned denominator (this is 0.14's
completion of the 0.13 program).

### 3.5 Release gates

1. **Null-flow soundness, closed classes with a differential oracle**: on a pinned adversarial null
   corpus plus the typed-CFG oracle tests, every runtime `NullReferenceException` in a non-interop
   region corresponds to a Calor diagnostic (Calor-diagnostic vs runtime-NRE differential). Claimed
   per closed construct class, never as "no null value can ever" — surface not exhausted.
2. **Signature resolution, non-circular denominator**: **resolved-fraction over all .NET call sites
   in the pinned conversion-subject corpus** (frozen commits), with the unsupported remainder
   published as a ledger. The fraction's bar freezes at the §3.1 spike's conclusion, before the
   binding work merges — "supported" is measured against the corpus, not defined by the
   implementation.
3. **Converter honesty**: output uses explicit Option semantics or preserves the original as
   interop; each nullable-idiom translation lands with a registered fixture asserting
   Option-or-interop (CI-checked via the PP-S4 registry). Never invents non-nullability — enforced
   per registered fixture class.
4. **TIER1A adjudicates on its own §6.3 matrix** — whichever row obtains is published, including the
   negative rows.
5. **Self-migration complete**: repo builds green at 2.0.0 semantics; the four §3.3 decisions are
   executed; the benchmark re-baseline and its discontinuity are in the CHANGELOG.
6. **Elision re-enable** (carried from 0.13): differential suite zero-mismatch over the pinned
   modeled-forms denominator with published coverage fraction; otherwise elision stays opt-in and
   the release says so. *(Met at v0.14.3; flipped default-on by PR #1088 for 0.15.0.)*

## 4. v0.15 — Composable Effects

**Draft v4 note (2026-08-24).** This section was rewritten against the source at v0.14.3 (review
round 3, §10). §1–§3 stand as the historical record of what was planned; §4.0 is the measured
inventory of what they actually left behind, and everything below builds on §4.0 rather than on
§1–§3's forward-looking text. Where §4 supersedes a numbered decision in §3 it says so by name.

**Thesis (unchanged):** effects become part of function types, so Calor safely expresses
higher-order code instead of rejecting it.

### 4.0 Where 0.13/0.14 actually left the effect system (measured at v0.14.3, 2026-08-24)

**Shipped — no longer 0.15 work.**

- **Analysis re-platform is done.** Typed CFG (#783, PR #960), CFG-based symbol-resolved taint
  (#784, PR #969), bug-pattern analyzers on the typed CFG (#786, PR #970 —
  `Analysis/BugPatterns/TypedBugPatternAnalysis.cs`; the five `Patterns/*Checker.cs` are shims),
  structural control-flow codegen (#763, PR #972). Residual carried forward:
  `Patterns/PreconditionSuggester.cs` is the one checker still walking the bound tree rather than
  the typed CFG, and #970's three-way split (verified finding / heuristic hint / explicit
  incomplete) is the analyzers' published residual (§4.5).
- **Effect enforcement is fail-closed** (#785, PR #968, 2026-08-13). An unresolved callee yields
  `EffectSet.Unknown` (`Effects/EffectEnforcementPass.cs:1432-1434`, `:2933-2939`), which fits no
  declared set (`Effects/EffectSet.cs:101`) and so forces Calor0410; `--permissive-effects` is the
  explicit waiver. Two of the laundering classes §4.4 gate 1 names are already typing rules at the
  declaration: virtual override → Calor0420 (`EffectEnforcementPass.cs:539`; pins
  `tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs:132` error / `:176` compiles), interface
  implementation → Calor0421 (`:586`; pins `StrictnessBatchTests.cs:198` / `:221`). Invoking a
  function-typed value is *rejected* — Calor0418 (`:1465` for parameters/bindings/fields, `:2690`
  for returned delegates; rejection pins `StrictnessBatchTests.cs:29,47,749`, waiver pin `:64`).
  That rejection is the first-order ceiling this version removes.
- **Project-model spine exists.** `Indexing/ProjectIndex.cs` (format 3.0) under `obj/calor`;
  `calor query` facets `symbol | callers | callees | impact | contracts | assumptions`
  (`Commands/QueryCommand.cs:26-34`); the edit-script identity corpus
  (`tests/TestData/EditScripts/`, ES-01…ES-07, CI-blocking via `EditScriptIdentityTests` —
  but see the F-3 supersession in gate 3); the golden query corpus (`tests/TestData/QueryCorpus/`);
  `calor rename`; LSP on `SymbolId` (#765/#767); the gate-8 performance envelope (nightly,
  `performance.yml`).
- **`FunctionBoundType` exists** (`Binding/BoundTypes/BoundType.cs:212-241`) with parameter and
  return types only; its doc comment reserves the effect-row slot for 0.15.
- **`MetadataBinder` is always on** — a best-effort enrichment inside `Binder`
  (`Binding/Binder.cs:143-215`; failures swallowed, never a hard gate). The
  `--enable-metadata-binding` flag that the scoping doc's D7 planned was never implemented (no
  commit outside the scoping doc mentions it), so "unflagged" is the state the spike shipped in,
  not the result of S6's flag retirement. The `BoundExpression.TypeName` shim is gone (F-5); the
  spike's S7 intent landed.
- **The S5 anti-tautology pin exists and blocks CI.**
  `tests/Calor.Compiler.Tests/Binding/Metadata/MetadataBinderCorpusMeasurementTests.cs:37-118`
  re-executes the corpus measurement and asserts **exact equality** — aggregate and per-subject —
  against `bench/phase0-agent-native/metadata-binding-corpus-ledger.json`; it runs on the
  `compiler` shard with submodules (`test.yml:368,383`), and a silent skip would trip the
  `expectedSkipped` count in `eng/test-manifest.json`. Only the scoping doc's PR-body-parsing leg
  was never built. (Draft v4's first revision said the pin did not exist; §10.)

**Not shipped — Draft v3 assumed these from 0.14.**

- **Metadata-binding S6 never merged.** `v0.14-metadata-binding-scoping.md` §3 S6 ("effect
  resolver reads `Type`; `_variableTypeMap` deleted") has no PR; the nullability workstream
  reused the S6/S7 labels (PRs #1067, #1073 are different slices). `Effects/ExternalCallCollector.cs`
  still carries `_variableTypeMap` (`:58,244,254`) — receivers resolve from
  `ReceiverSymbol.TypeName` *strings* first (`:278,283`), the map second. `EffectResolver.Resolve(string
  type, string method, string[] params)` (`Effects/EffectResolver.cs:48`) is string-keyed end to
  end, and nothing under `src/Calor.Compiler/Effects/` references `MetadataBinder`. Effect
  manifests and IL-derived effects feed that string lookup, beside the binder, not through it.
- **Lambdas are not function-typed yet.** `BoundLambdaExpression.Type` is a stringly
  `NominalBoundType("LAMBDA(...)")` (`Binding/BoundNodes.cs:2178`), not a `FunctionBoundType`.
  `§LAM` already parses a `§E` annotation (`Ast/LambdaNodes.cs:41`,
  `BoundLambdaExpression.DeclaredEffects`) that enforcement discards — `InferFromLambda`
  (`EffectEnforcementPass.cs:2942`) charges the body and ignores the declaration.
- **`Assumed` is not a lattice state.** It is a side table surfaced as Calor0419
  (`EffectEnforcementPass.cs:451-462`); `EffectResolutionStatus` is `Resolved | PureExplicit |
  Unknown`. `EffectSet.Unknown` is the only sentinel.
- **Signature resolution is 65.46%** over the pinned corpus (817/1248; MediatR 57.08%, Serilog
  92.04%, FluentValidation 64.25% — the ledger above). A third of BCL call sites therefore reach
  effect rows as `UnresolvedBoundType`.
- **The index holds no effect facts.** `ProjectIndexBuilder` never mentions effects;
  `EffectSummary` persists in the incremental build cache (`Incremental/BuildStateCache.cs:52`),
  keyed by name strings (`Effects/EffectSummaryBuilder.cs:68,75`). `impact` is transitive callers
  only (`ProjectIndex.cs:372-408`); the index records *declared* contracts, never an outcome
  (`:56-63`). `review-packet` computes impact from an in-memory call graph
  (`Reporting/ReviewPacket.cs:3-12`), not the index. MCP `calor_navigate`
  (`Mcp/Tools/NavigateTool.cs`) neither reads the index nor exposes callers/impact.
- **0.14 gates not adjudicated** (dispositions in §4.5): 3.5.1 null-state slice and adversarial
  null corpus (absent); 3.5.4 TIER1A (`docs/experiments/registry.json` is `{"entries": []}`; the
  reconstructed checker lives only on `origin/experiment/tier1a-rebuild-for-section-6`, not an
  ancestor of `main`); 3.5.5 self-migration (no `.calr` migrator; `SemanticsVersion.Major = 2`
  but `CheckCompatibility` still accepts 1.x, `SemanticsVersion.cs:38-46`, and has no caller in
  `src/`; **no committed `.calr` declares `§SEMVER` at all — 0 of 886** — so the corpus has
  compiled under 2.0.0 semantics since v0.14.0 and the "migrated corpus" of §3.3 was never a
  distinct artifact; no golden regeneration; the 1.32× headline is byte-identical across
  0.14.0–0.14.3 with no discontinuity note); 3.5.6 elision (the differential suite reports 0
  mismatches and elision coverage 40/65 — the re-enable condition is met — yet
  `--elide-proven-guards` remained opt-in at v0.14.3, `Program.cs:94-97` — **flipped default-on by
  PR #1088** for 0.15.0; `--keep-proven-guards` is the opt-out).
- **Null classes 0.14 did ship**, cited so §7 has a source: `str` scalars, arrays of `str`,
  whitelisted generic instantiations over `str`, and user reference types from Annotated sources
  only (CHANGELOG 0.14.0–0.14.3; S8-Oblivious widening held in draft PR #1078; epic #1082).
- **Measurement prerequisites partly landed.** No real Calor arm
  (`tools/Calor.RoundTrip.Harness/TaskGen/TaskGenReportWriter.cs:76-86`: "the runner never
  invokes the Calor compiler"); #881 is corrected (`run-bundle.sh` /
  `run-pair.sh` now read the cost-leg figure from the shared `token-usage.py`, which sums
  `modelUsage[*].outputTokens`; pinned reproduction in `bench/phase0-agent-native/tests/`); PR #944 — the §3.1 spike's
  pre-registration — is still open after S1–S5 shipped, which is the register-then-merge
  discipline breached once already. The only append-only tamper guard in the repo
  (`experiment-registry-tamper-check.yml`) covers `docs/experiments/registry.json`, **not** the
  A-annex where PPs are actually registered.

### 4.1 Entry gate — the design doc, on the direction doc's own terms

`calor-direction.md` mandates `docs/design/effect-rows-in-the-type-system.md` before TIER2D-class
implementation starts. Its 2026-04-22 postscript (`calor-direction.md:117-118`) tightened the
terms after TIER1A failed on a designer-judgment gate, and Draft v3 cited only the weaker
original. 0.15 adopts the postscript's three conditions, tightening the first:

1. **An emitter spike producing actual compiler output**, not prose examples. The postscript says
   "1–2 non-trivial modules"; this plan fixes **two**, named and frozen in the design doc before
   the spike runs: the dogfood utility (`tools/calor-allowlist-audit/allowlist-audit.calr`) and
   one module from a pinned conversion subject. Before/after output is committed alongside the
   doc. The spike runs on a throwaway rows branch; it does not gate E1 (below).
2. **An external critique cycle** with a pass bar, because every review record in this document
   (§8–§10) returned NEEDS-FIXES-then-applied and "two lenses ran" is satisfied by construction:
   at minimum evidence, internal-consistency, and test lenses on the doc; **exit criterion: both
   the evidence and consistency lenses return APPROVE on a revision, or every declined finding is
   recorded with its rationale** in the doc's review record.
3. **Priced blast radius in the doc itself**: the `IAstVisitor` surface (~236 methods × N
   implementers), `EffectSummary` cache migration, golden files under `tests/TestData`, the
   `BoundType.DisplayString` byte-identity discipline (consumers:
   `src/Calor.LanguageServer/State/WorkspaceState.cs`, `Utilities/SymbolFinder.cs` — the F-3B
   rule), conversion snapshots, and the round-trip harness.

**Demand denominator, registered before the doc is written — with its own tautology guard.** The
postscript reframed TIER2D as "architectural refactor, not new capability." Calor0418 *rejects*
higher-order code today, so the claim is testable rather than settled — but a corpus written in a
language that rejects the idiom will under-count it, which is the circularity the postscript's
§2(b) says killed TIER1A. So: **two denominators**, both frozen in
`bench/phase0-agent-native/higher-order-demand-ledger.json` (the `binder-incomplete-baseline.json`
pattern, re-executed by a sibling of `BinderIncompleteRatchetTests` on the `compiler` shard),
with the measured commit SHA recorded inside the ledger:

- **D-A (Calor-native):** Calor0418 firings plus function-typed Calor0419 assumptions over the
  committed `.calr` corpus.
- **D-B (C#-shaped backstop):** delegate/lambda/`Func`/`Action` parameter and invocation sites in
  the three A-1.5.3 conversion subjects (a Roslyn count, independent of Calor's rejection).

**Pre-registered floor:** if D-A + D-B is below 25 sites, gate 2 adjudicates **not-adjudicated**,
never HIT. Freeze event: the ledger's registration PR merges before the design doc opens; the
design doc opens with the two numbers.

**E1 is permitted to start before the design doc merges.** E1 (§4.2) deletes a string path and
changes keying; it introduces no row syntax and pre-empts no design decision. The doc records
E1's keying as executed. Without this, the doc needs E1 (for the emitter spike) and E1 needs the
doc — a circle the first revision of Draft v4 contained.

**Decisions the design doc must settle, each named so its absence is visible:**

- Row syntax on function types (the direction doc's `Int !{db:w, throw}` sketch is a placeholder,
  not a decision) and how it composes with `§E{}` on declarations.
- `Unknown` and `Assumed` as states of the row lattice (today: sentinel + side table).
- The fate of `§LAM`'s dormant `§E` annotation — it becomes the lambda's declared row, or it is
  removed; it does not stay parsed-and-ignored.
- Whether Calor0420/0421 fold into the general row-subtyping rule or remain separate codes.
  **Both outcomes retain the four existing adversarial pins; only the emitted code differs.**
- Whether `EffectSummary` is derived from the index or migrated into it (E5).
- Diagnostic allocation for E4, F-4 style: **Calor0424 `EffectRowMismatch`** and **Calor0425
  `EffectRowUnknown`** are reserved here (verified free at v0.14.3; 0400–0499 is the effects
  band) and frozen at design-doc merge.
- Rank-1 polymorphism: validated on the named combinator set (`Map`, `Match`, middleware,
  callbacks) by the emitter spike, or deferred via the exit ramp.
- Async/`Task`-shaped effects: deferred past 0.15 unless the spike finds it cheap (§5.1
  unchanged). `BoundLambdaExpression.IsAsync` today affects only a display string.

(`UnresolvedBoundType` → `Unknown` row, `FunctionBoundType`'s effect slot, and symbol-identity
keying are E1 decisions, made in §4.2, not design-doc decisions.)

**Exit ramp (pre-registered), and what it changes downstream:** if rank-1 polymorphism fails to
validate on the named combinator set, 0.15 ships monomorphic rows with explicit Unknown/Assumed
propagation and defers polymorphism. When the ramp fires, **E3's rank-1 leg and gate 1's fifth
class are removed with it** — gate 1's denominator becomes four classes and the release notes say
so. Still a shippable release that removes the first-order ceiling for the common case.

### 4.2 Ship — tiered, with the cut lines stated

MUST items gate the release; SHOULD items ship if they fit and defer to 0.15.x without
renegotiation; DEFERRED items are named so their absence is a decision.

**MUST**

- **E1 — Foundation (the never-merged metadata S6, plus the lambda type).**
  `ExternalCallCollector` and the enforcement pass resolve receivers and callees from
  `BoundExpression.Type` / bound symbols; `_variableTypeMap` is deleted; `EffectResolver`,
  manifests, and IL summaries key on symbol identity so external effects attach to typed external
  signatures; `BoundLambdaExpression` binds to a `FunctionBoundType`; an unresolved receiver
  contributes an `Unknown` row through `UnresolvedBoundType`, never a guessed one. E1 is the item
  everything else stands on, and it is exactly the work Draft v3 assumed 0.14 had done.
  **Exit pins, to be added in the E1 PR (none exist today):** (a) a grep pin — a `[Fact]` in
  `Calor.Enforcement.Tests` asserting no `_variableTypeMap` identifier under
  `src/Calor.Compiler/Effects/`; (b) the original S6 behavioural criterion — a receiver whose
  type is available **only** through metadata (no AST type string anywhere in the module)
  resolves its effects; (c) a structural pin that the string path is deleted, not bypassed — no
  `EffectResolver.Resolve(string, string, …)` overload remains.
- **E2 — Effect rows** on function, delegate, and lambda types (monomorphic MUST; rank-1
  polymorphism behind the §4.1 ramp).
- **E3 — Effect-compatibility checking** at assignment, argument, return, override, and
  interface-implementation sites, as one row-subtyping rule — plus rank-1 generic-instantiation
  sites **unless the §4.1 ramp fires**. Calor0420/0421 either fold into it or are re-pinned
  against it (design-doc decision; pins retained either way).
- **E4 — Calor0418 replaced.** Accepted when the function value's row fits; Calor0424 on
  mismatch; Calor0425 when the row is Unknown/Assumed because metadata is incomplete. The
  `DelegateInvocation_*` pins (`StrictnessBatchTests.cs:29,47,64,749`;
  `EffectEnforcementTests.cs:354,378`) are rewritten from "is an error" to "fits / does not
  fit"; the `--permissive-effects` waiver survives as the waiver for Calor0425 only (a row that
  *does not fit* is never waived — §4.5 row).
- **E5 — Effects facet in the index.** Effect rows per declaration recorded in `ProjectIndex`;
  `calor query effects`; effect-change blast radius via the existing `impact` closure.
  `EffectSummary` is derived from the index or migrated into it (design-doc decision) — and
  **a structural pin that no name-keyed second store remains** (`EffectSummaryBuilder`'s
  `function.Name` / `"Class.Method"` keys at `:68,75` gone) ships with E5, since gate 7 observes
  the facet's correctness but not the old store's deletion.
- **M1 — Measurement prerequisites that block E2's merge** (moved here from §4.3 so the chain is
  visible): PR #944 dispositioned; ~~#881 corrected or the cost leg re-registered~~ **done** (PR #1092:
  `bench/phase0-agent-native/token-usage.py`, annex A-1.9.1); the 0.15 PP
  registered in the A-annex (A-1.10). **No effect-row implementation (E2) merges before M1 is
  done** — §4.3 (i).

**SHOULD** (0.13 §2.2 leftovers this bullet used to hide inside "the agent workflow completes")

- **E6** `review-packet` reads the index (callers and effects) instead of its in-memory graph.
- **E7** MCP query surface reading the index: callers / callees / impact / effects.
- **E8** Contract outcomes recorded in the index and an invalidated-proofs facet.
- **E9** Affected-tests mapping (a new facet; no design exists today).
- **M2** The real Calor arm (§4.3) — a product commitment with no gate; SHOULD so its deferral is
  a recorded decision rather than a silence.

**DEFERRED** (named, and frozen as gate 1's residual list at design-doc merge): async rows
(§5.1); `PreconditionSuggester` on the typed CFG (#786 residual); reflection / `DynamicInvoke` /
`dynamic`-receiver dispatch; **event-handler subscription** (`+=` of a function value to a .NET
event); **BCL-returned delegates** (a `Func` obtained from a metadata call, whose row is
Unknown by construction until IL analysis produces rows).

**Cut lines.** (1) If E1 overruns, E5–E9 and M2 defer to 0.15.x; E1–E4 and M1 do not defer,
because a release titled "Composable Effects" without rows is not that release. (2) **Schedule
abort:** if E1 has not merged by the 0.15 branch cut, 0.15.0 ships as E1-only under a renamed
theme ("Symbol-Resolved Effects") and E2–E5 move to 0.16 — the postmortem's V3/V4 lesson is that
this repo underestimates binder-adjacent work, and E1 is binder-adjacent.

### 4.3 Honest measurement

- **A real Calor arm, as product** (M2): Option 1 from Call S — Calor0410 enforcement genuinely
  in the agent loop — gets built regardless of any epoch. Today's harness ships round-tripped C#
  in both arms and never invokes the compiler (§4.0).
- **#881 was a scheduled slice (M1), not a footnote — landed (PR #1092).** The probe's cost leg
  read `output_tokens` from `agent.json`, which under-counted 55× on subagent/compaction runs.
  The counter was corrected in `run-bundle.sh` / `run-pair.sh` through the shared
  `bench/phase0-agent-native/token-usage.py` (sums `modelUsage[*].outputTokens`; naive figure
  retained in `result.json` `tokenUsage` for audit; runner warns on disagreement) with a pinned
  reproduction in `bench/phase0-agent-native/tests/` and annex entry A-1.9.1. It landed before
  the PP registers, as required.
- **The pre-registered fixture-scale probe**, under a NEW PP id, with the full discipline:
  **(i)** freeze event named — the PP registers in the A-annex (`docs/plans/agent-native-gates.md`,
  currently A-1.9; **the A-1.10 bump is the freeze event**) before any effect-row implementation
  merges; the empty `docs/experiments/registry.json` is the TIER1A-hypothesis registry and is not
  where this goes. **The annex has no mechanical tamper guard today** — the append-only check
  (`experiment-registry-tamper-check.yml`) covers only `registry.json`. The A-1.10 PR extends that
  workflow's `paths` to the annex with an append-only check on its revision log, so the freeze
  is enforced by the same instrument that guards the other registry rather than by discipline
  alone. **(ii)** fixture and defect classes frozen in the same annex entry, with honest-timing
  disclosure if authoring is concurrent (A-1.2 pattern); **(iii)** the four-valued outcome (hit /
  miss / underpowered / not-adjudicated) with a pre-registered decidability fallback; **(iv)** the
  "no large loop tax" margin stated numerically via the PP-W5 derivation (existing-epoch
  variance → null-simulation p95 → bootstrap-bound conjunction).
- **Register-then-merge has a precedent to repair first (M1):** PR #944 (the §3.1
  pre-registration) is still open while its spike shipped. It is merged as the historical record
  or closed with the discrepancy noted before the 0.15 PP registers — otherwise the discipline is
  aspirational.
- **No real-scale epoch** unless both registered re-entry conditions hold (≥70 evaluable tasks;
  a real Calor arm). Unchanged.

### 4.4 Release gates — instrument, denominator, freeze point, discriminating pin

Conditional (they move with their SHOULD-tier deliverable): gate 3's MCP leg (E7), gate 7's E6/E7
legs. Conditional on the §4.1 ramp *not* firing: gate 1's fifth class. Unconditional: everything
else, including gate 7's E5 leg (E5 is MUST and cannot ship ungated).

1. **Effect laundering, closed classes.** *Instrument:* one adversarial pin per class, the
   `DelegateInvocation_*` pattern, positive and negative (`_IsError` / `_Compiles` pairs as
   `StrictnessBatchTests.cs:132/176` and `:198/221` already do). *Denominator:* five classes —
   virtual override and interface implementation (already closed by Calor0420/0421; **re-pinned
   under rows**, since folding them into E3 could silently reopen them), delegate/function-value
   *assignment*, *argument*, and *return* (closed by E3's typing rule, not by E4's rejection), and
   rank-1 generic instantiation (four classes if the ramp fires). *Freeze point:* the class list
   and the residual list freeze at **design-doc merge**; the residual (reflection,
   `DynamicInvoke`, `dynamic` receivers, event-handler subscription, BCL-returned delegates) is
   named in the release notes as "not closed", never "no callback can". *Discriminating pin:*
   delete E3's rule for any one class and its `_IsError` pin fails.
2. **Higher-order expressiveness.** *Instrument:* the §4.1 demand ledger re-executed at the
   release commit. *Denominator:* D-A + D-B as frozen, plus the registered combinator set. *Bar:*
   Calor0418 firings on the registered classes go to zero without `--permissive-effects` or
   interop wrapping; the residual count and its classes are published; below the 25-site floor
   the gate reads **not-adjudicated**. *Freeze point:* the ledger's registration PR (before the
   design doc). *Discriminating pin:* re-introduce the 0418 rejection for one class and the
   ledger test fails.
3. **Surface agreement.** *Instrument, as it exists:* `EditScriptIdentityTests` compares **clean
   vs incremental only, in one in-process path** (`CompilationDriver.CompileAll`), on canonical
   diagnostics text and serialized index bytes. *Instruments to build, each registered before E2
   merges:* a CLI-process leg and a `Calor.Sdk` leg over the same scripts; the MCP leg is
   conditional on E7. Effects are observed as diagnostics and index bytes — the gate claims no
   more. *Denominator:* the edit-script corpus, **after F-3 is superseded-with-disclosure**: the
   freeze record (`v0.13-freeze-registrations.md` F-3) registers ES-01…ES-06 with a
   `steps.jsonl`/`expect.md` schema and ES-04 as the #883 reproduction; the tree holds
   ES-01…ES-07 with a `script.json` schema and different semantics (the #883 shape lives in
   ES-05/ES-07). Under F-3's own "immutable once landed; supersede-with-disclosure only" rule,
   the supersession PR (schema + seven members re-registered, #883 leg re-identified) lands
   **before ES-08** — the effect-row script — registers, which itself lands before E2 merges
   (`RegisteredScriptIdsAreStable`, `EditScriptIdentityTests.cs:217-231`, forces the
   registration mechanically). PR #968's "defaults equivalent" sentence becomes a test that
   enumerates the four entry points' default `UnknownCallPolicy`. *Discriminating pin:* flip one
   surface's default and the equivalence test fails; drop ES-08 from the id list and
   `RegisteredScriptIdsAreStable` fails.
4. **The probe adjudicates** at its frozen thresholds under its four-valued outcome.
   *Instrument/denominator/freeze:* the A-1.10 annex entry (PP id, fixture, defect classes,
   margin). *Discriminating pin:* the annex append-only check rejects an edit to the frozen row.
5. **Compatibility, restated over the corpus that exists.** Draft v3's denominator — "the repo's
   migrated `.calr` corpus" — was never a distinct artifact: no committed `.calr` declares a
   version (§4.0). *Denominator:* the committed `.calr` corpus at the 0.15 branch-cut commit, in
   two legs — (a) **what CI compiles today**: `tests/TestData/Benchmarks` (226 files, ≥200
   asserted by `BulkBenchmarkCompilationTests`) plus `samples/` (`verify_phase1.py`) plus every
   `.calr` a test project compiles; (b) **the remainder of the 886** — no job compiles them today,
   so a `compile-all-committed-calr` job is registered **before E2 merges** and its count
   published; until it exists the claim is leg (a) only, and says so. *Instrument:* builds and
   tests green under the 0.15 compiler, **with E1-attributable changes separated**: E1 will
   resolve callees that string-guessing missed and fire new, *correct* Calor0410/0419 on this
   corpus; those are fixed in-corpus and their count published; only regressions *not*
   attributable to a newly-resolved callee fail the gate. First-order `§E` compatibility is
   claimed over that corpus, not universally. *Discriminating pin:* revert one in-corpus fix and
   leg (a) goes red.
6. **Resolution floor — keep the existing pin green.** *Instrument:*
   `MetadataBinderCorpusMeasurementTests` (§4.0), an exact-equality pin — aggregate **and
   per-subject** — against the committed ledger, on the `compiler` shard. *Denominator:* the
   pinned conversion-subject commits on the pinned SDK. *Freeze point:* the v0.14.3 values
   (817/1248; 129/226 MediatR, 104/113 Serilog, 584/909 FluentValidation) are the floor **for the
   0.15.0 release commit, whatever moved it** — E1's re-keying and #1082's `MetadataBinder`
   return-annotation change alike. Because the pin is two-sided, a *raise* also fails it until
   the ledger is regenerated (`CALOR_REGENERATE_S5_LEDGER=1`) in the same PR with the delta
   disclosed; a reference-manifest regeneration for SDK drift re-baselines the floor in its own
   PR, never bundled. This is the guard against "symbol-resolved" being achieved by resolving
   fewer symbols — and it is also what makes a mis-sequenced #1082 landing visible.
   *Discriminating pin:* the test as it stands.
7. **Index/query correctness, effects leg.** *Instrument:* `QueryGoldenTests` — ground truth
   authored from the fixture, not recorded (`EveryGoldenStatesWhyItExists`,
   `QueryGoldenTests.cs:152-172`); an unknown facet **throws** (`:134`), so the E5 PR must add
   the `effects` arm or the golden cannot land. *Denominator:* `tests/TestData/QueryCorpus/`,
   extended with effects ground truth. *Freeze point:* the E5 PR. E5 leg unconditional; E6/E7
   legs conditional. *Discriminating pin:* alter one expected effects answer and the golden
   fails.

### 4.5 Carried 0.14 debt — dispositioned with a trigger and a venue

| Item | State at v0.14.3 | 0.15 disposition |
|---|---|---|
| 3.5.6 elision re-enable | Condition met: 0 mismatches over the pinned modeled-forms denominator (`test.yml:196-200`), coverage 40/65 published; still opt-in at v0.14.3 | **Done — PR #1088** flips default-on for 0.15.0 on every surface (CLI compile/run/test, `CompilationOptions`, MSBuild task + `Sdk.targets`, MCP `calor_compile`), with `--keep-proven-guards` as the opt-out. The blocking pin `ProvenPostcondition_WithoutOptIn_KeepsGuard` became `ProvenPostcondition_Default_ElidesGuard` + `_WithOptOut_KeepsGuard`; `DifferentialGate.cs:42` still sets the flag explicitly (both legs). Caveat carried into the changelog: the differential executes the guard-forced emission and inspects the elided emission structurally |
| 3.5.5 self-migration (2.0.0) — **supersedes §3.3 decisions 1–4 as written** | `Major = 2`; 1.x accepted silently; no migrator; no golden regen; no re-baseline; **0 of 886 committed `.calr` declare a version** | Tracked as **#1084**. (1) **Execute §3.3 decision 1 as written, in 0.15.0**: wire `CheckCompatibility` so a declared `§SEMVER{1.x}` file is *refused* with a migration pointer (Error, fail-closed — not the Warning the first revision of this draft proposed; nothing in-repo declares 1.x, so the cost is `VersioningTests.cs:70-74` and 14 doc blocks). (2) Decision 4's re-baseline becomes a CHANGELOG **disclosure**: the corpus declared nothing and has been measured under 2.0.0 semantics since v0.14.0. (3) Decisions 2–3 (migrator, golden regen) are **demand-driven** — a user-reported 1.x file re-opens them immediately; otherwise re-adjudicated at the 0.16 branch cut. Gate 5 restated accordingly. (Executing this revealed the directive had never been lexed; PR #1087 adds it.) |
| 3.5.4 TIER1A adjudication | Registry empty; checker on a non-ancestor branch | **Not a 0.15 gate.** The 0.15.0 release notes carry an explicit "TIER1A: not run" row (an honest negative, a release-notes commitment with no instrument); running it under its §6.3 matrix is re-adjudicated at the 0.15.0 retro |
| 3.5.1 null-state slice + adversarial null corpus | Absent | Not a prerequisite for rows (unchanged reasoning). Trigger: the 0.15.0 retro decides whether it is 0.15.x or 0.16; venue: the retro's disposition table |
| #1082 (nullability follow-ons) | Epic open; PR #1078 draft | **Sequenced after E1 merges.** Item 1 changes what `MetadataBinder` emits for every reference-type return — the same surface E1 re-keys on; gate 6's ledger is what makes a mis-sequenced landing visible |
| #845 (unsigned obligation modeling) | Open | 0.15.x; re-adjudicated at the 0.15.0 retro |
| #875 (non-null `str` root cause) | Open; the `str` scope shipped across 0.14.0–0.14.3 | This plan's judgment (not the issue's own text): closes when #1082 items 1–3 land; no 0.15 gate |
| #859, #884 (Z3 CI flake) | **Still open** after 0.13 and 0.14 shipped (§5.2 previously dispositioned them to 0.13) | 0.15.x instrument debt; re-adjudicated at the 0.15.0 retro with the flake rate over the 0.15 cycle attached |
| #970 residual (verified / heuristic / incomplete tri-state) | Shipped as the analyzers' published residual | Unchanged in 0.15; the tri-state counts are published per release |
| `--permissive-effects` waiver under rows | Waives Calor0410/0411/0418 today | Survives as the waiver for Calor0425 (Unknown/Assumed rows) only; a row that does not fit (Calor0424) is never waived. Pinned in E4 |
| PR #944 (§3.1 pre-registration) | Open after the spike shipped | M1 — resolved before the 0.15 PP registers (§4.3) |
| PR #982 (§2.5 gate 7 CLI leg), #981, #976 | Open docs/CI PRs | Merged or closed in the 0.15 kickoff sweep; none gates 0.15 |

## 5. Explicitly not in these three releases — and the backlog dispositioned

### 5.1 Non-goals

- **No resurrection of the retired real-scale benchmark** until both registered re-entry conditions
  exist (≥70 evaluable tasks; an arm that actually invokes Calor).
- **No broad "support every C# construct" converter campaign** (§3.4 names the demoted issues).
- **No frames/`old()`, ownership, ghost state, or general heap verification.** Repeatedly lacking
  demonstrated demand.
- **No full generic-constraint or higher-rank type system.** Rank-1 effect polymorphism at most,
  behind the §4.1 ramp.
- **No declaration-addressed editing revival.**
- **No 1.0 declaration.**
- **Async/await**: binding `await` structurally is in #762's DoD (0.13); async *effect* semantics
  (`Task`-shaped effects in rows) is explicitly deferred past 0.15 unless the §4.1 design doc finds
  it cheap — deferred by decision, not silence.
- **Calor-library packaging/distribution**: premature at zero external adopters; revisit at Call 3.
- **Adoption campaigns** (#709 Codex / #710 Copilot / #711 Gemini validation, #673 MCP scaffold
  spine): continuous, post-0.15-leaning work, deliberately **not** release-gated — three
  external-agent validation campaigns do not share a version with the type system's centerpiece.
  Call 3 (named external adopter) remains the maintainer's reserved one-way door; PP-A2 stays
  "demand unproven".

### 5.2 Open-backlog disposition table (#760–#793 and successors — every open P0/P1 gets a line)

Refreshed in Draft v4 against GitHub state on 2026-08-24. Closed issues keep their row so the
plan's history is legible; where the closing PR is known it is named, otherwise the close date.

| Issue | Disposition |
|---|---|
| #760, #769, #770, #771, #772, #775, #776, #777 (conversion P0s), #766, #768 (P1s) | Demoted to demand-driven by Draft v1 (§3.4) — and then **closed anyway**: #760 (PR #918, 08-11), #770/#771/#776 (08-12), #766 (PR #977) and #768 (PR #975) (08-14), #769/#772/#775/#777 (08-18). PR #981's inventory records the post-0.13.2 batch |
| #847 (P1) | **Open** — the one conversion item still demoted; a named failing fixture re-opens it |
| #761 (compilation ⇒ Roslyn-valid C#) | **Closed.** Enabled by metadata binding; the §3.5.2 ledger is the residual instrument (§4.4 gate 6 carries it) |
| #762 | **Closed** (0.13 MUST, PR #900 completion) |
| #763 (structural control-flow codegen) | **Closed** (PR #972, 2026-08-14) |
| #764 | **Closed** (0.13 MUST) |
| #765, #767 (LSP) | **Closed** (PRs #921, #926, #980) — LSP consumes `SymbolId`s; the MCP *query* surface did not ride with it and is §4.2 E7 |
| #773 (FeatureSupport executable contract) | **Closed** (PR #991, 2026-08-18) |
| #778 | **Closed** (0.13 MUST) |
| #779 | **Closed** 2026-08-11 — the differential program exists and is green (0 mismatches, coverage 40/65); the **re-enable itself is the open half**, dispositioned in §4.5 |
| #780 | **Closed** |
| #781 (contract inheritance/modules) | **Closed** 2026-08-13 — removed from the §4.1 design-doc scope list |
| #782 (obligation guards on program state) | **Closed** 2026-08-13 |
| #783 | **Closed** (PR #960) |
| #784, #786 | **Closed** (PRs #969, #970) — no longer 0.15 work; residuals in §4.0/§4.5 |
| #785 | **Closed** (PR #968) — fail-closed shipped; the type-system unification is §4.2 E1–E4 |
| #787 (functional Sdk) | Closed by v0.12.1's first publish plus the shipped dogfood utility (`tools/calor-allowlist-audit`, CI-built via `Calor.Sdk`) |
| #788 (MSBuild determinism) | **Closed** 2026-08-10, before F-3's registered corpus was replaced in tree (§4.4 gate 3). §2.5 gate 2 holds on the ES-01…ES-07 corpus as it exists; the F-3 supersession is what makes that claim clean on paper |
| #789 (hermetic natives) | **Closed** 2026-08-12 |
| #790 (truthful release gates) | Closed when 0.13 shipped with the triple-form gates; §4.4 continues the form and adds a discriminating pin per gate |
| #791 (generated exhaustive infra) | **Closed** (PR #1023) |
| #792 (telemetry opt-in) | Closed |
| #793 (audit epic) | **Closed** 2026-08-19; this table remains its disposition record |
| #845 (unsigned-obligation modeling) | **Open** — 0.15.x (§4.5) |
| #851 (task-gen filter precision) | Retired with the venue; re-opens only under §4.3's re-entry conditions |
| #859, #884 (Z3 CI flake) | **Open** — Draft v3 dispositioned them to 0.13; both survived 0.13 and 0.14. 0.15.x instrument debt (§4.5) |
| #874, #879, #883 | Closed (0.13 MUST) |
| #875 (non-null `str`) | **Open** — `str` scope shipped across 0.14.0–0.14.3; remainder tracked by #1082 (§4.5) |
| #881 (agent token metrics 55× under-count) | **Corrected (PR #1092, annex A-1.9.1)** — `bench/phase0-agent-native/token-usage.py` is the single derivation for both runners; `result.json` `tokens.output` = `modelUsage[*].outputTokens` sum, `tokenUsage.output_tokens_naive` retained for audit; pinned test `tests/test_token_usage.py` (§4.2 M1, done). Build-state archiving follow-up: https://github.com/juanmicrosoft/calor/issues/1094 |
| #1082 (v0.14 nullability follow-ons epic) | **Open** — sequenced after §4.2 E1 (§4.5) |
| #1084 (§3.3 self-migration residue) | **Open** — filed by this draft; §4.5 row 2 |
| #1011 (test-suite audit epic) | Open; continuous, not release-gated |
| #673 (MCP scaffold spine) | Open; adoption work, not release-gated (§5.1) |

## 6. Cross-cutting disciplines (tuition already paid)

- **Every release gate names its triple**: instrument, denominator, freeze point. A gate missing
  any of the three is an aspiration and may not gate a release.
- **Freeze before measurement** — thresholds registered before values are visible; ordering
  controls over concealment controls; freeze *events* named, not implied.
- **Pins must discriminate** — revert the fix and watch the test fail, or it pins nothing.
- **Verify NuGet registries and GitHub release assets after every publish** — a workflow firing is
  not evidence it succeeded; Marketplace freshness is not a release criterion.
- **Claim only what is closed by construction** — "one class closed", never "surface exhausted".
- **Denominators are pinned artifacts, not implementation-defined sets** — the modeled-forms
  whitelist, the call-site corpus, the RID matrix.
- **No culture-sensitive formatting in committed artifacts**; epoch artifacts byte-reproducible.
- **When a selection/parsing bug is fixed, grep for the pattern everywhere.**

## 7. What "better than C#" means at the end of 0.15

Narrower and much more concrete than where v0.12 left it, and restated in Draft v4 to claim only
what §4.0 shows is closed or §4.2 commits to close. C# still wins on ecosystem, familiarity, and
density. Calor wins where **agent-authored correctness and explainable change impact are the
product**: a queryable semantic project model (`calor query` anchored by the §2.5 golden query
corpus, with the MCP query surface conditional on §4.2 E7), null safety enforced for the closed
classes 0.14 actually shipped (the §4.0 inventory: `str`, arrays of `str`, whitelisted generics
over `str`, Annotated user references — the null-state differential oracle of §3.5.1 is 0.15.x
work, and TIER1A's outcome is on the record as a published not-run unless §4.5 changes that),
honest contracts with a published falsification record and a visible elision-coverage fraction
(40/65 at v0.14.3; default-on per §4.5, or the release notes state why not), and compositional
effect safety for the registered combinator set — monomorphically, if the §4.1 ramp fires — with
the claim instrumented at fixture scale under a pre-registered four-valued PP, and a standing,
quantitative path back to real-scale measurement.

## 8. Review record — round 1 (2026-08-10)

Three independent adversarial lenses on Draft v1; all returned NEEDS-FIXES. Applied in this draft:

- **Evidence (80%)**: CRITICAL — the "changelog correction" bullet was stale (landed in PR #887)
  and wrong on both counts ("three" failures vs the record's every-release-since-v0.4.0; "two were
  the PAT" sourced from session memory, not the record) → §2.4 rewritten from the committed record.
  Major — §0 claimed enforcement "fail-closed" as shipped while #785 and §4 say it is future work →
  corrected. Minors — #875 scope (`str` only) and `Calor.Sdk` first-publish attribution (v0.12.1)
  → corrected.
- **Strategy (60%)**: CRITICALs — no 2.0.0 self-migration story (→ §3.3, four decisions), 0.13
  overloaded with no cut lines (→ §2 MUST/SHOULD/DEFERRED; LSP/MCP deferred; elision opt-in),
  0.15 missing the design gate `calor-direction.md` mandates (→ §4.1 + exit ramp). Majors —
  dependency inversions (→ effects facet excluded from 0.13 query; #778 moved to 0.13; §1
  sequencing restated), differential gate unbound to an enumeration (→ modeled-forms denominator +
  generator), dropped P0s (→ §5.2 table), metadata-binding spike missing (→ §3.1). Minors —
  performance number (→ §2.5.8), async/packaging/ordering posture (→ §5.1), PAT as external
  dependency (→ §2.4).
- **Measurement (40%)**: CRITICALs — binding-totality gate unbounded escape category (→ §2.5.1
  DoD-class denominator + published residual), shrinkable elision surface (→ pinned whitelist +
  coverage fraction), circular "supported" denominator (→ §3.5.2 resolved-fraction over pinned
  corpus), PP-S4 not a gate while 0.14 re-arms it (→ §2.5.6 + §3.2 sequencing constraint). Majors —
  install gate passing the broken Intel-Mac state (→ §2.5.7 solver-verdict bar), probe freeze/
  anti-tautology/four-valued outcome/numeric margin (→ §4.3), TIER1A matrix truncation +
  regeneration route + countersigner (→ §3.2), universal null gates (→ §3.5.1 closed classes +
  differential oracle), query correctness unanchored (→ §2.5.3), dogfood aspirational (→ §2.2
  mechanical checks). Minors — byte-identity qualification, rename behavior oracle, demand
  adjudicator, effect-gate phrasing, §7 claim hygiene → all applied.

Declined findings: none — every finding survived verification and was applied.

## 9. Review record — round 2 (2026-08-10, on Draft v2)

Two rotated lenses on the revised draft; both NEEDS-FIXES; all findings applied in Draft v3:

- **Internal consistency (75%)**: CRITICAL — gates 2/3/8 gated the release on SHOULD-tier
  deliverables with no conditionality (→ §2.5 preamble: conditional gates move to 0.13.x with the
  deferral). Majors — dogfood/migrator dependency dangle (→ §2.2 + §3.3.2: deferral makes it a
  0.14-entry precondition), #875 scheduled in body but missing from the "exhaustive" §5.2 table
  (→ row added — the one-site-fix failure mode recurring one round after it was named), §4.4 gate 5
  missing its instrument/denominator/freeze triple (→ rewritten over the frozen migrated corpus).
  Minors — effects-facet timing (0.15, not "0.14/0.15"), rename instrument named, header wording,
  D15 status clause → applied.
- **New-claims evidence (85%)**: CRITICAL — the binding-totality gate cited "construct classes
  enumerated in #762's DoD", but the issue's DoD is universal and contains no class list; the pin
  asserted did not exist (→ §2.1/§2.5.1: registering the list is 0.13's first to-do and the freeze
  event). Majors — "~19 releases" re-laundered the CHANGELOG's own undercount (release list shows
  27; erratum to file), #789 "substantially landed" overstated (five hermetic items still open →
  row corrected). Minors — blast surface ~800 committed vs ~1,600 headline, #792 already closed,
  the `§PROOF` obligation-elision third path added to the differential denominator, postmortem
  citation sharpened to V4. Verified clean at count precision: 60 expression classes, 869 golden
  files, 217-program corpus, A-1.5.3's three pinned subjects, `SemanticsVersion.CheckCompatibility`
  silent-reinterpretation, no pre-existing index machinery (gate 8's "frozen before the index
  exists" framing is sound).

Declined findings: none.

## 10. Review record — round 3 (2026-08-24, Draft v3 vs. the source at v0.14.3)

**Pass 1 — source-state audit of Draft v3** (three read-only audits: effect system, project
model/analysis, 0.14 closeout), run before 0.15 work starts. Result: NEEDS-FIXES; applied as the
first revision of Draft v4:

- **Stale scope (CRITICAL)** — §4.2 listed #784/#786 and #785 fail-closed as 0.15 deliverables;
  all closed COMPLETED 2026-08-13 (PRs #969, #970, #968), #783 via #960. Gate 1 counted override
  and interface implementation as future work; Calor0420/0421 already close them. → §4.0
  inventory; §4.2 rebuilt; gate 1 restated.
- **Foundation assumed from 0.14 that did not land (CRITICAL)** — effect resolver string-keyed,
  `_variableTypeMap` live, metadata S6 never merged, lambdas stringly typed, `MetadataBinder`
  unreferenced under `Effects/`. → §4.2 E1; gate 6.
- **Gate 5 had no denominator (CRITICAL)** — presupposed a self-migration that did not execute.
  → restated; §4.5.
- **Undelivered deliverables in one bullet (Major)** → E6–E9. **Entry gate cited the weaker
  terms (Major)** → §4.1. **Measurement prerequisites unstarted (Major)** → §4.3. **Carried debt
  without rows (Major)** → §4.5; §5.2.

**Pass 2 — three adversarial lenses on that revision** (PR #1083): evidence (92%,
NEEDS-FIXES, 2 CRITICAL / 5 Major / 9 Minor), internal consistency + strategy (74%, NEEDS-FIXES,
7 CRITICAL / 15 Major / 6 Minor), test-lens (~65% observable, NEEDS-FIXES, 5 load-bearing
UNVERIFIED). All applied in the second revision:

- **The S5 pin exists (evidence CRITICAL; test-lens factual error).** The first revision said the
  ledger's CI pin "does not exist"; `MetadataBinderCorpusMeasurementTests` is an exact-equality,
  per-subject pin on the `compiler` shard. → §4.0 corrected; gate 6 rewritten from "build" to
  "keep green", two-sided, bound to the release commit, per-subject.
- **Ten of eleven "demoted" issues are closed (evidence CRITICAL)**; #773/#793 closed; #859/#884
  open. → §5.2 rows corrected; the preamble no longer promises a closer per row.
- **Circular entry gate (consistency CRITICAL)** — the doc needed E1 and E1 needed the doc. → E1
  may start pre-doc; three E1 decisions removed from the design-doc list.
- **Unconditional gates on ramp-deferrable scope (consistency CRITICAL)** — E3's rank-1 leg and
  gate 1's fifth class. → conditional on the ramp, denominator becomes four.
- **A MUST shipped ungated (consistency CRITICAL)** — gate 7 was conditional though E5 is MUST.
  → E5 leg unconditional.
- **Gate 5 penalised E1's own improvement (consistency CRITICAL)** → E1-attributable new
  Calor0410/0419 separated and published.
- **F-3 was replaced, not extended (consistency + evidence)** — the freeze record registers a
  different six-script corpus under a different schema; the first revision called it "6 vs 7".
  → gate 3 requires supersede-with-disclosure before ES-08; #788's row says so.
- **§3.3 decision 1 had been silently rescinded (consistency CRITICAL)** — the first revision
  proposed a Warning and an open-ended deferral. Measured: 0 of 886 committed `.calr` declare a
  version, so fail-closed costs one test and 14 doc blocks. → executed as written in 0.15.0;
  #1084 filed; supersession named.
- **§4.3 untiered while gate 4 was unconditional (consistency CRITICAL)** → M1 (blocks E2) and
  M2 (SHOULD) in §4.2.
- **Demand denominator circular (consistency Major; test-lens load-bearing)** → two denominators
  (D-A Calor-native, D-B Roslyn-counted backstop), a 25-site not-adjudicated floor, a named
  ledger file and test.
- **Gate 3 overstated its instrument by three surfaces (test-lens load-bearing)** →
  clean-vs-incremental only as it exists; CLI/SDK legs to build; MCP conditional.
- **Gate 5 named no job (test-lens load-bearing)** → two legs; the 886-vs-covered gap disclosed.
- **A-annex has no tamper guard (test-lens load-bearing)** → stated; A-1.10 PR extends the
  existing workflow to the annex.
- **E1 "grep-pinned" cited a pin that does not exist (test-lens load-bearing)** → three named
  pins to be added in the E1 PR; E5 gains a second-store deletion pin.
- **§6 discipline (consistency Major)** — no gate named a discriminating pin; freeze events
  vague. → one pin per gate; freeze events named (design-doc merge, ledger PR, ES-08 PR, A-1.10,
  E5 PR).
- **Schedule abort missing (consistency Major)** → cut line 2: E1-only release under a renamed
  theme if E1 misses the branch cut.
- **§1 and §7 unquarantined (consistency Major)** → §1 note and sequencing amendment; §7 hedged
  on the ramp, the elision flip, and cites §4.0's null-class inventory.
- **Residual list implementation-defined (consistency Major)** → event handlers and
  BCL-returned delegates named in DEFERRED; residual frozen at design-doc merge.
- **Evidence Majors/Minors applied:** Calor0418 unknown-callee cite `:1432-1434` (was
  `:1671-1673`, an event helper); `PreconditionSuggester` is a bound-tree walker, not AST;
  `DisplayString` consumers are `WorkspaceState.cs`/`SymbolFinder.cs`, not `IdScanner`/hover;
  `BoundNodes.cs:2178`; `EffectSummaryBuilder.cs:68,75`; "defaults equivalent" quoted correctly;
  "1–2 modules" is tightened to two, not adopted verbatim; the metadata flag was never
  implemented; `:64` is the waiver pin; #875 closure is this plan's judgment; E4 codes
  reserved (Calor0424/0425); 0420/0421 pins retained either way; `EffectSummary` decision venued;
  "§2.5 gate 7" qualified; #970 tri-state and the permissive waiver get §4.5 rows.
- **§10 under-recorded the first revision (consistency Major):** the §7 rewrite, the §4.4
  conditional preamble, and gate 7 were unlisted, and "twelve" §5.2 rows was an undercount —
  **nineteen** rows moved from a future disposition to Closed. Corrected here.

**Declined findings:** none. One reviewer suggestion was narrowed rather than adopted: the
consistency lens proposed a *dated* re-decision point for the migrator; §4.5 adopts that (0.16
branch cut) but also keeps an immediate demand trigger, since a single 1.x user report is a
better instrument than a date.

Verified clean at count precision (both passes): 18 Calor04xx codes with 0404–0409 and 0424+
free; 7 edit scripts in tree vs 6 registered; 6 query facets; ledger 817/1248 = 65.46% with
129/226, 104/113, 584/909 per subject; differential 0 mismatches, 40/65 eliding;
`SemanticsVersion.Major = 2` with `CheckCompatibility` uncalled; 0 of 886 committed `.calr`
declare `§SEMVER`; `FunctionBoundType` has no effect field; `registry.json` is empty; the TIER1A
branch is not an ancestor of `main`.
