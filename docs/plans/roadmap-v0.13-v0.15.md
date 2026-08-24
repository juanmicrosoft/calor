# Roadmap — v0.13 / v0.14 / v0.15

**Date:** 2026-08-10
**Status:** Draft v4 — Draft v3 (2026-08-10, two adversarial rounds, §8–§9) with §4 rebuilt and
§5.2 refreshed against the source at v0.14.3 on 2026-08-24 (review round 3, §10). §2 and §3 are the
historical record of 0.13/0.14 as planned; §4.0 is the measured inventory of what they left.
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
  becomes **explicitly opt-in** in 0.13; verification remains diagnostic by default. The
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
5. **Differential program (elision opt-in)**: the #779 suite exists, runs in CI against the pinned
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
   the release says so.

## 4. v0.15 — Composable Effects

**Draft v4 note (2026-08-24).** This section was rewritten against the source at v0.14.3 (review
round 3, §10). §2 and §3 stand as the historical record of what was planned; §4.0 is the measured
inventory of what they actually left behind, and everything below builds on §4.0 rather than on
§2/§3's forward-looking text.

**Thesis (unchanged):** effects become part of function types, so Calor safely expresses
higher-order code instead of rejecting it.

### 4.0 Where 0.13/0.14 actually left the effect system (measured at v0.14.3, 2026-08-24)

**Shipped — no longer 0.15 work.**

