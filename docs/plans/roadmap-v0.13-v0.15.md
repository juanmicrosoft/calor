# Roadmap — v0.13 / v0.14 / v0.15

**Date:** 2026-08-10
**Status:** Draft v3 — merged from two independent proposals (Claude + Codex), then revised under
adversarial review round 1 (evidence 80%, strategy 60%, measurement 40%) and round 2 on the revised
draft (internal consistency 75%, new-claims evidence 85%) — all NEEDS-FIXES; all findings from both
rounds applied, dispositions in §8.
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
  surface; walker-level enforcement is still fail-open per #785 and is closed in 0.15); `calor
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
   **Status (2026-08-15).** Both legs are now instruments in CI rather than post-publish habits:
   `sdk-package-consumer` (5 RIDs, Calor0712 canary in the MSBuild task context) and
   `cli-tool-consumer` (the frozen 3 RIDs — win-x64, linux-x64, osx-arm64 — packing `calor`,
   installing it from a local feed with a cold package cache, and requiring a *solver verdict*:
   contracts Proven, none Skipped, no Calor0710). The VSIX leg is retired with the extension
   itself (#952). The dropped osx-x64 RID has no runner, so its amended oracle is reproduced on
   every leg by removing the runner's own Z3 native from the installed tool — the same state an
   Intel-mac consumer is in — and asserting documented degradation. **Building that oracle found
   the gate's own failure mode live in the shipped CLI**: `Calor0710` was `info` severity and the
   text report prints only errors and warnings, so a solverless consumer saw 14 `Skipped`, exit 0,
   and no stated reason outside `--format json`. Fixed in the same PR.
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

**Thesis:** effects become part of function types, so Calor safely expresses higher-order code
instead of rejecting it.

### 4.1 Entry gate — the design doc the direction commitment already requires

`calor-direction.md` mandates a dedicated design doc
(`docs/design/effect-rows-in-the-type-system.md`) **before implementation starts** on TIER2D-class
work, and calls it a 2–3 month commitment with compiler-architecture consequences. 0.15 honors
that: the design doc plus a **bounded spike** are the version's entry gate, with a **pre-registered
exit ramp**: if rank-1 effect polymorphism fails to validate on the named combinator set (`Map`,
`Match`, middleware, callbacks), 0.15 ships **monomorphic effect rows with explicit
Unknown/Assumed propagation** and defers polymorphism — still a shippable release that removes the
first-order ceiling for the common case. The 0.14 flow checker got exactly this discipline
(TIER1A); the language's centerpiece feature gets no less.

### 4.2 Ship

- **Effect rows** on function, delegate, and lambda types (monomorphic MUST; rank-1 polymorphism
  behind the §4.1 exit ramp).
- **Effect-compatibility checking** at assignment, argument, return, override, and
  interface-implementation sites.
- **Calor0418 replaced**: accepted when the function value's effect row fits; a precise mismatch
  when it doesn't; explicit Unknown/Assumed propagation when external metadata is incomplete.
  Effect enforcement becomes symbol-resolved, exhaustive, and fail-closed (#785) as a property of
  the type system rather than a walker — and the index's **effects facet turns on** (deferred from
  0.13 §2.2), now derived from typed signatures.
- **Effect manifests + IL-derived effects feed typed external signatures** — effects resolve with
  type binding, not beside it.
- **Analysis re-platform lands here**: bug-pattern analyzers on the typed CFG (#786), CFG-based
  symbol-resolved taint (#784).
- **Semantic index computes effect-change blast radius**, invalidated contracts, affected tests;
  the agent workflow completes: propose change → query impact → type/effect check → re-prove
  affected contracts → run impacted tests → emit review packet.

### 4.3 Honest measurement

- **A real Calor arm, as product.** Option 1 from Call S (Calor0410 enforcement genuinely in the
  agent loop — no contracts required, cheaper than the overlay) gets built regardless of any epoch;
  it is also simply the honest demo of the product.
- **A pre-registered fixture-scale probe under a NEW PP id**, registered in the A-annex with the
  full discipline, not a gesture at it: **(i)** freeze event named — the PP registers *before any
  effect-row implementation merges* (register-then-merge); **(ii)** fixture and defect classes
  frozen in the same annex entry, with honest-timing disclosure if authoring is concurrent (the
  A-1.2 pattern) — guarding the tautology route where fixtures authored after the feature make the
  hit deterministic-by-construction; **(iii)** the **four-valued outcome** (hit / miss /
  underpowered / not-adjudicated) with a pre-registered decidability fallback, not bare hit-or-miss;
  **(iv)** the "no large loop tax" margin stated **numerically**, derived from existing epoch
  variance (the PP-W5 derivation pattern) — and **#881 (agent token metrics under-count 55× on
  subagent/compaction runs) is a named prerequisite of the probe's cost leg**.
- **No real-scale epoch unless both registered re-entry conditions hold** (≥70 evaluable tasks
  surviving build+recovery, and the real Calor arm). If they hold, re-entry is adjudicated under a
  new pre-registered PP; if not, that is published, not implied.

### 4.4 Release gates

1. **Effect laundering, closed classes**: each of the four dispatch classes — delegate assignment,
   virtual override, interface implementation, generic instantiation at rank-1 — is closed by a
   typing rule with an adversarial test per class (the `DelegateInvocation_*` pin pattern), and a
   **named residual list** of dispatch forms not yet closed ships in the release notes. "These
   classes closed", never "no callback can" — surface not exhausted.
2. **Higher-order expressiveness**: the named combinator set type-checks without permissive effects
   or interop wrapping (per the §4.1 spike's registered set).
3. **Surface agreement**: CLI, SDK, MCP, clean and incremental builds agree on every effect result
   over the registered edit-script corpus (extending §2.5 gate 2).
4. **The probe adjudicates at its frozen thresholds** under its registered four-valued outcome —
   any of the four values is a result; only an unregistered one isn't.
5. **Compatibility**: the repo's migrated `.calr` corpus at a commit frozen at the 0.15 branch cut
   (denominator + freeze point) builds and tests green under the 0.15 compiler (instrument).
   First-order `§E` compatibility is claimed over that corpus, not universally.

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

| Issue | Disposition |
|---|---|
| #760, #769, #770, #771, #772, #775, #776, #777 (conversion P0s), #766, #768, #847 (P1s) | **Demoted** to demand-driven per §3.4; a named failing fixture re-opens any of them |
| #761 (compilation ⇒ Roslyn-valid C#) | **0.14** — metadata-backed binding + typed representation is the enabling work; gate = §3.5.2's ledger |
| #762 | **0.13 MUST** (§2.1) |
| #763 (structural control-flow codegen) | Deferred; revisit with #786 in 0.15 |
| #764 | **0.13 MUST** (§2.1) |
| #765, #767 (LSP) | **0.13.x/0.14** (§2.3) |
| #773 (FeatureSupport executable contract) | Partially discharged by the PP-S4 registry (§2.5 gate 6); remainder demand-driven |
| #778 | **0.13 MUST** (moved; §2.1) |
| #779 | **0.13** policy resolution + differential program (§2.1); zero-mismatch bar lands 0.14 (§3.5.6) |
| #780, #845 | **0.14** (§3.2) |
| #781 (contract inheritance/modules) | Deferred to the §4.1 design doc's scope decision |
| #782 (obligation guards on program state) | **0.14** where the null-state slice enables it (§3.2) |
| #783 | **0.14** (null-state slice) |
| #784, #786 | **0.15** (§4.2) |
| #785 | **0.15** (§4.2) |
| #787 (functional Sdk) | Substantially closed by v0.12.1's first publish; residual = Sdk docs + the §2.2 dogfood proving the consumption path |
| #788 (MSBuild determinism) | **Program, not an item**: #883 (its live instance) is 0.13 MUST; #788 closes when §2.5 gate 2 holds on the registered corpus |
| #789 (hermetic natives, P1) | **Partially** landed v0.12.1 (checksum/provenance subset only); residual = §2.4's osx decisions + arch assertion **plus** the still-open hermetic items: no-network ordinary builds, the hard-coded deps.json mutation, SelfTest copy-back, SBOM/provenance artifacts, lockfiles — deferred, revisit at 0.14 |
| #790 (truthful release gates) | **Executed by this plan's gate rewrite** (§2.5/§3.5/§4.4 instrument-denominator-freeze triples); closes when 0.13 ships with those gates in CI |
| #791 (generated exhaustive infra, P2) | Deferred; natural companion to the #762 rebuild if it pays for itself there |
| #792 (telemetry opt-in) | Already **closed** on GitHub; 0.13 re-verifies the docs inventory against shipped payloads (§2.2) |
| #793 (audit epic) | Tracking issue; this table is its disposition |
| #851 (task-gen filter precision) | Retired with the venue; re-opens only under §4.3's re-entry conditions |
| #859, #884 (Z3 CI flake) | **0.13** (§2.2) |
| #874, #879, #883 | **0.13 MUST** (§2.1) |
| #875 | **0.14** (§3.2) — the issue covers `str` only; arrays and user reference types are this plan's extension |
| #881 (agent token metrics 55× under-count) | **0.15 prerequisite** for the probe's cost leg (§4.3) |

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

Narrower and much more concrete than where v0.12 left it. C# still wins on ecosystem, familiarity,
and density. Calor wins where **agent-authored correctness and explainable change impact are the
product**: a queryable semantic project model (`calor query` + MCP, anchored by the §2.5 golden
query corpus, not just build-mode identity), null safety enforced across .NET boundaries (closed
classes, differential-oracle-tested, with the TIER1A matrix's outcome — ship *or* published
negative — on the record either way), honest contracts with a published falsification record and a
visible elision-coverage fraction, and compositional effect safety for the registered combinator
set — with the claim instrumented at fixture scale under a pre-registered four-valued PP, and a
standing, quantitative path back to real-scale measurement.

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
