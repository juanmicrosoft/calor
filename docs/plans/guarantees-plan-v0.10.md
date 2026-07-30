# The Guarantees — v0.10 Execution Plan

**Status:** Draft v2 — adversarial review round 1 applied (verdict on v1: 60 %; dispositions in §10). Proof points (§5) and the deviation sign-off (§8) remain the review targets.
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-29
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) (v4.1 incl. §9 postscript) and [`agent-native-gates.md`](agent-native-gates.md) (Annex A at A-1.2). Predecessor: [`loop-plan-v0.9.md`](loop-plan-v0.9.md), whose Call 2 (§6.2) resolved **PROCEED** on 2026-07-29 (PP-W1 HIT +5, PP-L5 HIT ~35 %, PP-L6 PASS; record: `bench/phase0-agent-native/epochs/m5-compare-001/VERDICT.md`, merged `ce7708af`). Per the v0.9 milestone map (§8), v0.10 is **The Guarantees** — Phase 2b terrain (verification depth) — and this plan is where the Guarantees-before-Wedge sequencing deviation must be signed off (§8 below), not drifted into.
**Target release:** v0.10.0. The v0.9.0 release itself has not been cut at planning time (main is versioned 0.8.0 with the loop program in `[Unreleased]`); cutting v0.9.0 is a release-process action outside this plan and does not gate G1.

---

## 0. Objective

> **Make every verification verdict the agent sees trustworthy — sound, honestly statused, and provenance-tagged — then widen the proven surface where the wedge evidence says it pays, and prove the upgrade with paired measurement.**

The v0.9 program earned its PROCEED on two observations: the loop tooling reduces convergence cost (PP-L5), and enforcement catches seeded defects C# tooling misses (PP-W1). The PP-W1 wedge was **concentrated in the runtime-contract channel** (W5-B: C# caught 0–1/3 per pair where Calor caught 3/3, via `ContractViolationException` at smoke time) — the differentiated claim's first observed instance runs through `§Q`/`§S`. But the machinery behind that channel is currently **untrustworthy in both directions**: it refutes correct code with fabricated counterexamples (#807), and where a contract *is* genuinely violable it reports the wrong model for the right verdict — the flagship "proven" sample reports 7/10 "potentially violated," 3 purely spurious and 4 masking real overflow violations (§1 item 2) — while on the eliding side it deletes runtime guards on an existence proof (#755 — satisfiability conflated with validity). v0.10's job is to close that gap: verdict soundness first, honest status/provenance model second, proven-surface depth third — and then measure whether depth converts the wedge's runtime catches into build-time proofs.

