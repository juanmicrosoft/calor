# Roadmap — v0.17 "Reach, and the Rows We Shipped"

**Date:** 2026-09-01
**Status:** **Draft v4** — three adversarial rounds (§10), matching the v0.16 roadmap's bar.
Round 1: self-conducted, twelve findings. **Round 2: independent (cloud multi-agent)** — found that
three of round 1's twelve "Applied" dispositions were only half-applied, the finding a self-review
structurally cannot make. **Round 3:** verified **27/27** dispositions landed, **independently
recomputed §0.3's four trigger numbers** (the gap rounds 1 and 2 both left open — all reconcile,
including three sum checks), and found six more, two of them Major: R4 mis-scoped a MUST against
both issues' own text, and round 1's own effect-size fix had handed the release the power to set
its target after seeing the data. All applied. **§10 names what is still unattacked.**
**Written against:** `cb68afcf` (main after PR #1140), with PP-W-rows adjudicated in PR #1145.
`Directory.Build.props:3` reads `0.15.0`; **v0.16.0 has not been released**, and every number
below is measured at the 0.16 branch cut, not after it.
**Governing inputs:** `roadmap-v0.16.md` (*R:*), **`roadmap-v0.13-v0.15.md` (*R15:*)** — v0.16's
own *R:*, which is why several ranges copied from it resolve there and not against *R:* —
`2026-09-01-ppw-rows-dry-run.md` (*W:*), `docs/design/effect-rows-in-the-type-system.md` (*D:*),
`docs/plans/agent-native-gates.md` (*A:*, read-only), **`2026-08-27-v0.15-e4-notes.md` (*e4:*) and
`-e5-notes.md` (*e5:*), `2026-08-27-v0.16-s1-s2-measurement-notes.md` (*N:*)**, issue #1136's
measured table, the four corpus ledgers under `bench/phase0-agent-native/`, and the open issue list
on 2026-09-01.

---

## 0. Where 0.16 left us — measured at the branch cut

### 0.1 Shipped

E7 (`calor_query` over the project index, gate 3's MCP leg and gate 7's E7 leg), K1 (the P32 ledger
on the shipping bind rule), W1 (per-turn capture), W3(a)(b)(c) (converter reach for #903 and #1097,
the #1104 recursion bound), W4 (turn attribution), W5 (Calor0406 at both caps, `Calor0600` retired
at `ProcessScc`), `CalorPermissiveEffects` through `Calor.Tasks`, and the kickoff sweep (ES-08
registered, #982/#981/#976/#949 dispositioned).

**Not built, carried again:** gate 5 leg (b)'s `compile-all-committed-calr` job (R15:996) and
gate 3's CLI-process and `Calor.Sdk` legs (issue #1116). **0.16's SHOULD tier did not ship:** W6
(the Calor0422/0423 and Calor0208 losses), W7 (`FunctionBoundType.Row` end-to-end), W8 (index
parity with `calor build`).

### 0.2 PP-W-rows — UNDERPOWERED, and the confound that sat under it

The proof point is adjudicated on A-1.12's own registered off-ramp: the A:81 dry run sized the
experiment at **N = 9 runs per cell** for 0.80 power at Δ = 0.5 — ~$287 against a frozen $150
ceiling, where the largest affordable N is **3** at power **0.48** (W:§4). `w-rows-001` was never
run. **The claim is neither supported nor refuted**; `legA` and `legB` are null in
`effect-rows-benefit-ledger.json`.

Published beside it, as facts about six fixtures and not about rows: **zero escapes on both arms
across 28 valid runs**, the registered shape realized in 13 of them, and a leg-B point of
**1.1175** — below the registered 1.20 margin (W:§2, §3).

**The confound that matters for this release.** #1136's measured table — registered at A-1.12 as a
pre-registered confound on leg A's direction — says the treatment arm was **not** the fail-closed
compiler the design assumed. Of twelve argument shapes measured on v0.15.0 with no flags, **five
launder silently** (`warning Calor0425`, exit 0): an own `§FLD` via `this.`, a **`§PROP` by simple
name with no receiver at all**, an own `§FLD` through a local alias of `this`, a `§FLD` on another
instance of the same class, and an instance method group with a parameter receiver. Seven are
charged. The held-out suites confirm it at runtime: the emitted C# builds and exactly the
effect-observing tests fail (W-001 2 of 7, W-006 2 of 10).

Two readings, both true: the zero-escape result is not *explained* by the confound (arm A produced
zero too, and neither arm's agents reached for these spellings), **and** an arm that leaks five of
twelve shapes cannot be the fail-closed half of a fail-closed-vs-permissive contrast. Whatever a
future probe measures, it must measure it against a compiler that actually refuses.

### 0.3 Every deferred trigger reads zero — and the competing reading

A-1.12 froze the residual list with a number attached to each item. All four are now measurable,
and none fires (measured 2026-09-01 against the committed ledgers):

| Deferred item | Registered trigger | Measured | Fires |
|---|---|---|---|
| IL-derived rows for BCL-returned delegates | `UnknownSource + InvocationUndetermined` > 10 over K1's enforced set | **0** (0/0/0 per subject) | no |
| Calor0419 at BCL argument sites | D-A `calor0419FunctionTyped` > 10 | **2** | no |
| Key parameter component = inferred argument types; IL keys `FromStrings` | gate 6 must move, **or** IL rows trigger | gate 6 **unchanged** at 65.46 %; IL rows did not fire | no |
| Q4 — the Calor0425 span | K1's never-invoked fraction | `RowlessNeverInvoked` = **0** | no |

*Sources:* `calor0425-corpus-ledger.json` (schema 3, `bindRule: "propagated"`),
`higher-order-demand-ledger.json`, `metadata-binding-corpus-ledger.json`.

**The instrument has a blind spot, and the first trigger sits inside it.** The Calor0425 ledger's
own documentation says so: *"§13.4's 'unresolved receiver' is NOT a bucket, and honestly so: an
invoked value whose type came from an unresolved receiver never reaches Calor0425 — the bare-target
guard sends it through the unknown-call chain as **Calor0411**"*
(`Calor0425CorpusLedgerTests.cs:57-60`). **No committed ledger counts Calor0411 over the corpus.**
So "`UnknownSource + InvocationUndetermined` = 0" is a statement about the sites that reach
Calor0425, and an adjacent class of unresolved-callee demand is unmeasured **by construction**. The
two named fields do still mean what the trigger's author meant — the ledger defines
`InvocationUndetermined` as *"§13.4's 'BCL-returned delegate'"* verbatim — so the trigger is
correctly *specified*; what is missing is a denominator beside it. **R1 adds the Calor0411 corpus
count**, so the next reading of this trigger is against a measured whole rather than a measured
part.

**The reading this document refuses to take on its own.** "Zero demand" and "the denominator is too
small to contain the demand" are not distinguishable from these numbers. Every trigger is counted
over code the compiler successfully binds and enforces; §0.4 is the measure of how much real code
that is. §1 is built on the second reading, and §4 registers the instrument that would tell them
apart.

### 0.4 Reach — the denominator, exact

Measured over the three A-1.5.3 conversion subjects at their pinned submodule commits.

| Instrument | Value | Source |
|---|---|---|
| Modules parsed | **364 of 364** — `ExcludedParseFailed` **0** on every subject | `calor0425-corpus-ledger.json` (the authority: `ExcludedParseFailed` per subject). The Calor0270 ledger's `AggregateModulesBound` = 364 agrees, but its field is named *Bound* while its `BindRule` is `"parsed"`; it is corroboration, not the source |
| Modules reaching the effect pass (`ModulesEnforced`, propagated rule) | **304** — MediatR 31, serilog 88, FluentValidation 185 | `calor0425-corpus-ledger.json` |
| Modules stopping at binding (`ExcludedBindFailed`) | **60** — MediatR 5, serilog 24, FluentValidation 31 | same |
| Calor0425 emitted | 90 diagnostics over 40 of 304 modules | same |
| Calor0425 dominant cause | `ExternalBase` **53 of 90 = 59 %** (serilog 13, FV 40); `RowlessDestination` 11, all `RowlessInvoked`; `InvocationRowless` 26 | same |
| Calor0270 (SignatureUnresolved), raw-bag rule | 315 over 39 of 364 | `calor0270-corpus-ledger.json` |
| Metadata resolution (gate 6) | **817 / 1248 = 65.46 %** — MediatR **129/226 = 57.08 %**, serilog 104/113 = 92.04 %, FV 584/909 = 64.25 % | `metadata-binding-corpus-ledger.json` |
| Higher-order demand | D-A **2**; D-B **3121** over 364 files (2676 lambdas, 311 delegate-typed declarations, 132 delegate invocations, **2 delegate declarations**) | `higher-order-demand-ledger.json` |
| Resolver keys | 265 bound / 814 string | `effect-resolver-key-ledger.json` |

**Two numbers carry this release.** W3(a) took parse failures to **zero** — gate 9's floor was
`ExcludedParseFailed ≤ 2` and the measurement is 0, and `ModulesEnforced` landed at 304 against a
floor of 250, matching R:§5 gate 9's published expectation of "≈ 256 + 48" exactly. What did *not*
move is everything after parsing: **60 modules still stop at binding**, and metadata resolution is
**unchanged at 65.46 %** — the same 817/1248 gate 6 has read since 0.14. The reach frontier moved
from the parser to the binder, and nobody has measured what is behind it: the 60 modules' failure
causes are **not broken out by code in any committed ledger**. §3.1 R1 is that measurement.

**`ExternalBase` is the largest row-adjacent number in the tree, and no release has assigned it.**
53 of the 90 Calor0425 diagnostics — **59 %** — are an override or interface implementation
reaching an external base (`Calor0425CorpusLedgerTests.cs:43-46`). It is *not* the
BCL-returned-delegate class the IL-rows trigger names: E3b's 0419 → 0425 retirement re-bucketed
schema 1's four `UnknownSource` entries into it, and the ledger records that the old ELSE arm
"mislabelled them". It has no trigger, no venue, and no residual row in R:§6. **§6 gives it one**,
and R1 reports it per subject beside the binding causes — a cause holding the majority of a gate's
diagnostics may not stay unassigned across two releases.

**The gap between D-A = 2 and D-B = 3121** is the reach problem stated in one line: real C# is full
of higher-order code (3121 sites over 364 files), Calor-native code has two, and the converter
emits no rows (D:1780-1783). Rows meet real code only through conversion, and conversion currently
loses 60 modules at binding and a third of its metadata.

### 0.5 Registered 0.16.x debts carried in (full table §6)

`FunctionBoundType.Row` has no end-to-end reader; lambda parameters invoked in-lambda → Calor0411;
index/`calor build` parity and interface members unindexed; Calor0422/0423 "effect contract
unavailable" and the Calor0208 binding stops; gate 3's CLI-process and SDK legs; gate 5 leg (b);
`§FLD`/`§B` rows not index positions and hover declared-only; solution-level manifests not
consulted; the `ρ_body` under-approximation; **#965's release-blocking perf suite**, the flake
cluster (#948, #959, #884, #859, #1135), and the 3.5.1 null-state slice.

---

## 1. Theme — two halves, and why they are one release

**Reach is the MUST tier; rows soundness contributes one MUST (S1) and the rest as SHOULD.**
*(Draft v1 tiered all of rows soundness SHOULD while §1's claim depended on it; review round 1
finding M3 corrected that, §10.)* They are in the same release because each is the other's
precondition for meaning anything.

- **Reach without rows soundness** produces a bigger denominator over which a leaky charge rule
  still fails to charge. The triggers in §0.3 would keep reading zero for a reason that has
  nothing to do with demand.
- **Rows soundness without reach** fixes a charge rule that meets two Calor-native higher-order
  sites (D-A = 2) and no converted ones. It would be correct and unobservable.

The 0.17 claim, stated so it can fail: *at the end of 0.17 the compiler binds and enforces
materially more converted real code than at the 0.16 branch cut, and every argument shape in
#1136's table is either charged or refused — no shape launders silently.* §4 registers both halves
as proof points; §5 gates them.

**One lesson from PP-W-rows is designed into §4:** both 0.17 proof points are **deterministic
compiles over committed fixtures and pinned corpora**. Neither needs an agent epoch, neither
consumes a usage allowance, and neither can be truncated by a rate limit. PP-W-rows spent $64 of
notional list-rate cost across two collections and produced a size, not an answer; a proof point
that a `dotnet test` can re-run is worth more per unit of evidence than one that cannot.

---

## 2. What is deliberately *not* in this release

- **A re-run of PP-W-rows.** Its fixtures are the defect (W:§6): six tasks that agents complete
  honestly cannot measure whether a compiler stops dishonesty. Redesigning them is a supersession
  under A:90-92 — the ~20 % first-collection shape-realization rate and the 28-run zero are the
  "documented empirical defect in the protocol" that rule requires — and it belongs to a release
  that can afford the collection. **0.18, with the fixture redesign registered before it.** Also:
  re-running it against a compiler that leaks five of twelve shapes would repeat the confound.
- **IL-derived rows.** Trigger reads 0 (§0.3) — against a denominator R1 shows is partial.
  Re-registered in §3.4 against the enforced set **R2** enlarges and the Calor0411 count **R1**
  adds; R1 measures, R2 moves, and the question is only honestly re-asked once both have.
- **Async rows.** Awaiting the maintainer's written adjudication, which A-1.12 made due at the
  0.16 branch cut and which has not been given (§9).

---

## 3. Ship — tiered

### 3.1 MUST — reach

- **R1 — The binding losses, measured and named.** The 60 modules that stop at binding are
  reported only as a count; no committed ledger breaks them out by diagnostic code. R1 extends the
  Calor0425 corpus ledger (schema 4) with a per-subject, per-code breakdown of
  `ExcludedBindFailed`, published with the module names, and names the **largest single cluster**.
  R:§0.4 measured 49 stops as Calor0208 29 / 0250 13 / 0201 7 under the shipped rule over a
  364-file denominator; that breakdown predates W3(a) and is not the current 60.
  **Two additions from review round 1, both denominators the plan was reading without:**
  (i) the **Calor0411 corpus count**, because the unresolved-receiver class never reaches the
  Calor0425 ledger and the IL-rows trigger is currently read against a partial whole (§0.3);
  (ii) **`ExternalBase` per subject**, because it holds 59 % of the gate's diagnostics and no
  release has assigned it (§0.4, §6). *Touches:* `Calor0425CorpusLedgerTests.cs`,
  `calor0425-corpus-ledger.json`, `CHANGELOG.md`.
  *Discriminating:* drop the breakdown and gate 13 (§5) has no denominator to floor.
- **R2 — Fix the largest binding cluster R1 names.** Scoped by measurement, not by guess: R1 runs
  first and its answer picks the target. The pre-registered acceptance is a **move in
  `ModulesEnforced`**, published per subject against gate 9's existing floors (250 aggregate;
  MediatR ≥ 29, serilog ≥ 84, FluentValidation ≥ 137). *Sequencing pin:* R2's PR asserts that the
  schema-4 ledger exists.
- **R3 — Metadata resolution must move.** Gate 6 has read **817/1248 = 65.46 %** since 0.14 and is
  the single largest "the compiler cannot see this code" number in the tree; MediatR at **57.08 %**
  is the outlier and the smallest subject, so it is the tractable one. The registered target is a
  **two-sided move**: the aggregate fraction rises and no subject falls. Related and sequenced
  behind it: #875 (non-nullable `str` at the binder, the root-cause fix for divergence D3) and
  #1082's remaining nullability follow-ons. *Discriminating:* gate 6 is already two-sided per
  subject; a fix that trades serilog for MediatR fails it as written.
- **R4 — The converter's real-code failures. Four defects, not one batch.** Draft v2 called
  #1137 and #1118 "one root cause, two issues"; **both issues say the opposite in their own text**
  (§10 R3-a). They are *complementary*, which is why one fix cannot close both:
  - **R4(a) — #1128:** the emitter writes a narrow effect row while emitting unclassified BCL/LINQ
    calls, so converted code fails Calor0410. *(See §10 R3-f on whether this belongs with R3.)*
  - **R4(b) — #1137:** a class with `§EXT` emits unqualified module-function **calls** → Calor1002
    / CS0103. #1137: *"Same family as #1118 …, **different trigger**: this one hits ordinary
    **calls**, which #1118 explicitly does not."*
  - **R4(c) — #1118:** a module-level function referenced as a **method group** from a class body
    is emitted unqualified. #1118: *"**Calls** … from the same position are **qualified
    correctly** …, so **only the method-group form is affected**."*
  - **R4(d) — #1127:** binder ICE — a named argument matching no parameter throws
    `IndexOutOfRangeException` (`Scope.cs:1677-1686`).
  *Critical path:* **R4(b) alone** is what §3.2 S1 waits on — #1136 records "inherited field,
  unqualified" as *not cleanly measurable end-to-end today* because a class with `§EXT` cannot call
  a module function unqualified, which is #1137. Draft v2 named both, which would have let R4(c)
  block S1 for no reason. *Discriminating:* each lands with the fixture that reproduced it, and
  fixing R4(b) must leave R4(c)'s fixture still red.

**Tier boundary, stated because round 3 asked for it (§10 R3-f).** R3 is *metadata resolution* —
what the binder can look up. R4 is *emission* — what the converter and codegen produce. **#1128
straddles them**: "unclassified BCL/LINQ calls" is an unresolved-metadata symptom with an emission
consequence. It is filed R4(a) because its **fix** is in the emitter, and the rule is stated so the
next boundary case is decided by where the fix lands, not by which symptom is quoted.

**Cut line 1.** If R3 overruns, it defers to 0.17.x **with** gate 6's movement leg restated as
regression-only for this release, named in the release notes — not silently re-tiered. R1 may not
be cut: it is the measurement every other reach claim is floored against.

**Cut line 2 — R2 measured, attempted, and immovable.** Draft v1 defined no outcome for the case
where R1 names a cluster and R2 cannot move it inside the release; a MUST with no failure route is
a MUST that will be quietly redefined. The registered route: R2 publishes **what it attempted and
what the cluster cost**, `ModulesEnforced` stays at its floor, **PP-R1 reads MISS on leg 1** — not
NOT-ADJUDICATED, because the instrument worked and the answer was no — and the cluster is
re-registered in §6 with its measured size as the trigger for the next release. An intractable
cluster is a finding, not an excuse.

### 3.2 MUST — rows soundness

- **S1 — #1136's fix set, entire. MUST.** The issue is explicit that a partial fix is worse than none:
  *"Fixing only fields leaves properties laundering silently."* The registered fix set is
  **{allow a row on `§PROP`} ∪ {fail closed}** —
  (a) `§PROP` gains an effects row (parser `Parser.ParseProperty`, `Ast/PropertyNodes.cs`, the
  eight-position table in `docs/syntax-reference/effects.md`), because today
  `§PROP{…} §E{cw}` is 4× `error Calor0100` and a property escape is `Unknown` **by
  construction**, not an inference gap; **and**
  (b) an argument the pass cannot name is treated as `Unknown` **fail-closed** — `Calor0410`
  rather than an uncharged `Calor0425` warning at exit 0.
  *Acceptance:* every one of #1136's twelve shapes is charged or refused, re-measured on the
  fixtures already committed under `pairs/W-00*/seeded/` with their multisets in
  `ppw-seeded-compiles.json`. *Discriminating:* revert either half and the shape table goes red on
  the half that was reverted — which is why the table, not a summary, is the pin (§4.2).
  **Why MUST, corrected in review round 1:** Draft v1 tiered this SHOULD while §1 registered
  *"every argument shape in #1136's table is either charged or refused"* as half the release
  claim. A claim a release makes must be backed by a MUST — otherwise a slipped SHOULD makes §1
  false and leaves PP-S1 (§4.2) with nothing to adjudicate and no route for it. The concern that
  motivated the SHOULD is real and is handled by sequencing instead: (b) is a fail-closed change to
  a shipped diagnostic and will produce new errors on converted code whose size is unknown until
  R1 measures it, so **S1 lands after R1 and reports the new-error count per subject in its own
  PR**. *Cut line 3:* if that count is large enough that S1 cannot land inside the release, S1
  slips **with §1's claim scoped down in the same commit** — the claim and the tier move together
  or neither moves. **Enforced, not promised** (§10 R3-e): gate 14 then reads **NOT-ADJUDICATED**
  and is published as such. A dropped gate would let the claim outlive its own instrument; a
  NOT-ADJUDICATED one cannot be mistaken for a pass.

### 3.3 SHOULD
- **S2 — W7 carried: `FunctionBoundType.Row` end-to-end** (R15:747; D:2700-2708) and
  lambda-parameter rows (lambda parameters invoked in-lambda → Calor0411). Row-family; the reader
  S1(a) needs for `§PROP` overlaps it.
- **S3 — W8 carried: index parity with `calor build`**, interface members indexed, the index-build
  effect pass measured against gate 8's envelope.
- **S4 — W6 carried: Calor0422/0423** ("effect contract unavailable"), re-read against R1's
  breakdown rather than against the pre-W3(a) numbers.

### 3.4 DEFERRED — frozen residual, triggers re-registered against the enlarged denominator

IL-derived rows for BCL-returned delegates (*trigger:* `UnknownSource + InvocationUndetermined` >
10 over **R2's** enforced set, re-measured at the 0.17 branch cut — 0 over 304 today); Calor0419
at BCL argument sites (*trigger:* D-A `calor0419FunctionTyped` > 10 — 2 today); IL-keyed resolver
keys (*trigger:* gate 6 moves **and** the string-key fraction stays above half — 814/1079 today);
async rows (**maintainer, in writing — now overdue**, §9); `PreconditionSuggester` on the typed CFG
with #909; reflection / `DynamicInvoke` / `dynamic`; `+=`; `§DEL` type parameters (D:1143); rank-2;
E9 (no design); converter-emitted rows (D:1780-1783); the `ρ_body` under-approximation of an
escaping lambda; **the PP-W-rows fixture redesign and re-run (0.18, §2)**.

---

## 4. Honest measurement — two proof points, both free to re-run

### 4.1 PP-R1 — "the compiler binds and enforces materially more converted real code"

*Claim:* at the 0.17 release commit, `ModulesEnforced` over the three pinned subjects exceeds its
0.16 branch-cut value of **304**, and gate 6's resolved fraction exceeds **65.46 %**, with no
subject falling on either.
*Instrument:* the schema-4 Calor0425 corpus ledger (R1) and the metadata ledger, both already
exact-equality tested. *Denominator:* 364 subject modules / 1248 metadata candidates at the pinned
submodule SHAs. *Freeze:* this document, before R2 or R3 merges.
*Pre-registered floors — regression:* `ModulesEnforced` ≥ 304 aggregate; per subject MediatR ≥ 31
and serilog ≥ 88 **exact**, FluentValidation ≥ 179 — this keeps gate 9's own stated precedent
(*"the per-subject MediatR/serilog floors are EXACT and the slack sits in FluentValidation"*,
`calor0425-corpus-ledger.json` `FloorRule.Note`), which Draft v1 dropped without saying why. A
reclassification of one or two modules in the largest subject is not a regression, and an
all-exact floor would red the gate on one.

***Effect size — the RULE is frozen here; only the NUMBER comes from R1.*** "Exceeds 304" makes a
one-module improvement a HIT while §1 claims *materially more*, which is not a proof point. Draft
v2 fixed that by deferring the whole effect size to R1's PR — and **round 3 found that this hands
the release the power to choose its own target after seeing the data** (§10 R3-b), which is the one
thing pre-registration exists to forbid. The size is therefore split:

> **Frozen here, before R1 runs:** R2 must recover **at least half of the largest binding cluster
> R1 names, and never fewer than 10 modules**. R1's PR supplies only that cluster's size; it may
> not choose the fraction or the floor, and its PR asserts both unchanged from this line.

Until R1 lands PP-R1 has no HIT — but the ambition is no longer R1's to set.
*Outcome map:* HIT (both legs move by at least their registered sizes, nothing falls) /
MISS (either fails to move) / **UNDERPOWERED** — on **one condition only**: R1's breakdown
shows the largest cluster is smaller than the frozen floor of **10 modules**, so the corpus cannot
supply the effect under a rule fixed before the data was seen. It is **not** available because half
the cluster turned out to be more work than expected; that is a MISS. (PP-W-rows' UNDERPOWERED
meant "we could not afford the runs"; this one means "the corpus does not contain the thing" — two
failure modes under one label, guarded rather than merged, §10 R3-c) / NOT-ADJUDICATED (a submodule SHA moves, a ledger's
bind rule changes mid-release, or R1's effect size was never registered).
*Discriminating:* revert R2 and `ModulesEnforced` returns to 304 → MISS.

### 4.2 PP-S1 — "no argument shape launders silently"

*Claim:* every one of the twelve argument shapes in #1136's measured table is **charged**
(`Calor0410`) or **refused**, and none produces an uncharged `warning Calor0425` at exit 0.
*Instrument:* a table-driven test over the fixtures #1136 already committed under
`bench/phase0-agent-native/pairs/W-00*/seeded/`, with the frozen multisets in
`ppw-seeded-compiles.json` as the before-state. *Denominator:* **exactly the twelve shapes in #1136's
table**, named individually — five currently escaping, seven currently charged; the seven are the
**controls** and must not change. The issue's two *disclosure* shapes (inherited field unqualified;
module-qualified module function from inside a class body) are **outside the twelve** and are
reported, never scored — stated because an ambiguous denominator is the failure these gates exist
to prevent. *Freeze:* #1136's table, which A-1.12 already registered as a confound and which is
therefore frozen prose, not something this release may edit.
*Two disclosures carried from the issue, not treated as failures:* "inherited field, unqualified"
is not cleanly measurable end-to-end until #1137 lands (R4), and a module-qualified module function
referenced from inside a class body additionally draws `Calor0425` — both are recorded per shape.
*Outcome map:* HIT (twelve of twelve) / MISS (any shape still silent) / NOT-ADJUDICATED (a fixture
or its frozen multiset is edited during the release).
*Discriminating:* revert S1(a) and the `§PROP` row goes red while the field shapes stay green;
revert S1(b) and the alias / other-instance / method-group shapes go red. **The two halves fail
different rows, which is the point of pinning the table instead of a count.**

**Cost, stated because PP-W-rows made it a live question:** both proof points run inside
`dotnet test`. Zero agent runs, zero usage allowance, no rate-limit truncation, and any reviewer
can reproduce them on a laptop.

---

## 5. Release gates

**Carried from 0.16, restated:** 1 (laundering, six closed classes), 2 (higher-order demand ledger,
floor 25), 3 (surface agreement — the MCP leg now exists; the CLI-process and `Calor.Sdk` legs are
**still unbuilt**, issue #1116, and gate 3 claims only the legs it has), 4 (PP-E1 regression pin:
leg A 10/10 with a clean control on every 0.17 commit), 5 (corpus compatibility, leg (a) only —
leg (b) unbuilt), 6 (**resolution floor — two legs, because a floor at today's value cannot gate a MUST whose
content is "must move": (i) regression, ≥ 65.46 % aggregate and no subject below its own current
figure, two-sided as it has always been; (ii) movement, the aggregate strictly above 65.46 % at the
release commit, which is PP-R1's leg 2 and reds gate 6 if R3 lands as a no-op**),
7 (index/query goldens including E7's leg), 8 (harness capture), 9 (**conversion denominator, re-set on the measurement:
`ExcludedParseFailed` = 0, and `ModulesEnforced` ≥ 304 aggregate with MediatR ≥ 31 and serilog ≥ 88
exact and FluentValidation ≥ 179 — the SAME floors §4.1 registers, because two pre-registered
floors over one measurement is how a release gets opposite verdicts from its own instruments**), 11 (non-convergence coverage — Calor0406 at both caps), 12 (turn attribution over
every archived epoch).

**Gate 10 (PP-W-rows) is closed as UNDERPOWERED** and does not gate 0.17; the ledger is a
regression pin only — its bytes must not change without a registered cause.

**New:**

13. **Binding-loss breakdown.** *Instrument:* the schema-4 ledger (R1). *Denominator:* the 60
    modules at the pinned SHAs. *Freeze:* R1's PR, before R2 merges. *Floor:* the breakdown sums
    to `ExcludedBindFailed` per subject — a cause that does not add up is a red gate, not a
    rounding note. *Pin:* delete a cause row → the sum check fails.
14. **The row escape table.** *Instrument:* PP-S1's table-driven test. *Denominator:* twelve
    shapes. *Freeze:* #1136's table. *Pin:* revert either half of S1 and a different set of rows
    goes red (§4.2).

---

## 6. Carried debt — a trigger or an unconditional venue for every residual

| Item | Source | Trigger | Venue |
|---|---|---|---|
| Gate 3's CLI-process and `Calor.Sdk` legs unbuilt | #1116 (R15's line range drifted; the issue is the durable anchor) | — | 0.17: unconditional, with gate 3 claiming only its built legs until then |
| Gate 5 leg (b) never built | R15:996 | — | 0.17.x; gate 5 claims leg (a) only |
| `FunctionBoundType.Row` no end-to-end reader | R15:747; D:2700-2708 | — | 0.17 SHOULD S2 |
| Lambda params invoked in-lambda → Calor0411 | e4:255-259 | — | 0.17 SHOULD S2 |
| Index folds cross-module charges; interface methods unindexed; index-build cost unmeasured | e5:256-275 | — | 0.17 SHOULD S3 |
| Calor0422/0423 (effect contract unavailable) | N:S2.2; R1's breakdown | — | 0.17 SHOULD S4 |
| Calor0410 demoted under `--permissive-effects` | e4:246-248 | **the 0.16.0 release commit** — R:§6's actual end condition, which has not happened | frozen; re-adjudicated **after 0.16.0 ships**, not before. PP-W-rows' arm A was the freeze's *rationale*, not its end condition, and a frozen rule may not be released early because its rationale weakened (§9) |
| **`ExternalBase` — 53 of 90 Calor0425 diagnostics (59 %), an override or interface implementation reaching an external base** | §0.4; `Calor0425CorpusLedgerTests.cs:43-46` | **none until now** — carried unassigned through 0.15 and 0.16 | **0.17: R1 reports it per subject; a venue is registered in the 0.17 release notes from that breakdown.** Not the IL-rows class, so IL rows' trigger does not cover it |
| **Calor0411 over the corpus is uncounted** — the unresolved-receiver class never reaches the Calor0425 ledger by construction | `Calor0425CorpusLedgerTests.cs:57-60` | — | **0.17 R1**, unconditional: the IL-rows trigger cannot be read again against a partial denominator |
| `ρ_body` under-approximation on an escaping lambda | e4:230-234 | a fixture measured silent | DEFERRED |
| `§FLD`/`§B` rows not index positions; hover declared-only | e5:168-175 | — | 0.17.x |
| Solution-level manifests not consulted by the index | e5:256-258 | a corpus solution with manifests | 0.17.x |
| #965 perf suite kills the CI runner; runs only on the release path | #965 | recurrence on the 0.16.0 release | **release-blocking for 0.17.0 if it recurs**; §9 asks for the 0.16.0 observation, and records an exit-143 kill on the `tests (compiler)` shard (run `33546049628`) that widens the issue beyond the perf suite |
| Flake cluster #948, #959, #884, #859, #1135 | R15:1040; R:§7 | flake rate attached at the branch cut | §9 — **overdue**, was due at the 0.16 branch cut |
| 3.5.1 null-state slice; #845; #970 tri-state; TIER1A not-run | R15:1040 | maintainer | 0.17.x instrument debt |
| #901 stale benchmark subjects; #929 parser dedent; #943 ref/out call syntax; #906 interpolation invisible to the binder | issue list | — | 0.17.x; #943 is a language addition, not a fix |
| #1139 `§YIELD` in a property accessor; #1132 `§ARR2D` name mismatch; #1121, #1131, #1134, #1142, #1143, #1144 | issue list | — | 0.17.x housekeeping |
| PP-W-rows fixture redesign | W:§6 | A:90-92 supersession registered first | **0.18** |

---

## 7. Backlog disposition — the open issues on 2026-09-01

| Issue | Disposition |
|---|---|
| #1136 | **0.17 MUST S1** — the fix set entire; a partial fix is refused by the issue itself |
| #1128, #1137, #1118, #1127 | **0.17 MUST R4(a)–(d)** — four *complementary* defects, not one batch (§10 R3-a); **#1137 alone** (R4(b)) unblocks PP-S1's inherited-field shape |
| #875, #1082 | 0.17 MUST R3, sequenced behind the measurement |
| #1116 | 0.17 unconditional (gate 3's remaining legs) |
| #965 | release-blocking for 0.17.0 if it recurs; observation due from the 0.16.0 release |
| #948, #959, #884, #859, #1135 | 0.17.x instrument debt; flake rate attached — **overdue since the 0.16 branch cut** |
| #943, #929, #906, #901 | 0.17.x |
| #1139, #1132, #1121, #1131, #1134, #1142, #1143, #1144 | 0.17.x housekeeping |
| #909 | 0.17.x with the `PreconditionSuggester` residual |
| #1084, #847, #922, #845 | demand-driven |
| #1011 (R1–R14), #1030, #1031, #1032, #1042 | continuous |
| #673, #709, #711 | adoption work, not release-gated |
| #903, #1097, #1104 | **closed by 0.16** (W3(a), W3(b), W3(c)) — verified closed on GitHub |
| #1094 | W1 shipped its content (PR #1119 archives `.calor-build-state.json` per run) but **the issue is still OPEN** — close it with the 0.16.0 release, or say why it is not done |

---

## 8. What "better than C#" means at the end of 0.17 — testable

C# cannot tell you that a callback passed into a helper does something the helper's signature says
it does not. Calor can — **for every shape you can write it in**, which is what §4.2 makes
testable and what today's compiler fails for five of twelve. And it can tell you so over converted real code — **which is
the ambition, not today's state, and Draft v2's §8 said otherwise** (§10 R3-d). 304 of 364 modules
reach the effect pass, but reaching it is not the same as the claim being *checkable* there:
**D-A = 2** says Calor-native higher-order sites number two, and the converter emits no rows at all
(D:1780-1783), so on nearly all of those 304 modules there is no callback row to check. §4.1 makes
the **reach** testable. What no instrument yet makes testable is the claim *on* that reach, and no
release should imply otherwise until the converter emits rows. Neither claim is about rows being clever; both
are about the claim being *checkable on code someone actually wrote*.

---

## 9. Maintainer adjudications now due

Three were made due at the 0.16 branch cut by A-1.12 and R:§6 and have not been given. They are
listed here as questions, not as assumptions:

1. **Async rows** (D:§11, 1922-1945) — the D:1936-1942 three-clause test, "adjudicated by the
   maintainer in writing at the 0.16 branch cut". Overdue. This draft holds them DEFERRED, which
   is a placeholder, not the adjudication.
2. **The flake rate** for #859 / #884 / #959 / #948 / #1135, "attached at the 0.16 branch cut".
   No rate has been measured or attached. Until it is, "0.17.x instrument debt" in §6 is a venue
   without a size.
3. **#965** — whether the perf suite killed the CI runner on the 0.16.0 release path. R:§7 makes
   it release-blocking for the *next* release if it recurs; the observation has to come from the
   0.16.0 release itself, which has not happened. **A first data point arrived while this draft
   was being written:** on PR #1145, run `33546049628`, the `tests (compiler)` shard was killed at
   **exit 143** — `##[error]The runner has received a shutdown signal` — after 10m33s with **zero
   test failures** in its log (every result line reads `Passed`). That is the exit-143 signature
   #965 names, on a *different* shard from the perf suite, which widens the issue rather than
   confirming it: whatever kills the runner is not specific to `Calor.Performance.Tests`. It did
   **not** reproduce on retry — the same job on the same tree went green. **But it then happened a
   second time the same day**, on the *release* PR #1147 (run `33565838496`, `tests (compiler)`
   again, killed at 8m26s with **7,040 passing result lines and zero test failures**). So the
   honest reading has moved once already and is now: **two exit-143 kills of the same shard, on
   two different trees, within one day, each passing on retry.** That is still the *intermittent*
   shape (#959 / #948) rather than #965's *reproducible* kill of the perf suite — the two remain
   distinct and should not be filed together — but a rate of two in a day on a release-path shard
   is not the rare flake the first observation suggested. **This is exactly why §9.2's flake rate
   is the deliverable**: two data points already moved the reading twice, and no one is counting.
   Recorded as an observation, not an adjudication.

A fourth is **not** created by this draft, and Draft v1's first version of it was wrong. The
**`--permissive-effects` demotion of Calor0410** (§6) is frozen **through the 0.16.0 release
commit** (R:§6), which has not happened. PP-W-rows' arm A was the *rationale* for that freeze, not
its end condition; reading the proof point's closure as releasing the freeze early is precisely the
post-hoc loosening the freeze discipline exists to prevent. It becomes due **after 0.16.0 ships**,
and is listed here only so it is not forgotten then.

---

## 10. Adversarial review

### Round 1 — 2026-09-01, three lenses, on Draft v1

**Conducted by the same agent that wrote Draft v1**, which is a real limitation and is stated
rather than hidden: a self-review cannot find an error whose cause is a misunderstanding the
reviewer shares. Every finding below was checked against the tree — the ledgers, the test sources,
the GitHub issue states — not against the prose. **Rounds 2 and 3 are still owed**, and the
document is Draft v2, not final.

**Verdict:** MEASUREMENT — *needs-fixes*, one Major. PLAN/PROCESS — *needs-fixes*, two Majors.
ENGINEERING — *approve with fixes*. All **twelve** findings **applied**; none declined.
*(Draft v2 said "eleven" here and in its own status line — the table has always had twelve rows.
Round 2 caught it; see R2-c below.)*

| # | Lens | Finding | Disposition |
|---|---|---|---|
| **M1** | measurement | **The instrument has a blind spot the draft read straight past.** `Calor0425CorpusLedgerTests.cs:57-60` states that the unresolved-receiver class *never reaches Calor0425* — it goes out as **Calor0411** — and **no committed ledger counts Calor0411 over the corpus**. "Every trigger reads zero" was therefore partly a statement about where the instrument looks. *The attack that failed:* the trigger's named fields were checked against their definitions and **do** still mean what its author meant (`InvocationUndetermined` is defined as "§13.4's 'BCL-returned delegate'" verbatim), so the trigger is correctly specified — only its denominator is partial. | **Applied.** §0.3 discloses it; **R1 adds the Calor0411 corpus count**; §6 carries it unconditionally; §2 and §3.3 reworded (n11). |
| **M2** | process | **The draft released a frozen rule early.** §6 and §9 claimed the `--permissive-effects` Calor0410 freeze "has lapsed" because PP-W-rows closed. R:§6 freezes it **through the 0.16.0 release commit**, which has not happened; PP-W-rows' arm A was the freeze's *rationale*, not its end condition. Releasing a freeze because its rationale weakened is exactly the post-hoc loosening the discipline exists to prevent — and the draft did it to itself, unprompted, in its first hour. | **Applied.** §6's trigger restated to the real end condition; §9's "fourth adjudication" withdrawn and corrected in place, with the error left visible. |
| **M3** | plan | **A release claim backed by a SHOULD.** §1 registers *"every argument shape in #1136's table is either charged or refused"* as half the release claim while §3.2 tiered S1 as SHOULD. A slipped SHOULD would make §1 false and leave PP-S1 with nothing to adjudicate and no route for it. | **Applied.** S1 promoted to **MUST**. The real concern behind the SHOULD (unknown new-error volume) is handled by sequencing S1 behind R1 and reporting the count per subject, plus **cut line 3**: if S1 slips, §1's claim is scoped down *in the same commit*. |
| m4 | measurement | **PP-R1 had no effect size.** "Exceeds 304" makes a one-module improvement a HIT while §1 claims *materially more*. PP-E1 and PP-W-rows both registered Δ and power; this did not. | **Applied.** The size cannot honestly be written before R1 names the cluster, so it is a **deferred registration with a named deadline and deriver** — R1's PR, before R2 merges — and **PP-R1 has no HIT until it lands**. An `UNDERPOWERED` outcome is added for "the corpus cannot supply the effect", so the release cannot discover that after the fact. |
| m5 | measurement | **Exact floors on all three subjects dropped gate 9's own slack precedent** (`FloorRule.Note`: *"the per-subject MediatR/serilog floors are EXACT and the slack sits in FluentValidation"*) with no stated reason. One reclassified module in the largest subject would red the gate. | **Applied.** MediatR ≥ 31 and serilog ≥ 88 exact; FluentValidation ≥ 179. |
| m6 | plan | **R2 had no failure route.** No outcome was defined for "R1 named the cluster, R2 could not move it". A MUST with no failure route is a MUST that gets quietly redefined. | **Applied.** **Cut line 2**: R2 publishes what it attempted and what the cluster cost, PP-R1 reads **MISS** on leg 1 — not NOT-ADJUDICATED, the instrument worked and the answer was no — and the cluster is re-registered with its measured size. |
| m7 | measurement | **Gate 6 as restated could not gate its own MUST.** "≥ 65.46 %" is the current value, so the gate was regression-only while R3's content is "must move". | **Applied.** Gate 6 split into a regression leg and a **movement leg**; R3 landing as a no-op now reds the gate. |
| m8 | measurement | **The largest row-adjacent number in the tree was unassigned.** `ExternalBase` holds **53 of 90** Calor0425 diagnostics (59 %) and has carried through 0.15 and 0.16 with no trigger, no venue and no residual row. It is *not* the IL-rows class — E3b re-bucketed schema 1's four `UnknownSource` entries into it — so IL rows' trigger does not cover it. | **Applied.** §0.4 states the share and the distinction; R1 reports it per subject; §6 gives it a row and the 0.17 release notes a venue. |
| n9 | fact | §7 listed **#1094 as "closed by 0.16"**. It is **OPEN** (verified on GitHub). W1 shipped its content in PR #1119. | **Applied.** Its own row: close it with the release, or say why it is not done. |
| n10 | measurement | §4.2's "twelve shapes" did not say the issue's two *disclosure* shapes are **outside** the twelve. An ambiguous denominator is the failure these gates exist to prevent. | **Applied.** Denominator stated as *exactly* the twelve; the two disclosures reported, never scored. |
| n11 | consistency | §2 re-registered IL rows against "the enlarged denominator **R1** produces"; §3.3 said "**R2's** enforced set". R1 measures; R2 moves. | **Applied.** Both now say R1 measures and R2 enlarges, and name the Calor0411 count from M1. |
| n12 | sourcing | §0.4 sourced "modules parsed" to the Calor0270 ledger's `AggregateModulesBound`, a field named *Bound* on a ledger whose `BindRule` is `"parsed"`. | **Applied.** The Calor0425 ledger's `ExcludedParseFailed` is named as the authority; Calor0270 is corroboration. |

### What round 1 did not do

It did not re-derive the four trigger numbers independently — it re-read the same ledgers the draft
read. A reviewer who has not seen this session should recompute `UnknownSource`,
`InvocationUndetermined`, `calor0419FunctionTyped`, `RowlessNeverInvoked` and gate 6 from the
committed JSON before relying on §0.3. It also did not attack §8 or the tier boundary between R3
and R4, and it found nothing in §3.1 R4 — which for a batch of four issues is more likely to mean
the lens missed something than that the batch is clean.


### Round 2 — 2026-09-01, independent (cloud multi-agent), on Draft v2

**Not conducted by the author.** Round 1 predicted its own weakest point — *"a self-review cannot
find an error whose cause is a misunderstanding the reviewer shares"* — and round 2 hit it
squarely: its central finding is that **two of round 1's twelve "Applied" dispositions were applied
to only one of the two sites they named**, leaving the document contradicting itself and §10's
verdict false against the tree. No amount of further self-review would have found that.

Three merged findings, all filed *nit* by severity and none of them cosmetic in effect. **Nine
sub-findings applied; two recorded as stale-base artifacts.**

| # | Finding | Disposition |
|---|---|---|
| **R2-a** | **Round 1's m5 was half-applied.** The FluentValidation slack floor (≥ 179) landed in §4.1 PP-R1 but §5 gate 9 still read *"`ModulesEnforced` ≥ 304, exact per subject"*. Round 2 supplied the splitting measurement: MediatR 36 / serilog 88 / FV 180 / aggregate 304 is a **PP-R1 HIT and a gate 9 RED** — one measurement, two pre-registered floors, opposite verdicts, which is the exact failure pre-registration exists to prevent. | **Applied.** Gate 9 now carries §4.1's floors verbatim, with the reason stated inline. |
| **R2-b** | **Round 1's M2 was half-applied.** The Calor0410-freeze correction landed in §9 but §6's row still read *"PP-W-rows is closed, so the freeze that protected it has lapsed \| re-adjudicate in 0.17"* — **verbatim the wording M2 flagged as "the post-hoc loosening the discipline exists to prevent"**. The document asserted three inconsistent things about one rule. *Cause, for the record:* the edit script carrying that fix aborted on a later assertion before writing, and the retry covered only §9. | **Applied.** §6's Trigger and Venue now mirror §9's corrected end condition. |
| **R2-c** | §7 still tiered **#1136 as "SHOULD S1"** after round 1's M3 promoted it to MUST — a third half-applied fix, in the same sweep. And the header and §10 verdict both said **"eleven findings"** where the table has always had **twelve** rows (M1–M3, m4–m8, n9–n12); the wrong count reached the commit message too. | **Applied.** §7 reads MUST; the count is corrected in both places with the error named. |
| **R2-d** | **Four copied `R:` line ranges do not resolve.** v0.16's *R:* was `roadmap-v0.13-v0.15.md`; Draft v1 redefined *R:* as `roadmap-v0.16.md` and then copied v0.16's citations verbatim. R:938-940 / R:916-932 / R:978-984 point past `roadmap-v0.16.md`'s EOF (767 lines), and R:710-720 resolves to v0.16's **round-2 review table**, not the `FunctionBoundType.Row` claim. | **Applied.** **`R15:` added to Governing Inputs** and each range re-anchored *by grep, not by arithmetic*: `compile-all-committed-calr` → R15:996, `FunctionBoundType.Row` → R15:747, the 3.5.1 row → R15:1040. Gate 3's range is dropped for **#1116**, a durable anchor, because the line range had drifted and a citation nobody can check is worse than none. |
| **R2-e** | **`e4:`, `e5:` and `N:` are used in §6 but never defined** — v0.16 declared them and Draft v1 dropped them while keeping the citations. | **Applied.** All three added to Governing Inputs. |
| **R2-f** | §0.4's D-B parenthetical enumerates 2676 + 311 + 132 = **3119** against a stated total of **3121**; the missing 2 is `delegateDeclarations` in the ledger. | **Applied.** The parenthetical now lists all four components. |
| **R2-g** | Cut lines are defined in prose order **1, 3, 2**, and §10 m6 cites "Cut line 3" for the one in §3.1. | **Applied.** Renumbered to prose order; §10 m6's reference updated with them. |
| **R2-h** | *"`2026-09-01-ppw-rows-dry-run.md` does not exist in the repo or git history, so every `W:` citation is unresolvable and the N = 9 / \$287 / 28-run-zero / leg-B 1.1175 numbers cannot be reproduced from any committed source."* | **Stale-base artifact, not applied.** The file was merged to `main` in **PR #1145** (`82a7c653`) — `git ls-tree origin/main docs/plans/2026-09-01-ppw-rows-dry-run.md` returns blob `8a4130d3`. The review was launched against a base that predated the merge; this was flagged as a likely artifact *before* the round ran, for exactly this reason. **No change made, and no credit taken:** the finding is correct about its own tree and would have been a Major on it. |
| **R2-i** | *"`effect-rows-benefit-ledger.json` does not exist — the file with legA/legB fields is `effect-rows-probe-ledger.json`, which is PP-E1 data and non-null, so the present-tense claim is misleading."* | **Stale-base artifact, not applied.** Same cause: merged in PR #1145, `git ls-tree origin/main` returns blob `d25c6e40`, and its `legA` / `legB` are null with `verdict: "UNDERPOWERED"` as §0.2 states. The reviewer's *distinction* is nonetheless correct and worth keeping in view — `effect-rows-probe-ledger.json` is PP-E1's, non-null, and a different instrument. |

**What round 2 changes about how to read round 1.** Round 1's eleven — twelve — findings were
real and its analysis held up; what failed was **its own application step**, three times out of
twelve, in a way it then certified as complete. The lesson is narrow and worth keeping: a review
that both finds and applies its own findings needs a separate pass that re-reads the tree for each
disposition, or an independent round to do it. Round 3 should verify **every** round-1 and round-2
disposition against the tree before looking for anything new.

### What round 2 did not do

It did not recompute the four trigger numbers in §0.3 from the committed JSON — the anti-anchoring
task round 1 named as its own first gap is **still open**. It did not attack §8, the R3/R4 tier
boundary, or R4's four-issue batch, all of which round 1 also left alone; two rounds have now
passed over R4 without a finding. And it did not examine the new mechanisms round 1 introduced —
cut lines 2 and 3, the deferred effect-size registration, PP-R1's `UNDERPOWERED` outcome — which
remain reviewed by nobody.

### Round 3 — 2026-09-01, on Draft v3

**Task order fixed by round 2:** verify every prior disposition against the tree *before* looking
for anything new. That pass ran first and mechanically — 27 assertions, one per disposition site,
each checking the **body** of the document rather than §10's claim about it.

**Verification: 27/27 landed.** Every round-1 and round-2 disposition is present at the site it
names. Round 2's three half-applied fixes are the only ones that ever failed this check, and they
are now closed.

**§0.3's four trigger numbers, recomputed independently — the gap rounds 1 and 2 both left open.**
Not re-read: recomputed from the raw JSON with fresh arithmetic, plus the sum checks that caught
round 2's D-B defect.

| Check | Result |
|---|---|
| IL rows: `UnknownSource` 0 + `InvocationUndetermined` 0 | **0**, does not fire (> 10) |
| Calor0419: D-A `calor0419FunctionTyped` | **2**, does not fire |
| Gate 6: 817 / 1248 recomputed from `perSubject` | **65.46 %**, matches |
| Q4: `RowlessNeverInvoked` | **0**, does not fire |
| **Sum check** — the seven cause fields vs `AggregateDiagnostics` | 90 = 90 **MATCH** (`InvocationWitness` 26 is an overlay, not a cause) |
| **Sum check** — `ModulesEnforced` + all `Excluded` vs the 364 denominator | 304 + 60 = **364 OK** |
| **Sum check** — D-B's five components vs its total | 3121 = 3121 **MATCH** |
| Doc claims 304 / 60 / 0 / 90-over-40 / 53 = 59 % / 31-88-185 / D-A 2 | **all reconcile** |

§0.3 is load-bearing and now stands on an independent recomputation.

**Six new findings, all applied.**

| # | Lens | Finding | Disposition |
|---|---|---|---|
| **R3-a** | plan | **R4 mis-scoped a MUST, and both issues say so in their own text.** Draft v2 called #1137 and #1118 "one root cause, two issues". #1137: *"different trigger: this one hits ordinary **calls**, which #1118 explicitly does not."* #1118: *"**Calls** … are **qualified correctly** …, so **only the method-group form is affected**."* They are complementary — one fix cannot close both — and #1136 calls them "same fixture family, **different mechanism**". A batch scoped on a shared root cause would have fixed one and reported the theme done. Two rounds passed over R4 without a finding; §10 predicted that silence was a miss. | **Applied.** R4 split into R4(a)–(d) with each issue's own words quoted. **S1's critical path corrected to R4(b) alone** — Draft v2 named both, which would have let R4(c) block a MUST for no reason. |
| **R3-b** | process | **Round 1's own fix handed the release the power to set its own target after seeing the data.** m4 deferred PP-R1's *entire* effect size to R1's PR. But R1 is the measurement — so whoever writes R1's PR chooses the ambition **with the breakdown in front of them**, which is precisely what pre-registration forbids. A fix for an unregistered number created an unregistered *rule*. | **Applied.** Split: the **rule is frozen now** — R2 recovers *at least half the largest cluster R1 names, never fewer than 10 modules* — and R1 supplies only the cluster's size, asserting the fraction and floor unchanged. |
| **R3-c** | measurement | **PP-R1's `UNDERPOWERED` conflated two failure modes under one label.** For PP-W-rows it meant *"we could not afford the runs"*; here it meant *"the corpus cannot supply the effect"* — and as written it was reachable whenever the cluster was smaller than a size R1 itself had chosen, i.e. an escape hatch from R3-b's hole. | **Applied.** Available on one condition only: the largest cluster is below the frozen floor of 10. "Harder than expected" is a **MISS**. |
| **R3-d** | measurement | **§8 overstated what the release can demonstrate.** It said the callback claim is testable *"over converted real code … 304 of 364 modules"*. Reaching the effect pass is not the claim being checkable there: **D-A = 2**, and the converter emits no rows, so nearly all 304 modules contain no callback row to check. §4.1 makes the reach testable, not the claim on it. | **Applied.** §8 now separates the ambition from today's state and names D-A = 2 as the reason. |
| **R3-e** | process | **Cut line 3 was a promise with no gate.** "S1 slips *with §1's claim scoped down in the same commit*" had nothing enforcing it; a slipped S1 would simply leave gate 14 unmentioned and the claim standing. | **Applied.** Gate 14 reads **NOT-ADJUDICATED** and is published as such — a claim may not outlive its instrument. |
| **R3-f** | plan | **The R3/R4 tier boundary was never stated**, and #1128 straddles it: "unclassified BCL/LINQ calls" is an unresolved-metadata symptom with an emission consequence. Nothing said which tier owns it or why. | **Applied.** The rule is stated — **R3 is what the binder can look up, R4 is what the emitter produces, and a straddling issue is filed where its fix lands, not where its symptom is quoted.** #1128 stays R4(a) on that rule. |

### A process note round 3 earned the hard way

While applying these findings, the first edit script aborted on a later assertion **before writing**
— the exact mechanism that caused round 1's three half-applied fixes (R2-b). It was caught because
round 2 had made it a known failure mode, and the remaining edits were applied one per script with
its own write. **The lesson generalises past this document:** a batch of fixes that commits at the
end is one failed assertion away from silently applying none of them while the author believes all
landed. Round 3's verification pass exists because of that, and should run at the top of every
future round.

**And it caught round 3 doing it too.** Re-running the assertions after applying R3-a–f showed §7
still reading *"#1137/#1118 share one root cause"* — R3-a applied to §3.1 and not to §7, the third
appearance of this exact half-application in three rounds. It is recorded rather than quietly
fixed: **the failure mode is not carelessness that can be resolved by trying harder** — it recurred
after being named, diagnosed, and consciously guarded against. What actually caught it was a
mechanical re-check, both times.

### What round 3 did not do

It did not attack §5's gates 1–8 and 11–12, carried from 0.16 and re-stated rather than re-derived.
It did not check whether the eight 0.16 gates still *mean* anything against a 0.17 tree. It did not
attack §7's dispositions for the two dozen issues not in R1–R4 or S1. And it remains a
self-conducted round on a document this author wrote — the independent round (2) found three things
no self-review had, and a fourth independent pass would be worth more than a fifth self-conducted
one.