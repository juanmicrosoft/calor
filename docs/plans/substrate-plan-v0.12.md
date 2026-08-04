# The Substrate — v0.12 Execution Plan

**Status:** Draft v1 — not yet adversarially reviewed. The registration details (§2 WS-S5, §5) and the eligible-supply threshold (§4 M-S3) are the standing review targets.
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-08-04
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) and [`agent-native-gates.md`](agent-native-gates.md) (Annex A at A-1.4 tranche 2). Predecessor: [`wedge-plan-v0.11.md`](wedge-plan-v0.11.md), whose **Call W** resolved **PROCEED (substrate)** on 2026-08-04 — PP-W2 **not adjudicated**, PP-A2 = **"demand unproven"**. The two records this plan is built on: [`wedge-real-scale-closeout.md`](wedge-real-scale-closeout.md) (the finding and the three levers) and [`call-w-adjudication.md`](call-w-adjudication.md) (the shape decision).
**Release carry-forward:** there is **no v0.11.0 tag** — the maintainer folded v0.11 forward (Call W record). v0.12.0 therefore ships the v0.11 range as well, and **PP-W5** (strictness parity, gate frozen at A-1.4 tranche 1 / #839) and **PP-A1** (CI adoption gates) carry forward as v0.12.0 release gates.
**Target release:** v0.12.0.

---

## 0. Objective

> **Make the real-scale measurement possible: supply the benchmark with thesis-testing tasks it can actually adjudicate, by moving the three substrate levers that starved it — and prove the supply mechanically, before spending on another epoch.**

v0.11 answered its question and the answer was structural. The real-scale benchmark is *built* and *proven* — oracle-hidden bundle runner, three task strata, a working `Calor0410` addressability differential — and it produced **zero** eligible thesis-testing tasks on a pinned three-project corpus. Not a null result: a supply failure, with three named causes. v0.12's job is to remove them.

The discipline that makes this plan cheap is that **its headline proof point needs no live agent spend**. Eligible-task supply is a mechanical property of the task generator over the corpus: it is computed by running the harness, not by running agents. v0.12 can therefore be adjudicated on a laptop, and the next epoch is only authorized once the substrate demonstrably supplies it. That inverts v0.11's cost structure, where the supply failure was discovered *after* the dry-run spend.

The one thing v0.11 did establish about the venue is worth restating, because it is what makes this worth doing: **the v0.10 authorable-fixture ceiling does not survive real code.** The C# arm shipped genuine, held-out-failing bugs at real scale where it shipped none on authored fixtures. There is headroom to measure. What is missing is our ability to put a verification-addressable defect in front of it.

Non-goals: running the real-scale epoch (that is the *next* spend gate, and it is explicitly out of this plan's box — see §6.2); new adoption depth (PP-A2 routed the adoption half to maintenance-mode); Phase 3 items; frames/`old()`; async/concurrency verification; the Call 3 crossing (reserved, §6.3); 1.0.

---

## 1. Audit delta: the substrate at Call W

**Audit rule (carried from v0.9 §1):** every file:line anchor is grep-verified on the audit date against a recorded commit. This audit: **2026-08-04, commit `85347e49`** (main at Call W; version still `0.10.0` in `Directory.Build.props:3`, per the no-v0.11.0-tag decision).

1. **Converter fidelity is measured, and it is the binding constraint.** The first real fidelity measurement (Slice B, `5de804cc`) on the SHA-pinned vendored corpus: **`NativeFraction` MediatR 0.469, Serilog 0.400, FluentValidation 0.532** — all below the provisional 0.70 steer (`tools/Calor.RoundTrip.Harness/TaskGen/FidelityGate.cs:11`, bar configurable and explicitly provisional; `Program.cs:387`). Roughly half of every real project is excluded from native conversion, and the close-out's structural point is that the excluded half is **not a random half**: stateful, guard-bearing code — exactly where injectable defects live — converts worst.
2. **The loss ledger already tells us where fidelity goes.** `ConversionLossKind` (`src/Calor.Compiler/Migration/ConversionContext.cs:125–147`) partitions every loss into `InteropPreserved`, `FallbackTodo`, `Dropped`, `PreprocessorStripped`, `EmitterFallback`, each with a `file:line` (`ConversionLoss`, `:154–161`), and the harness aggregates per project (`RoundTripReport.cs:59–60`, `:312`). **The instrument for ranking the fidelity work exists and has never been read as a ranked work-list.** That is D-S1.1, and it is nearly free.
3. **The honesty work moved the denominator, deliberately.** `FeatureSupport` now registers **137 entries: 105 Full / 10 Partial / 22 NotSupported** (`grep -c "Support = SupportLevel.<L>,"` on `src/Calor.Compiler/Migration/FeatureSupport.cs`) against v0.10's 124: 105 / 8 / 11. Note what did **not** move: `Full` is unchanged at 105. All 13 net-new entries are `Partial` or `NotSupported`, because W1 Slice A (`a91c679c`, `4d7f5887`) converted *silent* substitutions into *declared* losses. This is the single most important thing to hold onto in v0.12: **some of the current low native fraction is honesty, not incapability**, and the fix for those entries is real emitter work, never re-labelling. §5's PP-S4 exists to keep that true.
4. **Checker breadth is narrower than the close-out's one-line summary implies.** `NullDereferenceChecker` keys *entirely* on `Option`/`Result` unwrap spellings — `target.EndsWith(".unwrap")`, `".unwrap_unchecked"`, and a `target.Contains("unwrap")` catch-all (`Analysis/BugPatterns/Patterns/NullDereferenceChecker.cs:264–268`); **plain reference-null dereference is not modeled at all**. `IndexOutOfBoundsChecker` keys on `.get` / `.at` / `[]` / `array_get` / `list_get` target spellings (`IndexOutOfBoundsChecker.cs:203–208`). Converted C# produces neither vocabulary natively. The checker set is `DivisionByZero`, `IndexOutOfBounds`, `NullDereference`, `OffByOne`, `Overflow`, `PreconditionSuggester` (`Analysis/BugPatterns/Patterns/`) — five checkers, all shape-keyed.
5. **The expressible-defect machinery is banked and reusable.** `ExpressibleMutationOperators` + `VerificationAddressability` + `ExclusionAccounting` (PR #856, `tools/Calor.RoundTrip.Harness/TaskGen/`), 132 tests, **0 `src/` changes**. The differential gate mechanically confirms a Calor check fires on the mutated conversion and not on the clean one. Its measured yield on the current corpus: **3 native candidates, 3/3 addressable, 0/3 eligible** — every exclusion under clause (b) (no value-asserting covering test; arm-divergent failure signature). The mechanism is not the problem.
6. **Corpus shape is a one-line diagnosis.** MediatR (mediator/dispatch), Serilog (logging), FluentValidation (validation) are immutable-leaning and assertion-poor in exactly the native surface that survives conversion. The close-out's `NoObservableDefect` exclusion is the corpus telling us this directly: the defect was injected and the check fired, and *no test looked at the value*.
7. **Two release gates are unadjudicated and now belong to v0.12.** PP-W5 (strictness parity) is frozen at A-1.4 tranche 1 (`08446f6d` / #839) and never run; its treatment arm is now "v0.11 + v0.12 substrate", which the epoch must state rather than inherit. PP-A1 (CI adoption gates) likewise carries forward. Neither is a Call-S input; both block the v0.12.0 release.

---

## 2. Workstreams

Ordering rationale: WS-S1 (fidelity) leads because every other lever is capped by it — checker breadth over code that does not convert natively buys nothing, and a better corpus converted at 45% is still half-excluded. WS-S2 (checkers) and WS-S3 (corpus) proceed in parallel behind it, because both are independently valuable and neither blocks the other. WS-S4 (overlay) is the parallel proof-depth track and is deliberately *not* on the critical path. WS-S5 measures — mechanically, without an epoch — and hosts the call.

### WS-S1 — Converter fidelity (size: L; the lead)

- **D-S1.1 Read the ledger.** Aggregate `LossKindCounts` across the three vendored projects into a **ranked work-list**: loss kind × feature × occurrence count × estimated native-fraction recovery. This is a report, not an inference, and it is the input that prices every item below. Nothing in WS-S1 is scoped before this lands.
- **D-S1.2 Close the top-ranked emitter gaps.** Execute the work-list in recovery order until the M-S1 bar is met. Scope is deliberately data-set at D-S1.1 rather than guessed here; the plan commits to the *ranking discipline*, not to a pre-chosen feature list.
- **D-S1.3 `EmitterFallback` elimination.** The `EmitterFallback` kind exists because the Calor emitter's internal fallback paths do not thread through the loss ledger (`ConversionContext.cs:139–146`, #836 M2) — it is reconciled post-emission so raw C# can never coexist with a "fully native" claim. Each occurrence is either a real feature gap (→ D-S1.2) or a ledger-threading gap (→ fix the threading). Both are in scope; conflating them is not.
- **D-S1.4 Fidelity non-regression in CI.** Per-project `NativeFraction` is asserted against a committed floor, so fidelity cannot silently regress while other work lands. Floors ratchet up, never down, and a ratchet commit must cite the deliverable that earned it.
- **D-S1.5 Honesty invariant (the anti-gaming rule).** A CI check that fidelity gains come from conversion, not classification: no `FeatureSupport` entry may move toward `Full`, and no loss may leave the ledger, without a semantic test demonstrating faithful conversion. This is PP-S4's enforcement arm and it is a **blocker**.

Exit criteria: M-S1 met on ≥2 of 3 projects; ranked work-list committed with per-item disposition (done / deferred / not-worth-it, each priced); D-S1.4 floors and D-S1.5 check green in CI.

### WS-S2 — Checker breadth (size: M)

- **D-S2.1 Plain reference-null dereference.** Model the null-deref pattern converted C# actually produces, not only `Option.unwrap` spellings (§1 item 4). This is the single highest-yield checker gap: reference null is the most common real defect class in the corpus's own bug history.
- **D-S2.2 Index-OOB over converted accessor forms.** Extend beyond the `.get`/`.at`/`[]` target-spelling keys to the indexer and collection shapes the converter emits.
- **D-S2.3 Checker-honesty guard.** Every breadth extension ships with both a positive and a **negative** corpus: the check must fire on the defect and must *not* fire on the clean conversion. Broadening a checker into a false-positive generator would inflate eligible-task counts while destroying the differential — the failure mode this guard exists to prevent.
- **D-S2.4 Addressability re-measurement.** Re-run the #856 differential over the corpus after each breadth extension and record the candidate/addressable/eligible triple. This is a mechanical measurement and belongs in CI once it is fast enough.

Exit criteria: M-S2 met; every extension carries its negative corpus; the re-measured triple is committed per extension.

### WS-S3 — Corpus shape (size: S–M)

- **D-S3.1 Add a value-asserted subject.** Vendor (SHA-pinned, permissively licensed, same submodule discipline as `5de804cc`) at least one numeric/collection/stateful library whose native surface is exercised by **value-asserting** tests. Selection criteria are registered *before* the candidates are measured: permissive license, buildable on the pinned SDK without source retargeting, and a test suite that asserts on returned values rather than on interaction/shape.
- **D-S3.2 Selection honesty.** Every candidate evaluated is recorded with its measurements and its accept/reject reason — including candidates rejected *after* measurement. Choosing the corpus by looking at which one yields the most eligible tasks and reporting only the winner is corpus-shopping; the register is what makes the choice auditable.
- **D-S3.3 Per-project supply accounting.** Eligible-task supply is reported per project, never pooled into a single headline, so a single fortunate subject cannot carry the gate.

Exit criteria: ≥1 new subject vendored and building; selection register committed; per-project supply reported.

### WS-S4 — Authored-contract overlay (size: M; parallel, off the critical path)

The D-W4.5 arm-ii channel that does not exist: a deterministic mechanism to re-apply `§Q`/`§S` to each per-task conversion, so proof-depth can be tested on defect classes the mechanical checkers can never reach.

- **D-S4.1 Overlay mechanism.** Deterministic, re-appliable, and **blind by construction** — the overlay must be derivable without reference to the injected defect, or the arm proves nothing. This constraint, not the mechanism, is the hard part.
- **D-S4.2 Feasibility record.** A written finding on whether a blind overlay is constructible at all, published either way. A negative here is a real result and retires arm-ii honestly rather than leaving it perpetually "planned".

Exit criteria: mechanism shipped **or** D-S4.2 negative finding published. Explicitly **not** a Call-S input (§6.2) — WS-S4 cannot block the call.

### WS-S5 — Measurement and the call (size: S; no live spend)

- **D-S5.1 Supply measurement.** Run the task generator across the full corpus and report the candidate → addressable → eligible funnel per project, per stratum, with full exclusion accounting. Mechanical; no agents.
- **D-S5.2 Annex A-1.5 registration.** Register M-S1/M-S2/M-S3 and PP-S1–PP-S4 **before** D-S5.1 runs, per the freeze rule ("registration happens before freezing — never after"). The M-S3 threshold is provisional in this draft (§4) and is frozen at registration from D-S1.1/D-S2.4 data.
- **D-S5.3 Call S.** Adjudicate (§6.2).
- **D-S5.4 Release gates.** Run the carried-forward PP-W5 parity epoch (its own spend authorization; treatment arm stated explicitly as "v0.11 + v0.12 substrate") and clear PP-A1, then ship v0.12.0 covering the v0.11 range.

### WS-S0 — Adoption: maintenance-mode posture (size: S; standing)

PP-A2's pre-committed routing, not a discretionary choice. Keep the shipped v0.11 adoption surface (`calor import`, review packet, tested eject, playbook, `Calor.Sdk`) **working, correct, and documented** — bug fixes, honest docs, green gates. Fund **no new adoption depth** ahead of demand. Crossing Call 3 is what takes the adoption half off maintenance-mode; until then this workstream is deliberately small, and any growth in it is a recorded deviation.

---

## 3. Sequencing

1. **S1 (kickoff):** D-S1.1 ledger read → work-list committed → WS-S1 scope confirmed and box corrected (v0.9 discipline). D-S5.2 registration drafted in parallel.
2. **S2:** WS-S1 execution (D-S1.2/1.3), with D-S1.4/1.5 landing early so the ratchet and the honesty invariant guard the rest of the work.
3. **S3 (parallel):** WS-S2 and WS-S3. Neither blocks the other; both consume WS-S1's improving fidelity.
4. **S4 (parallel, off critical path):** WS-S4.
5. **S5:** A-1.5 registration frozen → D-S5.1 supply measurement → **Call S** → release gates (D-S5.4) → v0.12.0.

The registration (D-S5.2) must be frozen **before** D-S5.1 runs. This is the one ordering constraint that is not negotiable for scheduling convenience.

---

## 4. Success metrics (definitions)

| Metric | Definition | Bar |
|---|---|---|
| **M-S1** Native fraction | `ConversionCoverage.NativeFraction` per project on the vendored corpus (`RoundTripReport.cs:311`) — `ConvertedNative = Replaced ∧ LossCount == 0 ∧ not reverted` | **Provisional ≥ 0.70** on ≥2 of 3 original projects (the existing provisional steer, `FidelityGate.cs:11`); frozen at A-1.5 |
| **M-S2** Addressability yield | Native candidates → verification-addressable, via the #856 differential | Reported per checker extension; **no bar** — an input to M-S3, not a gate in itself |
| **M-S3** Eligible-task supply | Tasks passing the full D-W4.1 eligibility predicate, per project, per stratum | **Provisional ≥ 5 eligible expressible-stratum tasks across ≥ 2 projects**; frozen at A-1.5 from S1/S3 data. See the caution below |
| **M-S4** Honesty invariant | `FeatureSupport`/ledger movements without a semantic test (D-S1.5) | **0**, enforced in CI |

**Caution on M-S1's provisional value.** The gap is large and should be stated plainly: 0.400 / 0.469 / 0.532 → 0.70 is roughly a 35–75% relative increase in native fraction per project, and the 0.70 steer was never calibrated against a ledger read — it predates D-S1.1. It is carried here unchanged only so the draft does not invent a new number before the data exists. D-S1.1 may well reprice it, in either direction, and repricing it *from the ledger* at A-1.5 is legitimate; repricing it after seeing whether we hit it is not.

**Caution on M-S3's provisional value.** Five is a supply floor sufficient to *run* an epoch of the dry-run's shape (6 tasks), **not** a power calculation for adjudicating PP-W2. The number that matters for the epoch is whatever powers the registered effect, and it is computed at A-1.5 from the S1/S3 measurements — not asserted here. Freezing M-S3 at "enough to run something" would repeat v0.11's error in a new place.

---

## 5. Proof points (go/no-go claims — review these)

| ID | Claim | Test | Outcome | If it fails |
|---|---|---|---|---|
| **PP-S1** | Converter fidelity is movable | M-S1 on ≥2 of 3 original projects | Hit / miss | The 40–53% is structural, not a work-list. The wedge's real-scale venue is retired as unreachable at this converter architecture, and v0.13 re-scopes to what a partial-conversion product can honestly claim |
| **PP-S2** | Real defects are expressible | M-S2 rises with D-S2.1/2.2, on real corpus code | Hit / miss | Checker breadth is not the lever; the overlay (WS-S4) becomes the only proof-depth channel and its feasibility finding becomes decisive |
| **PP-S3** | **The benchmark can be supplied** (the headline) | M-S3 | Hit / miss / **not adjudicated** | Miss → the real-scale venue is unreachable at v0.12 maturity and the program stops paying for it; the honest claim re-scopes to earliness/attribution/cost, which *is* evidenced |
| **PP-S4** | No converter-appeasement (**blocker**) | M-S4 = 0, D-S1.5 green | Pass / fail | Fail blocks the release. Fidelity bought by reclassification is worse than no fidelity: it re-introduces exactly the silent-substitution dishonesty W1 Slice A removed |

PP-S3 is deliberately four-valued in the same shape as PP-W2, and **task-supply starvation is now an enumerated not-adjudicated route** — the close-out registered it as a novel route the wedge plan had not anticipated (A-1.4 tranche 2). It will not be novel twice.

Carried forward as **release gates, not Call-S inputs**: **PP-W5** (strictness parity, frozen #839) and **PP-A1** (CI adoption gates). **PP-A2** is resolved ("demand unproven") and is not re-opened by this plan; only the Call 3 crossing reopens it.

---

## 6. Decision structure

### 6.1 Per-workstream gates

Each workstream's exit criteria are stated in §2 and are checked at its own close, not deferred to Call S. A workstream that misses its exit criteria is re-boxed or descoped **with the deviation recorded** (§10), never silently carried.

### 6.2 Call S — the v0.12 → v0.13 gate

Adjudicated on **PP-S3** (the headline), with PP-S1/PP-S2 as diagnostic inputs explaining *which* lever moved and PP-S4 as a blocker. **WS-S4 is not an input** — the overlay is a parallel track and its outcome cannot decide this call.

- **PP-S3 hit** → the real-scale epoch is re-authorized as a **separate spend gate** (it is not in this plan's box), with the supply measurement as its feasibility evidence and the epoch's power computed from the realized supply.
- **PP-S3 miss** → the program stops paying for the real-scale venue. The differentiated claim re-scopes to earliness/attribution/cost — which the v0.10 probe *did* evidence — and v0.13 leads with product, not measurement.
- **PP-S3 not adjudicated** → published as such, with the route named, per the §6.1 principle the close-out exercised.

In every branch the program continues; Call S decides *shape*. This is the Call 2 / Call G / Call W pattern.

### 6.3 Call 3 — reserved

Unchanged and untouched by this plan: the named-external-adopter crossing is a one-way door reserved for the maintainer. PP-A2's "demand unproven" does not close it; it parks it.

---

## 7. Risks

1. **Fidelity work is unbounded.** The ledger may rank a long tail with no head. *Mitigation:* D-S1.1 precedes all scoping, and PP-S1's miss branch is a real, pre-committed retirement — not an invitation to keep digging.
2. **Checker broadening inflates supply with false positives.** The failure mode that would make M-S3 look green while destroying the differential. *Mitigation:* D-S2.3's mandatory negative corpus; the differential itself requires the check to be *absent* on the clean conversion.
3. **Corpus-shopping.** Picking the subject that yields the best numbers. *Mitigation:* D-S3.1's pre-registered selection criteria + D-S3.2's full evaluation register incl. post-measurement rejections + D-S3.3's per-project reporting.
4. **Fidelity bought by relabelling.** *Mitigation:* PP-S4 as a blocker with a CI arm (D-S1.5). This risk is rated highest-severity because it is the one that would corrupt the evidence base rather than merely waste effort.
5. **The overlay quietly becomes the critical path.** It is the intellectually interesting track and the easiest to over-invest in. *Mitigation:* explicitly excluded from Call S; D-S4.2 permits a published negative.
6. **v0.12.0 carries two releases' worth of change.** No v0.11.0 tag means a larger, less-bisectable release. *Mitigation:* release notes and CHANGELOG must cover the full v0.11+v0.12 range (Call W record), and PP-W5's treatment arm must be stated as "v0.11 + v0.12 substrate".

---

## 8. Relationship to the parent strategy

v0.12 is **not** on the strategy's Phase-2a/2b progression — it is prerequisite engineering underneath it, entered by Call W's not-adjudicated branch. The strategy's headline claim (verification converts earliness into outcomes) is unchanged and unproven; what v0.12 buys is the *ability to test it*. The adoption half stays at maintenance-mode posture (PP-A2), so the strategy's 2a items shed by v0.11 — persisted semantic index, `calor query` — remain deferred, and their deferral is now two versions old and should be re-examined at the v0.13 planning gate rather than rolled forward silently a third time.

---

## 9. Deferred item register

| Item | Disposition |
|---|---|
| Persisted semantic index + `calor query` (strategy 2a never-shed core) | Deferred again (maintenance-mode posture). **Flagged: third consecutive deferral — re-examine explicitly at v0.13 planning, do not roll forward silently** |
| Real-scale epoch re-run | Out of box by design; separate spend gate after Call S |
| Logic-mutation stratum as a thesis channel | Retired as a mechanical-arm channel (no verification signal); retained only as a conversion-penalty measurement |
| Frames / `old()` | Still default-not-run (v0.10 register) |
| #807-adjacent verifier limits, `Justified` tier | Unchanged from v0.10/v0.11 registers |

---

## 10. Revision log

- **Draft v1 (2026-08-04):** initial plan. Written directly against the Call W record and the close-out finding; incorporates the maintainer's two Call-W-adjacent decisions of the same date — **fold v0.11 forward (no v0.11.0 tag)** and **converter fidelity leads**. Not yet adversarially reviewed; §4 M-S3 and §5 are the standing review targets.