Non-goals (unchanged from strategy 2b's "deliberately absent" list): ghost state/lemmas, object-invariant methodology, quantifier trigger engineering, JML-style exceptional specs, async/concurrency verification. Also out: the v0.11 adoption push (Call 3 one-way door), effect-manifest curation (`calor import` effects half — v0.11 per v0.9 §8), and any re-run of the parked 2a gate.

---

## 1. Audit delta: verification at Call 2 (updates strategy §1)

**Audit rule (carried from v0.9 §1):** every file:line anchor is grep-verified on the audit date against a recorded commit. This audit: **2026-07-29, commit `ce7708af`**. Strategy-doc references cite sections, not lines.

1. **The status model is spec-only.** Strategy §5.1 specifies seven statuses (`Proven`/`Disproven`/`Assumed`/`Unknown`/`Timeout`/`Unsupported`/`Unavailable`), an orthogonal `Justified` disposition, a `vacuous` flag on `Proven`, and content-addressed assumption sets. The implemented `ProofStatus` has **five** values (`Verification/ProofOutcome.cs:15–27`); `Assumed` appears zero times in `ProofOutcome.cs`; solver-unavailable folds into `Unknown`. The WS1 choke point (v0.9 D1.2) is real and conformance-tested, but it chokes on a narrower vocabulary than the strategy's soundness model requires.
2. **Result-referencing postconditions are unsound in the refuting direction (#807) — and the flagship sample is itself unsound (found in review of this plan's v1).** The obligation is `Q ⊨ S[result]` with `result` unbound by the body; `samples/Verification/proven-contracts.calr` reports **Proven 3 / Disproven 7** (reproduced this audit date with the Release binary). The 7 refutations split into two classes: **3 are purely spurious** (`Identity`, `Max` ×2 — provable once `result` is bound), while **4 flag genuinely violable contracts with fabricated models** — under the committed bit-vector/two's-complement semantics (`docs/verification-modeled-forms.md`) and C# unchecked runtime semantics alike, `Square(46341)` wraps negative, `AbsoluteValue(int.MinValue)` is the classic `Math.Abs` violation, and `AddPositive(int.MaxValue, 1)` wraps — the sample named "proven-contracts" would throw at runtime, and #807 itself classes this overflow shape as honest refutation. So a *correct* verifier still refutes 4/7 as-written — with different, genuine models. Spurious counterexamples (`Identity: x=0, result=4294967295`) actively misdirect agents, and `calor verify` exits 1 on them (intentional since #754). The current honest proven surface is single-parameter inequality chains — exactly the D1.5 `proven.calr` shape; the D3.3 latency fixture's 2,049 contract markers had to be constrained to it.
3. **Precondition "Proven" is unsound in the eliding direction (#755).** `VerifyPrecondition` checks *satisfiability* (`Verification/Z3/Z3Verifier.cs:34–35,69`), and the emitter treats that as license to elide the runtime guard (`CodeGen/CSharpEmitter.cs:1772,1789–1792` — "PROVEN: Precondition always satisfiable"). `∃` is not `∀`: callers violating the precondition sail through unguarded. Strategy §5.2 rule 1 (elide only on unconditional, non-vacuous `Proven`) is violated on main today.
4. **`Unsupported` is enforced by scattered exception fallbacks, not a positive whitelist.** The normative modeled-forms list exists as documentation (`docs/verification-modeled-forms.md`, v1) with the rearchitecture explicitly deferred to Phase 2b item 5. Known semantic divergences D1–D7 are tracked there as defects.
5. **Vacuity has two guarded channels out of three** *(corrected in v2 — v1 repeated a stale strategy-§1 claim without re-verification, the predecessor plan's signature audit failure)*. The obligation side is guarded twice over: `Obligations/ObligationSolver.cs:144–161` runs a consistency pre-check over the asserted set ("vacuous discharge prevented"), and `FactCollector.cs` has been flow-scoped since #686 (`4b0dd55a`: dominating-guard `ScopedFact` + conservative assignment kill) — the "known live defect" recorded in strategy §1 is fixed on main. The **live** channel is contract-level: `Z3Verifier.VerifyPostcondition` asserts the preconditions and negated postcondition with no SAT pre-check, so an unsat `§Q` set yields vacuous `Proven`. There is no `vacuous` flag anywhere in code.
6. **Multi-module programs cannot link (#809).** Cross-module calls emit unqualified C# (CS0103 under csc); the qualified spelling trips strict-unknown effect enforcement (`Calor0410 Unknown:*`). No working spelling exists. This bounded WS5's W5-C to intra-module laundering and silently constrains every multi-module Guarantees fixture.
7. **What the wedge evidence actually says.** `ws5-probe-001` (M-W1: calor 9/9, C# 4/9, Δ+5): the delta came from the **runtime-contract channel** (all three W5-B pairs) plus effect build-block (W5A-003) and a length-3 laundering chain (W5C-003); where both arms caught (4 pairs), the agent noticed without enforcement forcing it. The wedge's observed value is contract enforcement; none of it yet comes from *proof* — every W5-B catch was a runtime exception, the weaker half of the Guarantees claim.

---

## 2. Workstreams

Ordering rationale: WS-G1 is the soundness floor — nothing downstream is publishable while the verifier refutes correct code and unsoundly elides guards. WS-G2 gives every later verdict an honest vocabulary (and the envelope carries it, reusing the v0.9 instrument). WS-G3 spends depth effort only where WS-G1/G2 make it trustworthy and §5's measurement can price it. WS-G4 is independent and unblocks multi-module fixtures early.

### WS-G1 — Sound core (size: M)

- **D-G1.1 Body→result binding (#807).** Encode `result = body` into the postcondition obligation for encodable bodies: single-`§R` expression bodies and `§IF`/multi-return bodies via if-then-else over the modeled-forms surface. Bodies outside the encodable set (loops, calls, effects, unmodeled expressions) route the *postcondition* outcome to `unsupported` at the choke point — never `refuted` with a free `result`. A refutation must mean the body provably violates the contract.
- **D-G1.2 Elision soundness (#755).** The emitter never elides a precondition guard on a satisfiability result. Verifier side: precondition outcomes get an explicit polarity (`satisfiable` evidence is not `Proven`-for-elision); emitter side: only a genuine ∀-proof may elide, per strategy §5.2 rule 1. Smallest safe form (issue option 1) lands first; the typed fix (option 2) lands with WS-G2's status model.
- **D-G1.3 Contract-level vacuity guard.** Precondition-set SAT pre-check in `VerifyPostcondition`; unsat `§Q` set ⇒ `Proven(vacuous)` (never elides, raises a diagnostic per strategy §5.1). The obligation-side channels are already guarded (§1 item 5); a regression fixture pins that behavior rather than re-implementing it.
- **D-G1.4 Corpus pinning + sample repair.** (a) **Repair `proven-contracts.calr`**: 4 of its contracts are genuinely violable under the committed overflow semantics (§1 item 2) — add overflow-excluding `§Q` bounds so the sample's name is true; a CHANGELOG-visible sample-soundness fix (§7 risk 6), with the current unbounded shapes preserved as honestly-refuted-with-genuine-model fixtures. (b) D1.5 outcome corpus gains: proven-with-result fixtures (Identity, bounded-Square shapes), the honest-overflow-refutation fixtures from (a) distinguished from the #807 spurious class, a vacuous-precondition fixture, an elision-soundness test (compile `--verify --contract-mode debug`, call with violating argument, assert the guard throws). CI conformance extends to all of them. The corpus edit lands and freezes **before** PP-G1 is adjudicated.

Exit criteria: the **repaired** `proven-contracts.calr` reports 10/10 proven; zero spurious refutations across the extended corpus, and every refutation carries a genuine model reproducible as a runtime guard violation; the elision-soundness test passes; choke-point conformance still holds (no status assigned outside `ProofOutcome.Assign`).

### WS-G2 — Honest status and provenance model (size: M–L)

Implements strategy §5.1 in code — the vocabulary the Guarantees claim is stated in.

- **D-G2.1 `Assumed` tier.** New status carrying a per-proof, content-addressed assumption list (callee summaries, aliasing assumptions, interop-unknown markers). Transitive; listed per-proof; never aggregates into `Proven`; never elides. Consumers: envelope, `verify` text/JSON output, exit-code policy (assumed ≠ failure, like unknown).
- **D-G2.2 Status completion.** `Unavailable` split out of `Unknown` (solver missing vs solver gave up); `vacuous` flag on `Proven` (from D-G1.3); reason fields preserved through the choke point. `ProofOutcome.Assign` remains the single exit; conformance test extends to the new vocabulary. **In scope: the three legacy mapping sites** (`ProofOutcome.ToContractStatus`/`ToObligationStatus`/`ToImplicationStatus`, `Verification/ProofOutcome.cs:261–285` — `ObligationStatus` today folds Unknown *and* Timeout into `Timeout`), with the rule frozen here: **`Assumed` never maps to a legacy Proven-equivalent** in any consumer.
- **D-G2.3 Positive-whitelist `Unsupported` (2b item 5).** Rearchitect the scattered exception-fallback sites across `Z3Verifier.cs`/`ContractTranslator.cs` into a positive modeled-forms whitelist in code, with `docs/verification-modeled-forms.md` regenerated from or conformance-checked against it — the document stops being the only enumeration. "A blacklist by accident" (strategy §1.2) ends here. **In scope: the second, reduced translator** (`BoundExpressionTranslator` in the bug-pattern checkers, modeled-forms §5 — signed-only; unsigned comparisons and div/mod are mis-modeled behind agent-visible Calor09xx verdicts): it adopts the same whitelist or routes unsigned forms to an honest non-verdict.
- **D-G2.4 Envelope schema v1.2.** The `verification` payload carries the full status vocabulary, `vacuous`, and the assumption list; additive minor bump per the schema's own rules; D1.4 conformance + D1.5 corpus extended per status. The v0.9 instrument is the delivery vehicle — no new formats.
- **D-G2.5 Exceptional-path assumptions (2b item 6) — `Assumed`'s first producer.** `§S` holds on normal return only; exceptional paths surface as a named assumption on affected proofs instead of the silent D5 divergence. Pulled into WS-G2 (from the depth workstream, where v1 had it) because without it nothing in the compiler can emit `Assumed` on real input until G4 — the G2 exit criterion would be satisfiable only synthetically.
- **Priced but default-deferred:** the `Justified(who, why, when)` disposition layer. It is a human-workflow artifact with no consumer at bus factor 1; it enters only if a G3/G5 deliverable needs it (e.g. `Assumed`-heavy fixtures drowning signal). Deferral recorded here so it is a decision, not a gap.

Exit criteria: every verification outcome on the extended corpus reports one of the seven statuses; `Assumed` proofs list their assumptions and never elide, exercised on a **real producer** (D-G2.5), not synthetic fixtures alone; whitelist conformance test replaces fallback-site enumeration.

### WS-G3 — Depth: widening the proven surface (size: L; scope-gated, priced at G4 kickoff)

Ordered by wedge evidence (the W5-B channel is scalar-contract shaped) and by 2b's dependency order. Each item is a separate go/no-go at G4 kickoff — this workstream is where v0.10 can most easily drown, and the Spec# lesson (strategy §2.1) says prover incompleteness, not annotation cost, is the binding constraint.

- **D-G3.1 Scalar depth on the sound core.** With `result` bound, extend the encodable-body set pragmatically: local `§B` chains (SSA-style substitution), bounded conditional nesting, the D1–D7 divergence fixes that are cheap (narrow-type promotion D1, literal signedness D2). Target: the W5-B probe contracts (shipping-quote/loyalty-points/request-quota shapes) become *provable*, not just runtime-checkable. **PP-G3 depends on this item**: two of the three W5-B fixtures (`QuoteWithSurcharge`, `AwardWithFloor`) compute a local `§B` before branching — outside D-G1.1's encodable set — so descoping D-G3.1 structurally caps PP-G3's earliness leg at 1/3; the §6.1 G4 gate states the consequence.
- **D-G3.2 Calls-in-contracts (2b item 3).** Pure-function calls in `§Q`/`§S` with the full guard set: purity incl. reads, `§DEC` termination measure, assumption-set consistency check. Callee contract summaries enter as `Assumed` (D-G2.1) unless themselves proven. May descope to "modeled BCL functions only" (e.g. `Math.Min/Max/Abs`) if pricing busts the box.
- **D-G3.3 Frames and `old()` (2b items 1–2), scoped honestly.** Scalars, arrays, and disjoint whole objects — what the pairwise no-aliasing rule and Z3's array theory actually support; reachability aliasing surfaces as named `Assumed`, never proofs. **Default: does not run in v0.10.** It enters only if G4 kickoff finds D-G3.1/G3.2 landed under budget *and* PP-G3's feasibility check demands heap-shaped contracts. Recording the default here prevents scope drift into the hard Spec# terrain with measurement still pending.

*(v1's D-G3.4, exceptional-path honesty, moved to WS-G2 as D-G2.5 — review M5.)*

Exit criteria: registered depth corpus (see §4 M-G1) proven-rate reported; every widened form has corpus fixtures in all reachable statuses; no D1–D7 divergence silently remains (each is fixed or surfaces as `Assumed`/`Unsupported`).

### WS-G4 — Multi-module linking (size: S–M)

- **D-G4.1 Cross-module emission fix (#809).** One working spelling for cross-module calls: emitter qualifies bare-name cross-module targets (the binder/effect registry already resolves them — `CrossModuleEffectEnforcementPass` charges the callee's effects correctly), and/or the qualified spelling resolves through the cross-module registry instead of falling to `Unknown:*`. Both front-end and csc must accept the same program.
- **D-G4.2 Multi-module verification fixtures.** With linking fixed: a small multi-module contract corpus (cross-module calls under contracts, effects + contracts on the same chain), unblocking realistic Guarantees fixtures and un-bounding the W5-C laundering class for any future probe.

Exit criteria: the #809 repro compiles and links under MSBuild/csc with effects enforced, **scoped to modules within one project/invocation** (the module map `Calor.Tasks`/the CLI already hold at emission time; cross-*project* Calor→Calor references are outside the registry, outside #809's repro, and not priced here); multi-module corpus green in CI. Note the enforcement-side risk is confined to the alternative front-end spelling — qualified *emission* cannot break effect enforcement, which runs on the AST pre-emission.

### WS-G5 — Measurement (size: M; spend-gated)

- **D-G5.1 Metric + threshold registration (Annex A-1.3).** M-G1–M-G4 definitions (§4) and the PP-G3/PP-G4 thresholds (§5) enter the gates doc's Annex A as **A-1.3**, additive-only, frozen before any treatment epoch runs, with a feasibility check per the D4.5 discipline. A-1.3 must freeze the operational detail A-1.2 froze for M-W1: the **surfacing event** per channel (the first journaled envelope/build event carrying the seeded defect's refutation or block, per run), the **per-defect aggregation rule** (majority over runs per arm, the A-1.2 pattern), and the **weakening decision procedure** (M-G4). The earliness channel is deterministic-by-construction once M-G1/M-G2 pass in CI (the seeded W5-B bodies encode to genuinely-SAT obligations), so feasibility-by-determinism applies to the surfacing half — **and its stated limit applies symmetrically: what the epoch actually adjudicates is the agent-behavior residual** (does the agent act on a build-time refutation, or ignore/delete it), which determinism does not cover.
- **D-G5.2 Guarantees probe epoch.** Re-run of the WS5 probe pairs (all 9; the W5-B trio is the primary lens), **5 runs/arm** (up from ws5-probe-001's 3 — see M-G4's floor), v0.10 build vs v0.9 build (`ce7708af` archived as the control arm **now**, per the v0.9 D4.3 lesson — archive before treatment merges), simultaneous, same tasks, same model pins. **Each arm runs its own registered harness configuration** — the v0.9 arm per A-1.2 (static-verify channel excluded, as `ws5-probe-001` was registered and run: an arm whose verifier refutes every result-referencing `§S` cannot honestly run a verify gate); the v0.10 arm per A-1.3 (verify gate on). This is declared config *variance*, not drift: identical task/pins, per-arm-native tool configuration, recorded in pins.json. Consequences for what the comparison supports: **M-W1 catch totals are the cross-arm paired quantity** (both arms' native configs, ceiling noted — v0.9 scored 9/9, so "no regression" is the bar); **the earliness leg of PP-G3 is a single-arm property of the v0.10 configuration** (a product-configuration claim, the v0.9 arm-C mold — not a cross-arm attribution). **Attribution commitment (the v0.9 review-C2 rule):** the treatment arm is attributable only if main between `ce7708af` and G5 contains only this plan's work — otherwise an isolation build is constructed exactly as M5's arm B was.
- **D-G5.3 Published record + Call G.** Epoch record, adjudication of PP-G3/PP-G4, and the v0.10→v0.11 disposition (§6).

Exit criteria: A-1.3 frozen pre-epoch; control arm archived pre-merge; epoch run and adjudicated; spend authorized through `phase-2-spend-authorisation.md` with numbers before G5 kickoff (dollar figures are not invented in this doc).

---

## 3. Sequencing

Calendar boxes are estimates, confirmed or corrected at each milestone kickoff (v0.9 discipline). One epoch total (G5); no epoch spend before its authorization gate.

| Milestone | Contents | Box (est.) | Depends on |
|---|---|---|---|
| **G1** | WS-G1 sound core + corpus; **archive `ce7708af` as the v0.10 control arm** (tag, pins) before any treatment merge | 2–3 wk | — |
| **G2** | WS-G2 status/provenance model + envelope v1.2 | 3 wk | G1 |
| **G3** | WS-G4 multi-module linking (parallel to G2) | 1–2 wk | — |
| **G4** | WS-G3 depth, scope-gated per-item at kickoff | 4–6 wk | G1, G2 |
| **G5** | A-1.3 registration + feasibility, spend auth, Guarantees probe epoch, adjudication, **Call G** | 2 wk | G1–G4, A-1.3 |

The hard rule, carried from v0.9: the control build is **archived before treatment work merges** and both arms run **simultaneously at G5** — never a longitudinal comparison. G1's archive step is deliberately first.

---

## 4. Success metrics (definitions)

**Toolchain metrics** (CI, no agent):

- **M-G1 Proven-rate on the registered depth corpus** — % of corpus contracts reporting non-vacuous `Proven`, reported per corpus tier (sound-core tier / depth tier / multi-module tier). The corpus is committed and versioned; additions re-baseline, never silently move the rate.
- **M-G2 Verdict honesty** — over the extended D1.5 corpus: 100 % of outcomes in the closed seven-status set (whitelist-conformance-backed, extends M-E3), **zero refutations on known-proven fixtures** (the #807 regression pin), zero elisions without a ∀-proof (the #755 regression pin).

**Probe metrics** (agent runs, pinned model, per epoch — A-1.3 registers these):

- **M-G3 Catch earliness** — per injected defect (D5.1 set), the earliest channel that surfaced it per arm: **`build-proof` (verify refutation) > `build-block` (Calor0410-class enforcement) > `runtime-guard` (ContractViolationException at smoke) > `missed`** — proof and enforcement are separate channels (the v0.9 arm already scores 5 build-*block* catches; conflating them would let the headline "runtime catches become build-time proofs" overread its own metric). Surfacing event and per-defect aggregation frozen at A-1.3 (D-G5.1). M-W1 (catch/no-catch) is computed alongside for continuity with `ws5-probe-001`. M-G3 is meaningful only conditioned on M-G2 green on the same build (an unsound refute-everything verifier maximizes it) — stated as an adjudication precondition in the PP-G3 row.
- **M-G4 Prover-appeasement incidence** — on the contract-carrying pairs (the W5-B trio): iterations-to-green (gates §2 semantics) plus **contract-weakening incidence**, decided **mechanically, not by inspection**: diff the run's final contract set against the fixture's frozen set; deletion is weakening by definition; a modified contract is weakened iff `frozen ⇒ final` proves and `final ⇒ frozen` does not (the repo's own implication prover — the product dogfooding its measurement). Weakening-eligible runs = all runs on contract-carrying pairs, both arms; at 5 runs/arm this is 3 × 5 × 2 = **30 ≥ the 20-run adjudication floor** (the floor is designed to clear, not gestured at). This is the Spec# lesson made measurable: an agent facing `unknown`/`unsupported`/refutation will contort the contract until the prover accepts.

---

## 5. Proof points (go/no-go claims — review these)

**[P]** thresholds are provisional until D-G5.1's feasibility check confirms decidability and A-1.3 freezes them — moved before freezing, never after.

| # | Claim | Measurement | Threshold | On hit | On miss |
|---|---|---|---|---|---|
| **PP-G1** | The verifier is sound where it speaks | M-G2 (CI) | Zero spurious refutations on the pinned known-proven corpus; every refutation carries a genuine model reproducible as a runtime guard violation; zero unsound elisions; the **repaired** `proven-contracts.calr` (D-G1.4a) 10/10 — corpus repair lands and freezes before adjudication | Sound-core floor established; #807/#755 closed with regression pins | **Release blocker: v0.10 does not ship a verifier that refutes correct code or deletes guards it didn't prove** |
| **PP-G2** | Every verdict is honestly statused | Extended corpus: all outcomes in the closed set; `Assumed` lists assumptions, never elides; vacuity flagged | 100 % (whitelist-conformance-backed) | Strategy §5.1 model is real; envelope v1.2 frozen | Ship blocker for the same reason PP-L2 was: silent cliffs are the failure mode the program exists to remove |
| **PP-G3** | Verification depth converts the wedge — runtime catches become build-time proofs, and agents act on them | M-G3 on the Guarantees probe epoch (per-arm-native configs, D-G5.2); adjudicated only with PP-G1/M-G2 green on the treatment build | Two legs: (a) *cross-arm, paired*: v0.10 arm M-W1 = no regression from the v0.9 arm's 9/9; (b) *single-arm, v0.10 config* (product-configuration claim, not cross-arm attribution): ≥ 2 of the 3 W5-B defects surface at `build-proof` **and the agent's converged fix follows from the refutation** (not contract deletion) **[P]**. **Depends on D-G3.1** (two of three W5-B fixtures need `§B`-chain encoding); if G4 descopes it, PP-G3 is restated or demoted to reported-not-adjudicated at A-1.3, before the epoch — never after | The Guarantees claim has its observed instance: proofs, not just guards, catch seeded defects, and agents act on the proof channel. v0.11 leads with verification as the centerpiece | Depth is not converting: the wedge's value stays runtime-shaped. v0.11 leads with the runtime-contract + effect channels; WS-G3-style depth investment is frozen pending real-scale evidence |
| **PP-G4** | Depth didn't buy prover-appeasement | M-G4, v0.10 arm vs v0.9 arm | No significant iterations-to-green regression on contract-carrying tasks (gates §6.1 bootstrap, PP-L6 pattern); contract-weakening incidence **adjudicated mechanically** (M-G4's Z3-implication procedure; 30 eligible runs ≥ the 20 floor by design): v0.10-arm weakening incidence not exceeding the v0.9 arm's by a margin frozen at A-1.3 **[P]** | The Spec# failure mode is measurably absent at probe scale | **Release blocker regardless of PP-G3**: shipping a deeper prover that teaches agents to weaken contracts is worse than not shipping it |

---

## 6. Decision structure

### 6.1 Per-milestone gates

- **PP-G1/PP-G2 are unconditional** — G1/G2 do not exit without them; nothing downstream merges on top of an unsound core.
- **G4 kickoff is a scope gate**: each WS-G3 item is a separate go/no-go with a priced box; D-G3.3 (frames/`old()`) defaults to **not running** in v0.10 (§2). **Descoping D-G3.1 has a named consequence**: PP-G3's earliness leg is structurally capped at 1/3 (§2 D-G3.1), so the descope decision *must* be accompanied by restating or demoting PP-G3 at A-1.3, before the epoch — a scope decision may not masquerade as measurement evidence at Call G.
- **PP-G3 miss → v0.11 is runtime-channel-shaped** (see the miss column) — the program does not stall; it re-weights.
- **PP-G4 miss → release blocker**, and the transcript corpus becomes the next planning input (which statuses drove weakening: `unknown` vs `unsupported` vs slow feedback).

### 6.2 Program-level call

- **Call G (at G5, the v0.10→v0.11 gate):** adjudicated on PP-G3 + PP-G4 together. Either way v0.11 planning proceeds — Call 2 already committed the program through v0.11 planning — but Call G decides its *shape*: verification-centerpiece (PP-G3 hit) vs runtime-channel-centerpiece (PP-G3 miss). **Call 3 (the v0.11 adoption one-way door) is untouched by this plan** and still requires its own sign-off at v0.11 planning per the v0.9 §6.3 register; everything in v0.10 remains a two-way door.

---

## 7. Risks

1. **WS-G3 is the drowning pool.** Frames/aliasing sank Spec#; the mitigation is structural: per-item go/no-go at G4 kickoff, D-G3.3 defaulting out, and PP-G4 measuring the failure mode directly instead of assuming design steers around it.
2. **Choke-point erosion under vocabulary growth.** Seven statuses + flags + assumption sets multiply the surface where a bypass could hide. Mitigation: `ProofOutcome.Assign` stays the single exit (conformance test extends, never relaxes); D-G2.3's whitelist replaces enumeration.
3. **Probe-epoch continuity.** Re-running the D5.1 pairs on a new build risks silent drift in the fixtures' meaning (e.g. depth work making a "runtime-guard" defect trivially provable *by accident* changes M-G3's baseline semantics). Mitigation: fixtures are frozen at `267aee94` lineage; any fixture change re-registers via A-1.3 before the epoch; pins record both arm builds (the M5 `--calor-dll` machinery is reused as-is).
4. **Measurement cost.** One epoch: 9 pairs × 2 arms × 5 runs = 90 probe-scale runs (ws5-probe-001's 54-run shape realized ≈$8; this is < 2× that shape and far below the M5 comparison scale); authorized with numbers before G5 via `phase-2-spend-authorisation.md`.
5. **Bus factor 1 / self-asserted approvals.** Unchanged from v0.9; A-1.3 freezes and the §8 sign-off are maintainer-approved via PR at bus factor 1, recorded as such.
6. **#807's fix changes `verify` exit behavior on real code.** Code that failed CI on spurious refutations starts passing; code depending on the old behavior (none known) would shift. Recorded as a CHANGELOG-visible behavior change, not a silent fix.

---

## 8. The sequencing deviation, signed off

**What is being approved.** The parent strategy gates Phase 2b (verification depth) *behind* a passed 2a gate — depth only after wedge demand is proven (the Spec# lesson, strategy §2.1). This plan executes Guarantees-terrain before Wedge-terrain, inverting that gating. The v0.9 plan flagged this as "a supersession of parent-strategy sequencing [that] needs sign-off as such at v0.10 planning" (v0.9 §8). This section is that sign-off.

**The argument, updated with Call 2 evidence (stronger than when first made):**

1. The 2a gate remains **parked as unfalsifiable at authorable scale** (strategy §9); "wait for a passed 2a gate" still means "wait indefinitely."
2. New since the flag was raised: **PP-W1 measured the wedge's demand signal directly** — the observed catch delta runs through the contract channel (§1 item 7). That is evidence *for* deepening exactly this machinery, of the kind the 2a gate was supposed to provide and could not at authorable scale.
3. The soundness floor (WS-G1) is not optional under any sequencing: a verifier that refutes correct code and unsoundly elides guards is a liability in the v0.11 Wedge regardless of what v0.10 is named.
4. The Spec#-shaped risk of premature depth is bounded structurally (§7 risk 1): D-G3.3 defaults out, and PP-G4 measures prover-appeasement rather than assuming it away.

**Approval mechanics:** maintainer sign-off happens by merging this plan's PR with this section intact; at bus factor 1 this approval is self-asserted, recorded per the A-1.0/A-1.2 precedent. The supersession is scoped: it reorders 2a/2b terrain for v0.10 only; it does not touch the Call 3 adoption gate.

---

## 9. Deferred item register (placement decisions)

| Item | Decision | Rationale |
|---|---|---|
| **Contract synthesis** (`calor import` contract half; v0.9 §8 assigned it to v0.10 "gated behind provenance tiers") | **Enters only after WS-G2 lands; priced at G4 kickoff; default defer to v0.11** alongside the effect-manifest half | The gate it was assigned (provenance tiers = D-G2.1) is itself v0.10 scope; synthesizing `assumed` contracts before `Assumed` exists in code would recreate the trust hole WS-G2 closes. Deferring keeps both `calor import` halves adjacent to the Wedge onboarding work they serve |
| **`Justified` disposition** | Priced in WS-G2, default-deferred (§2) | No consumer at bus factor 1 |
| **Frames/`old()`** (D-G3.3) | Default not-run; scope-gated at G4 | §7 risk 1 |
| **Real-scale benchmark** (`real-scale-benchmark-design.md`) | Still design-only; PP-G3's miss path names it as the evidence trigger for reopening depth | Unchanged from strategy §9 disposition |
| **#807-constrained fixtures** (D3.3 latency fixture contract forms) | Re-baseline after WS-G1 per the fixture README's recorded trigger — **coordinated with any v0.9.0/v0.10.0 version bump**, whose minor-version regeneration trigger the same README carries, so the fixture re-baselines once, not twice | The constraint was recorded with exactly this re-baseline in mind |

---

## 10. Revision log

**v4 amendment (2026-07-30, Call G) — CALL G = PROCEED; v0.11 planning is
verification-centerpiece-shaped.** Adjudicated per §6.2 on the merged
`guarantees-probe-001` record (#828 → `dfa7371c`; independently recomputed
RECORD-SOUND from raw artifacts): **PP-G3 HIT** — leg (a) treatment M-W1
9/9 ≥ control 9/9; leg (b) all three W5-B defects earned joint-predicate
credit (5/5, 4/5, 5/5 slots; the one unsatisfied slot lacked only the
build-proof event — the agent pre-fixed the defect before any compile).
M-G3 shows the headline conversion: all three contract-class defects moved
`runtime-guard` → `build-proof` under the gate, effect classes unchanged.
**PP-G4 PASS** (release blocker cleared) — leg (a) itg median paired ratio
1.0000, one-sided 95 % lower bound 0.8462, no significant regression
(floor-bound power honesty as frozen: bounds only LARGE regressions);
leg (b) weakening 0/15 control vs 0/15 treatment, 0 indeterminate,
absolute excess 0 ≤ 3. Preconditions discharged: PP-G1/M-G2 green on the
treatment build (`d7c0e6ac` CI success), 90/90 valid runs, 0 tampered,
0 censored. Realized spend $79.39 (authorized gate; ~4× the pre-epoch
<$20 estimate — recorded, and the estimate method corrected for future
gates: sum per-run `total_cost_usd`). Instrument amendments A-1.3.1 and
A-1.3.2 were applied results-blind before the epoch and are part of the
record. **Ceiling honesty, carried into v0.11 planning:** the control arm
already catches 9/9 via runtime guards on these fixtures, so this epoch
demonstrates the gate changes the *channel* (earlier, attributed,
counterexample-bearing surfacing), not the catch *outcome*; whether
earliness converts to outcome differences requires tasks above the current
fixture ceiling — the natural v0.11 measurement question, adjacent to the
deferred real-scale benchmark. Deferred-register items (§9: contract
synthesis, `Justified`, frames/`old()`, real-scale benchmark) roll to
v0.11 planning as registered — none is retired by this call. **Call 3
(the v0.11 adoption one-way door) remains untouched and unexercised.**
D-G5.3 is complete; the v0.10 program's in-repo work is done.

**v3 amendment (2026-07-29, G5 kickoff)** — **A-1.3 froze** (gates doc Annex A):
M-G1–M-G4 definitions and the PP-G3/PP-G4 thresholds are registered,
results-blind, before the Guarantees probe epoch. The D-G3.1 restate-check for
PP-G3 was made and recorded: D-G3.1 shipped (#824, W5-B shapes prove 3/3), so
the ≥ 2/3 leg stands as planned. Two §5 wordings were adjusted at freeze with
disclosure in the A.2 rows: the PP-G4 iterations leg adjudicates over all 9
pairs (3-cluster bootstrap is degenerate — the PP-W1 precedent; the
contract-carrying trio's medians are additionally reported), and the weakening
margin froze at ≤ 3/15 excess runs (small-sample honesty). G1–G4 engineering
complete at freeze time (#818/#820, #819/#821/#822, #823, #824); the
registration itself was adversarially reviewed pre-merge (#825, twelve
findings amended in-document, results-blind — the A.3 entry records the
round). Spend authorization remains a separate gate.

**v2 amendment (2026-07-29, G2 implementation)** — D-G2.4 called the envelope
bump "schema v1.2 … additive minor bump per the schema's own rules." The
schema's own rules say otherwise: the status vocabulary is **closed**, and
growing it (five → seven) is a **major** bump. The implementation follows the
schema's rule — envelope **2.0** — with a CHANGELOG migration note; everything
else in the payload change is additive. Recorded here because the plan's label
was wrong, not the plan's intent ("per the schema's own rules" decides).

**Draft v2 (2026-07-29)** — adversarial review round 1 (independent agent; verdict on v1: 60 %). Dispositions:

- **C1 (accepted)**: v1's PP-G1 threshold ("`proven-contracts.calr` 10/10") was mathematically unsatisfiable — the reviewer verified that 4 of the 7 refuted contracts are *genuinely violable* under the committed bit-vector semantics (`Square(46341)`, `AbsoluteValue(int.MinValue)`, `AddPositive(int.MaxValue,1)` all wrap), i.e. the flagship sample is itself unsound and only `Identity`/`Max`×2 are the spurious class. §1 item 2 taxonomy corrected; D-G1.4 gains the sample repair (overflow-excluding `§Q` bounds, CHANGELOG-visible) with the unbounded shapes pinned as honest-refutation fixtures; PP-G1 restated over the repaired, pre-frozen corpus. (Also disposes m5.)
- **C2 (accepted)**: v1's §1 item 5 claimed the FactCollector flow-insensitivity defect was live — it was fully fixed in #686 (`4b0dd55a`: `ScopedFact` dominating-guard scoping + assignment kill), and the obligation-side consistency pre-check already covers the asserted fact set. The exact stale-claim-in-an-audit failure the audit rule exists for, carried from strategy §1 without re-verification. Item rewritten (two channels guarded, contract-level live); D-G1.3 rescoped to the contract-level guard + regression pin; FactCollector deliverable deleted.
- **C3 (accepted)**: v1's D-G5.2 ("same pins") left the verify-channel configuration unresolved — running verify in the v0.9 arm rigs the comparison (that build refutes *every* result-referencing `§S` per #807, unappeasably), while running it only in the v0.10 arm falsifies "same pins." Redesigned: per-arm **native registered configurations** (v0.9 per A-1.2 verify-excluded, v0.10 per A-1.3 verify-on), declared as config variance in pins; M-W1 totals are the cross-arm quantity (ceiling noted: no regression from 9/9); the earliness leg is a **single-arm product-configuration claim** (the v0.9 arm-C mold). The v0.9-review-C2 attribution commitment added (isolation build if main accretes non-plan work before G5).
- **M1 (accepted)**: PP-G3's ≥2/3 threshold silently depended on D-G3.1 (two W5-B fixtures have `§B`-chain bodies outside D-G1.1's encodable set). Dependency stated in D-G3.1 and the PP-G3 row; §6.1 G4 gate requires restating/demoting PP-G3 at A-1.3 if D-G3.1 descopes — a scope decision may not masquerade as measurement evidence.
- **M2 (accepted)**: PP-G3's surfacing channel is deterministic-by-construction once M-G1/M-G2 are green (the seeded bodies encode to genuinely-SAT obligations) — the epoch adjudicates the **agent-behavior residual**, stated per the A-1.2 feasibility-by-determinism precedent and its limit, applied symmetrically (D-G5.1); the PP-G3 hit criterion now names agent action on the refutation, not mere surfacing.
- **M3 (accepted)**: v1's contract-weakening adjudication ("transcript/diff inspection") was eyeballing at bus factor 1, and its ≥20-run floor was unreachable at the 3-runs/arm probe shape (18 total). Weakening is now decided **mechanically** (frozen-vs-final contract-set diff + Z3 implication asymmetry — the product dogfooding its measurement); the epoch runs 5 runs/arm so eligible runs = 30 ≥ 20 by design; PP-G4's blocker leg rides on an adjudicated quantity.
- **M4 (accepted)**: M-G3 operationalized — surfacing event and per-defect aggregation (majority-over-runs, A-1.2 pattern) frozen at A-1.3; the build channel split into `build-proof` vs `build-block` (the v0.9 arm already has 5 build-*block* catches; conflation would overread the headline); PP-G3 adjudication conditioned on PP-G1/M-G2 green on the treatment build.
- **M5 (accepted)**: `Assumed` had zero real producers until G4, making the G2 exit criterion synthetically satisfiable. Exceptional-path assumptions (2b item 6) pulled from WS-G3 into WS-G2 as **D-G2.5**, `Assumed`'s first producer; G2 exit requires a real producer.
- **m1 (accepted)**: the three legacy status-mapping sites (`ToContractStatus`/`ToObligationStatus`/`ToImplicationStatus`) named in D-G2.2 scope with the frozen rule "`Assumed` never maps to a legacy Proven-equivalent."
- **m2 (accepted)**: the second reduced translator (`BoundExpressionTranslator`, bug-pattern checkers — unsigned forms mis-modeled behind agent-visible verdicts) was absent from v1; folded into D-G2.3's whitelist rearchitecture.
- **m3 (accepted)**: WS-G4 exit criterion scoped to single-project multi-module; cross-project references recorded as unpriced/out; enforcement-side risk confined to the front-end spelling half.
- **m4 (accepted)**: latency-fixture re-baseline (§9) coordinated with release version bumps so the fixture re-baselines once.

**Draft v1 (2026-07-29)** — initial draft, written after Call 2 resolved PROCEED (m5-compare-001 merged `ce7708af`). Inputs: v0.9 plan §8 milestone map and its M9 deviation flag; strategy §2.1/§5.1/§9 and the Phase 2b item list; `ws5-probe-001` + `m5-compare-001` verdicts; open issues #807/#755/#809; `docs/verification-modeled-forms.md`; audit anchors grep-verified at `ce7708af` (§1). §5 thresholds provisional until A-1.3.