- **Analysis re-platform is done.** Typed CFG (#783, PR #960), CFG-based symbol-resolved taint
  (#784, PR #969), and bug-pattern analyzers on the typed CFG (#786, PR #970 —
  `Analysis/BugPatterns/TypedBugPatternAnalysis.cs`; the five `Patterns/*Checker.cs` are shims).
  Residual carried forward: `Patterns/PreconditionSuggester.cs` is the one remaining AST walker,
  and #970's three-way split (verified finding / heuristic hint / explicit incomplete) is the
  analyzers' published residual.
- **Effect enforcement is fail-closed** (#785, PR #968, 2026-08-13). An unresolved callee yields
  `EffectSet.Unknown` (`Effects/EffectEnforcementPass.cs:1671-1673`), which fits no declared set
  (`Effects/EffectSet.cs:101`) and so forces Calor0410; `--permissive-effects` is the explicit
  waiver. Two of the laundering classes §4.4 gate 1 names are already typing rules at the
  declaration: virtual override → Calor0420 (`EffectEnforcementPass.cs:539`), interface
  implementation → Calor0421 (`:586`). Invoking a function-typed value is *rejected* — Calor0418
  (`:1465` for parameters/bindings/fields, `:2690` for returned delegates; pins
  `tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs:29,47,64,749`). That rejection is the
  first-order ceiling this version removes.
- **Project-model spine exists.** `Indexing/ProjectIndex.cs` (format 3.0) under `obj/calor`;
  `calor query` facets `symbol | callers | callees | impact | contracts | assumptions`
  (`Commands/QueryCommand.cs:26-34`); the edit-script identity corpus
  (`tests/TestData/EditScripts/`, 7 members, CI-blocking via `EditScriptIdentityTests`); the
  golden query corpus (`tests/TestData/QueryCorpus/`); `calor rename`; LSP on `SymbolId`
  (#765/#767); the gate-8 performance envelope (nightly, `performance.yml`).
- **`FunctionBoundType` exists** (`Binding/BoundTypes/BoundType.cs:212-241`) with parameter and
  return types only; its doc comment reserves the effect-row slot for 0.15.
- **`MetadataBinder` is unflagged and always on** — a best-effort enrichment inside `Binder`
  (`Binding/Binder.cs:143-215`; failures swallowed, never a hard gate) — and the
  `BoundExpression.TypeName` shim is gone (F-5). The metadata-binding spike's S7 intent landed.

**Not shipped — Draft v3 assumed these from 0.14.**

- **Metadata-binding S6 never merged.** `v0.14-metadata-binding-scoping.md` §3 S6 ("effect
  resolver reads `Type`; `_variableTypeMap` deleted") has no PR; the nullability workstream
  reused the S6/S7 labels (PRs #1067, #1073 are different slices). `Effects/ExternalCallCollector.cs`
  still carries `_variableTypeMap` (`:58,244,254`) — receivers resolve from
  `ReceiverSymbol.TypeName` *strings* first, the map second. `EffectResolver.Resolve(string type,
  string method, string[] params)` (`Effects/EffectResolver.cs:48`) is string-keyed end to end,
  and nothing under `src/Calor.Compiler/Effects/` references `MetadataBinder`. Effect manifests
  and IL-derived effects feed that string lookup, beside the binder, not through it.
- **Lambdas are not function-typed yet.** `BoundLambdaExpression.Type` is a stringly
  `NominalBoundType("LAMBDA(...)")` (`Binding/BoundNodes.cs:2179`), not a `FunctionBoundType`.
  `§LAM` already parses a `§E` annotation (`Ast/LambdaNodes.cs:41`,
  `BoundLambdaExpression.DeclaredEffects`) that enforcement discards — `InferFromLambda`
  (`EffectEnforcementPass.cs:2942`) charges the body and ignores the declaration.
- **`Assumed` is not a lattice state.** It is a side table surfaced as Calor0419
  (`EffectEnforcementPass.cs:451-462`); `EffectResolutionStatus` is `Resolved | PureExplicit |
  Unknown`. `EffectSet.Unknown` is the only sentinel.
- **Signature resolution is 65.46%** over the pinned corpus (817/1248; MediatR 57.08%, Serilog
  92.04%, FluentValidation 64.25% — `bench/phase0-agent-native/metadata-binding-corpus-ledger.json`).
  The S5 bar was never recorded in the ledger and the CI pin that re-executes the measurement
  against the PR-body number does not exist. A third of BCL call sites therefore reach effect
  rows as `UnresolvedBoundType`.
- **The index holds no effect facts.** `ProjectIndexBuilder` never mentions effects;
  `EffectSummary` persists in the incremental build cache (`Incremental/BuildStateCache.cs:52`),
  keyed by name strings (`Effects/EffectSummaryBuilder.cs:71`). `impact` is transitive callers
  only (`ProjectIndex.cs:372-408`); the index records *declared* contracts, never an outcome
  (`:56-63`). `review-packet` computes impact from an in-memory call graph
  (`Reporting/ReviewPacket.cs:3-12`), not the index. MCP `calor_navigate`
  (`Mcp/Tools/NavigateTool.cs`) neither reads the index nor exposes callers/impact.
- **0.14 gates not adjudicated** (details and dispositions in §4.5): 3.5.1 null-state slice and
  adversarial null corpus (absent); 3.5.4 TIER1A (`docs/experiments/registry.json` empty; the
  reconstructed checker lives only on `origin/experiment/tier1a-rebuild-for-section-6`, not an
  ancestor of `main`); 3.5.5 self-migration (no `.calr` migrator; `SemanticsVersion.Major = 2`
  but `CheckCompatibility` still accepts 1.x, `SemanticsVersion.cs:38-46`, and has no caller in
  `src/`; no golden regeneration; the 1.32× headline is byte-identical across 0.14.0–0.14.3 with
  no discontinuity note); 3.5.6 elision (the differential suite reports 0 mismatches and
  elision coverage 40/65 — the re-enable condition is met — yet `--elide-proven-guards` remains
  opt-in).
- **Measurement prerequisites unstarted.** No real Calor arm
  (`tools/Calor.RoundTrip.Harness/TaskGen/TaskGenReportWriter.cs:76-86`: "the runner never
  invokes the Calor compiler"); #881 has no code or doc work (`run-bundle.sh` / `run-pair.sh`
  read `output_tokens` uncorrected); PR #944 — the §3.1 spike's pre-registration — is still open
  after S1–S5 shipped, which is the register-then-merge discipline breached once already.

### 4.1 Entry gate — the design doc, on the direction doc's own terms

`calor-direction.md` mandates `docs/design/effect-rows-in-the-type-system.md` before TIER2D-class
implementation starts. Its 2026-04-22 postscript (`calor-direction.md:117-118`) tightened the
terms after TIER1A failed on a designer-judgment gate, and Draft v3 cited only the weaker
original. 0.15 adopts the postscript's three conditions verbatim:

1. **An emitter spike producing actual compiler output**, not prose examples — on two named
   modules: the dogfood utility (`tools/calor-allowlist-audit/allowlist-audit.calr`) and one
   module from a pinned conversion subject, chosen and frozen in the design doc before the spike
   runs. Before/after output is committed alongside the doc.
2. **An external critique cycle** — at minimum two independent adversarial lenses on the doc
   (evidence and internal consistency) plus the test-lens ("which test observes each normative
   claim?"), with a review record in the doc.
3. **Priced blast radius in the doc itself**: the `IAstVisitor` surface (~236 methods × N
   implementers), `EffectSummary` cache migration, golden files under `tests/TestData`, LSP
   hover and `IdScanner` keyed on `BoundType.DisplayString` (the F-3B byte-identity discipline),
   conversion snapshots, and the round-trip harness.

**Demand denominator, registered before the doc is written.** The postscript reframed TIER2D as
"architectural refactor, not new capability." That is no longer accurate: Calor0418 *rejects*
higher-order code today. The instrument for "first-order ceiling" is therefore a count, frozen at
a named commit: Calor0418 firings plus function-typed-value Calor0419 assumptions over the pinned
corpus (in-repo committed `.calr` plus the three A-1.5.3 conversion subjects). Gate 2 reads from
this denominator; the design doc opens with it.

**Decisions the design doc must settle, each named so its absence is visible:**

- Row syntax on function types (the direction doc's `Int !{db:w, throw}` sketch is a placeholder,
  not a decision) and how it composes with `§E{}` on declarations.
- `Unknown` and `Assumed` as states of the row lattice (today: sentinel + side table), including
  what an `UnresolvedBoundType` receiver contributes to a row.
- The fate of `§LAM`'s dormant `§E` annotation — it becomes the lambda's declared row, or it is
  removed; it does not stay parsed-and-ignored.
- `FunctionBoundType` gains the effect slot; `BoundLambdaExpression.Type` becomes a
  `FunctionBoundType`.
- Keying: `EffectResolver`, manifests, and IL summaries key on bound symbol identity
  (`SymbolId` / metadata symbol), with the string path deleted rather than bypassed.
- Whether Calor0420/0421 fold into the general row-subtyping rule or remain separate codes.
- Rank-1 polymorphism: validated on the named combinator set (`Map`, `Match`, middleware,
  callbacks) by the emitter spike, or deferred via the exit ramp.
- Async/`Task`-shaped effects: deferred past 0.15 unless the spike finds it cheap (§5.1
  unchanged). `BoundLambdaExpression.IsAsync` today affects only a display string.

**Exit ramp (unchanged, pre-registered):** if rank-1 polymorphism fails to validate on the named
combinator set, 0.15 ships monomorphic rows with explicit Unknown/Assumed propagation and defers
polymorphism — still a shippable release that removes the first-order ceiling for the common
case.

### 4.2 Ship — tiered, with the cut line stated

MUST items gate the release; SHOULD items ship if they fit and defer to 0.15.x without
renegotiation; DEFERRED items are named so their absence is a decision.

**MUST**

- **E1 — Foundation (the never-merged metadata S6, plus the lambda type).**
  `ExternalCallCollector` and the enforcement pass resolve receivers and callees from
  `BoundExpression.Type` / bound symbols; `_variableTypeMap` is deleted (grep-pinned, per the
  original S6 discriminating pin); `EffectResolver`, manifests, and IL summaries key on symbol
  identity so external effects attach to typed external signatures; `BoundLambdaExpression`
  binds to a `FunctionBoundType`; an unresolved receiver contributes an `Unknown` row through
  `UnresolvedBoundType`, never a guessed one. E1 is the item everything else stands on, and it is
  exactly the work Draft v3 assumed 0.14 had done.
- **E2 — Effect rows** on function, delegate, and lambda types (monomorphic MUST; rank-1
  polymorphism behind the §4.1 ramp).
- **E3 — Effect-compatibility checking** at assignment, argument, return, override,
  interface-implementation, and rank-1 generic-instantiation sites, as one row-subtyping rule.
  Calor0420/0421 either fold into it or are re-pinned against it (design-doc decision).
- **E4 — Calor0418 replaced.** Accepted when the function value's row fits; a precise mismatch
  diagnostic when it doesn't (a new code from the free 0404–0409 / 0424+ range, not a re-purposed
  0418); explicit Unknown/Assumed propagation when metadata is incomplete. The
  `DelegateInvocation_*` pins are rewritten from "is an error" to "fits / does not fit".
- **E5 — Effects facet in the index.** Effect rows per declaration recorded in `ProjectIndex`;
  `calor query effects`; effect-change blast radius via the existing `impact` closure.
  `EffectSummary` is derived from the index or migrated into it — not maintained as a second,
  name-keyed store.

**SHOULD** (0.13 §2.2 leftovers this bullet used to hide inside "the agent workflow completes")

- **E6** `review-packet` reads the index (callers and effects) instead of its in-memory graph.
- **E7** MCP query surface reading the index: callers / callees / impact / effects.
- **E8** Contract outcomes recorded in the index and an invalidated-proofs facet.
- **E9** Affected-tests mapping (a new facet; no design exists today).

**DEFERRED** (named): async rows (§5.1); `PreconditionSuggester` on the typed CFG (#786
residual); reflection / `DynamicInvoke` / `dynamic`-receiver dispatch (gate 1 residual list).

**Cut line.** If E1 overruns, E5–E9 defer to 0.15.x; E1–E4 do not defer, because a release
titled "Composable Effects" without rows is not that release.

### 4.3 Honest measurement

- **A real Calor arm, as product** (unchanged from Draft v3): Option 1 from Call S — Calor0410
  enforcement genuinely in the agent loop — gets built regardless of any epoch. Today's harness
  ships round-tripped C# in both arms and never invokes the compiler (§4.0).
- **#881 is a scheduled slice, not a footnote.** The probe's cost leg reads `output_tokens` from
  `agent.json`, which under-counts 55× on subagent/compaction runs. Either the counter is
  corrected in `run-bundle.sh` / `run-pair.sh` with a pinned reproduction, or the cost leg is
  re-registered on a metric that does not depend on it. This lands before the PP registers.
- **The pre-registered fixture-scale probe**, under a NEW PP id, with the full discipline:
  **(i)** freeze event named — the PP registers in the A-annex (`docs/plans/agent-native-gates.md`,
  currently A-1.9; the A-1.10 bump *is* the freeze event) before any effect-row implementation
  merges; the empty `docs/experiments/registry.json` is the TIER1A-hypothesis registry and is not
  where this goes; **(ii)** fixture and defect classes frozen in the same annex entry, with
  honest-timing disclosure if authoring is concurrent (A-1.2 pattern); **(iii)** the four-valued
  outcome (hit / miss / underpowered / not-adjudicated) with a pre-registered decidability
  fallback; **(iv)** the "no large loop tax" margin stated numerically via the PP-W5 derivation
  (existing-epoch variance → null-simulation p95 → bootstrap-bound conjunction).
- **Register-then-merge has a precedent to repair first:** PR #944 (the §3.1 pre-registration)
  is still open while its spike shipped. It is merged as the historical record or closed with the
  discrepancy noted before the 0.15 PP registers — otherwise the discipline is aspirational.
- **No real-scale epoch** unless both registered re-entry conditions hold (≥70 evaluable tasks;
  a real Calor arm). Unchanged.

### 4.4 Release gates — instrument, denominator, freeze point

Conditional (move with their SHOULD-tier deliverable): gate 3's MCP leg, and gate 7. Unconditional:
gates 1, 2, 3 (CLI/SDK/build legs), 4, 5, 6.

1. **Effect laundering, closed classes.** Instrument: one adversarial pin per class, the
   `DelegateInvocation_*` pattern. Denominator: five classes — virtual override and interface
   implementation (already closed by Calor0420/0421; **re-pinned under rows**, since folding them
   into E3 could silently reopen them), delegate/function-value *assignment*, *argument*,
   *return* (closed by E3's typing rule, not by E4's rejection), and rank-1 generic
   instantiation. Freeze point: the class list is this gate; additions go in the release notes'
   **named residual list** (reflection, `DynamicInvoke`, `dynamic` receivers, BCL-returned
   delegates, event handlers — whichever are not closed). "These classes closed", never "no
   callback can".
2. **Higher-order expressiveness.** Instrument: the §4.1 demand denominator re-counted at the
   release commit. Denominator: the registered combinator set plus the frozen Calor0418/0419
   count. Bar: Calor0418 firings on the registered classes go to zero without `--permissive-effects`
   or interop wrapping; the residual count and its classes are published.
3. **Surface agreement.** Instrument: `EditScriptIdentityTests`. Denominator: the registered
   edit-script corpus, extended with at least one effect-row edit script (ES-08) registered
   **before E2 merges**, and the freeze record corrected from 6 to the 7 members already in tree.
   CLI, SDK, MCP, clean, and incremental builds agree byte-for-byte on every effect result. PR
   #968's "equalized defaults" claim becomes a pinned property rather than a PR-body sentence.
4. **The probe adjudicates** at its frozen thresholds under its four-valued outcome. Unchanged.
5. **Compatibility, restated over the corpus that exists.** Draft v3's denominator — "the repo's
   migrated `.calr` corpus" — does not exist because §3.3's self-migration did not execute
   (§4.0). Denominator: the committed `.calr` corpus **as it is** (unmigrated; mixed `§SEMVER`
   declarations) at a commit frozen at the 0.15 branch cut. Instrument: builds and tests green
   under the 0.15 compiler. First-order `§E` compatibility is claimed over that corpus, not
   universally. The self-migration debt is dispositioned in §4.5, not assumed away.
6. **Resolution floor (new).** Instrument: the metadata-binding corpus ledger, re-executed in CI
   (closing the S5 pin that was scoped and never built). Denominator: the pinned conversion-subject
   commits. Freeze point: **65.46% at v0.14.3 is the floor** — E1's re-keying may not lower it,
   and any raise is reported, not required. This is the guard against "symbol-resolved" being
   achieved by resolving fewer symbols.
7. **Index/query correctness, effects leg** (conditional on E5–E7 shipping in 0.15.0). The golden
   query corpus gains effects-facet ground truth authored from the fixture, not recorded from the
   implementation (the `QueryGoldenTests` discipline).

### 4.5 Carried 0.14 debt — dispositioned, not inherited silently

| Item | State at v0.14.3 | 0.15 disposition |
|---|---|---|
| 3.5.6 elision re-enable | Condition met: 0 mismatches over the pinned modeled-forms denominator, coverage 40/65 published; still opt-in | **Flip default-on in 0.15.0** as its own PR, with the coverage fraction in the release notes; if it is *not* flipped, the release notes say why |
| 3.5.5 self-migration (2.0.0) | `Major = 2`; 1.x accepted silently; no migrator; no golden regen; no re-baseline | **Split.** (a) Wire `SemanticsVersion.CheckCompatibility` so a declared 1.x file gets a *diagnostic* (Warning; Error deferred until a migrator exists) — one PR, 0.15. (b) Migrator, golden regeneration, benchmark re-baseline: **deferred by decision** to the first release with a second real user of 2.0.0 semantics; tracked as an issue; gate 5 restated accordingly |
| 3.5.4 TIER1A adjudication | Registry empty; checker on a non-ancestor branch | **Not a 0.15 gate.** Published in 0.15.0 release notes as "not run" (an honest negative row), or run in 0.15.x under its own §6.3 matrix if a window opens. §7's "on the record either way" is satisfied by the published not-run, not by silence |
| 3.5.1 null-state slice + adversarial null corpus | Absent | 0.15.x candidate, after E1; not a prerequisite for rows (unchanged reasoning) |
| #1082 (nullability follow-ons: BCL user-ref return flow, F-3C, member-access flow, Calor0208-vs-0274) | Epic open; PR #1078 draft | **Sequenced after E1.** Item 1 changes what `MetadataBinder` emits for every reference-type return — the same surface E1 re-keys on. Landing it first means re-doing E1's pins; landing it after means one migration |
| #845 (unsigned obligation modeling) | Open | 0.15.x, unchanged priority |
| #875 (non-null `str` root cause) | Open; the `str` scope shipped in 0.14.0–0.14.3, epic tracks the remainder | Closes when #1082 items 1–3 land; no 0.15 gate |
| PR #944 (§3.1 pre-registration) | Open after the spike shipped | Resolved before the 0.15 PP registers (§4.3) |
| PR #982 (gate 7 CLI leg), #981, #976 | Open docs/CI PRs | Merge or close in the 0.15 kickoff sweep; none gates 0.15 |

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
plan's history is legible; their disposition is the closing PR.

| Issue | Disposition |
|---|---|
| #760, #769, #770, #771, #772, #775, #776, #777 (conversion P0s), #766, #768, #847 (P1s) | **Demoted** to demand-driven per §3.4; a named failing fixture re-opens any of them. (#766, #768 were nonetheless closed on main post-0.13.2 — see PR #981's inventory.) |
| #761 (compilation ⇒ Roslyn-valid C#) | **Closed.** Enabled by metadata binding; the §3.5.2 ledger is the residual instrument (gate 6 in §4.4 carries it) |
| #762 | **Closed** (0.13 MUST, PR #900 completion) |
| #763 (structural control-flow codegen) | **Closed** 2026-08-14 (with the #786 re-platform) |
| #764 | **Closed** (0.13 MUST) |
| #765, #767 (LSP) | **Closed** (PRs #921, #926, #980) — LSP consumes `SymbolId`s; the MCP *query* surface did not ride with it and is §4.2 E7 |
| #773 (FeatureSupport executable contract) | Partially discharged by the PP-S4 registry (§2.5 gate 6); remainder demand-driven |
| #778 | **Closed** (0.13 MUST) |
| #779 | **Closed** 2026-08-11 — the differential program exists and is green (0 mismatches, coverage 40/65); the **re-enable itself is the open half**, dispositioned in §4.5 |
| #780 | **Closed** |
| #781 (contract inheritance/modules) | **Closed** 2026-08-13 — removed from the §4.1 design-doc scope list |
| #782 (obligation guards on program state) | **Closed** 2026-08-13 |
| #783 | **Closed** (PR #960) |
| #784, #786 | **Closed** (PRs #969, #970) — no longer 0.15 work; residuals in §4.0 |
| #785 | **Closed** (PR #968) — fail-closed shipped; the type-system unification is §4.2 E1–E4 |
| #787 (functional Sdk) | Closed by v0.12.1's first publish plus the shipped dogfood utility (`tools/calor-allowlist-audit`, CI-built via `Calor.Sdk`) |
| #788 (MSBuild determinism) | **Closed** 2026-08-10 — §2.5 gate 2 holds on the registered edit-script corpus |
| #789 (hermetic natives) | **Closed** 2026-08-12 |
| #790 (truthful release gates) | Closed when 0.13 shipped with the triple-form gates; §4.4 continues the form |
| #791 (generated exhaustive infra) | **Closed** (PR #1023, "Generate exhaustive AST infrastructure") |
| #792 (telemetry opt-in) | Closed |
| #793 (audit epic) | Tracking issue; this table is its disposition |
| #845 (unsigned-obligation modeling) | **Open** — 0.15.x (§4.5) |
| #851 (task-gen filter precision) | Retired with the venue; re-opens only under §4.3's re-entry conditions |
| #859, #884 (Z3 CI flake) | 0.13 (§2.2) |
| #874, #879, #883 | Closed (0.13 MUST) |
| #875 (non-null `str`) | **Open** — `str` scope shipped across 0.14.0–0.14.3; remainder tracked by #1082 (§4.5) |
| #881 (agent token metrics 55× under-count) | **Open, no work yet** — a scheduled 0.15 slice before the probe registers (§4.3) |
| #1082 (v0.14 nullability follow-ons epic) | **Open** — sequenced after §4.2 E1 (§4.5) |
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
classes 0.14 actually shipped (`str`, arrays, whitelisted generics, Annotated user references — the
null-state differential oracle of §3.5.1 is 0.15.x work, and TIER1A's outcome is on the record as a
published not-run unless §4.5 changes that), honest contracts with a published falsification record
and a visible elision-coverage fraction (40/65 at v0.14.3, default-on per §4.5), and compositional
effect safety for the registered combinator set — with the claim instrumented at fixture scale
under a pre-registered four-valued PP, and a standing, quantitative path back to real-scale
measurement.

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

One source-state lens, run before 0.15 work starts: every §4 claim checked against the code, CI,
GitHub issue state, and the v0.14 scoping docs. Result: NEEDS-FIXES; all findings applied in
Draft v4. §2 and §3 are left as written (historical record); §0's #785 clause corrected.

- **Stale scope (CRITICAL)** — §4.2 listed #784/#786 ("analysis re-platform lands here") and #785
  fail-closed as 0.15 deliverables; all three closed COMPLETED 2026-08-13 (PRs #969, #970, #968),
  #783 via #960. Gate 1's "four dispatch classes" counted override and interface implementation
  as future work; Calor0420/0421 already close them. → §4.0 inventory; §4.2 rebuilt; gate 1
  restated as five classes with two re-pinned.
- **Foundation assumed from 0.14 that did not land (CRITICAL)** — the effect resolver is still
  string-keyed (`EffectResolver.cs:48`; `_variableTypeMap` live in `ExternalCallCollector.cs`);
  metadata-binding S6 never merged (the nullability workstream reused the label); lambdas bind to
  a stringly nominal type; `MetadataBinder` is unreferenced under `Effects/`. Draft v3's one-line
  "effects resolve with type binding, not beside it" was the largest item in the release and
  was priced as a bullet. → §4.2 E1 is the named foundation; gate 6 (resolution floor) guards
  it.
- **Gate 5 had no denominator (CRITICAL)** — "the repo's migrated `.calr` corpus" presupposed
  §3.3's self-migration, which did not execute (no migrator; 1.x silently accepted;
  `CheckCompatibility` unwired). → Gate 5 restated over the corpus as it is; self-migration
  split and dispositioned in §4.5.
- **Undelivered deliverables hidden in one bullet (Major)** — "the agent workflow completes"
  required review-packet→index, contract outcomes in the index, an MCP query surface, and a
  tests mapping, none of which exist. → Tiered as E6–E9 SHOULD with a stated cut line.
- **Entry gate cited the weaker terms (Major)** — `calor-direction.md`'s postscript requires an
  emitter spike with real output, an external critique cycle, and priced blast radius; Draft v3
  cited only "design doc + bounded spike". → §4.1 adopts all three, adds the Calor0418/0419
  demand denominator, and enumerates the decisions the doc must make.
- **Measurement prerequisites unstarted (Major)** — #881 has zero work; two registries exist and
  the plan named neither; PR #944 breached register-then-merge without a disposition. → §4.3.
- **Carried debt without rows (Major)** — 0.14 gates 3.5.1/3.5.4/3.5.5/3.5.6 unadjudicated;
  epic #1082 absent from §5.2; twelve §5.2 rows referenced closed issues as future work. →
  §4.5 table; §5.2 refreshed.
- **Minors** — freeze record says 6 edit scripts, tree has 7 (gate 3); scoping doc's
  `contract-outcomes` facet shipped as `contracts` and records declarations only (§7 wording);
  #781 removed from the design-doc scope list (closed).

Declined findings: none.

Verified clean at count precision: 18 Calor04xx codes defined with 0404–0409 and 0424+ free;
7 edit scripts; 6 query facets; ledger 817/1248 = 65.46%; differential suite 0 mismatches,
40/65 eliding; `SemanticsVersion.Major = 2`; `FunctionBoundType` has no effect field.
