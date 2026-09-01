# Roadmap — v0.17 "Reach, and the Rows We Shipped"

**Date:** 2026-09-01
**Status:** **Draft v1** — no adversarial round has run against it. §10 is empty on purpose; the
v0.16 roadmap reached Draft v4 through three rounds, and this document should not be treated as
comparable until it has had at least as many.
**Written against:** `cb68afcf` (main after PR #1140), with PP-W-rows adjudicated in PR #1145.
`Directory.Build.props:3` reads `0.15.0`; **v0.16.0 has not been released**, and every number
below is measured at the 0.16 branch cut, not after it.
**Governing inputs:** `roadmap-v0.16.md` (*R:*), `2026-09-01-ppw-rows-dry-run.md` (*W:*),
`docs/design/effect-rows-in-the-type-system.md` (*D:*), `docs/plans/agent-native-gates.md` (*A:*,
read-only), issue #1136's measured table, the four corpus ledgers under
`bench/phase0-agent-native/`, and the open issue list on 2026-09-01.

---

## 0. Where 0.16 left us — measured at the branch cut

### 0.1 Shipped

E7 (`calor_query` over the project index, gate 3's MCP leg and gate 7's E7 leg), K1 (the P32 ledger
on the shipping bind rule), W1 (per-turn capture), W3(a)(b)(c) (converter reach for #903 and #1097,
the #1104 recursion bound), W4 (turn attribution), W5 (Calor0406 at both caps, `Calor0600` retired
at `ProcessScc`), `CalorPermissiveEffects` through `Calor.Tasks`, and the kickoff sweep (ES-08
registered, #982/#981/#976/#949 dispositioned).

**Not built, carried again:** gate 5 leg (b)'s `compile-all-committed-calr` job (R:938-940) and
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

**The reading this document refuses to take on its own.** "Zero demand" and "the denominator is too
small to contain the demand" are not distinguishable from these numbers. Every trigger is counted
over code the compiler successfully binds and enforces; §0.4 is the measure of how much real code
that is. §1 is built on the second reading, and §4 registers the instrument that would tell them
apart.

### 0.4 Reach — the denominator, exact

Measured over the three A-1.5.3 conversion subjects at their pinned submodule commits.

| Instrument | Value | Source |
|---|---|---|
| Modules parsed | **364 of 364** — `ExcludedParseFailed` **0** on every subject | `calor0270-corpus-ledger.json` (`AggregateModulesBound` 364); `calor0425-corpus-ledger.json` |
| Modules reaching the effect pass (`ModulesEnforced`, propagated rule) | **304** — MediatR 31, serilog 88, FluentValidation 185 | `calor0425-corpus-ledger.json` |
| Modules stopping at binding (`ExcludedBindFailed`) | **60** — MediatR 5, serilog 24, FluentValidation 31 | same |
| Calor0425 emitted | 90 diagnostics over 40 of 304 modules | same |
| Calor0425 dominant cause | `ExternalBase` **53** (serilog 13, FV 40); `RowlessDestination` 11, all of them `RowlessInvoked` | same |
| Calor0270 (SignatureUnresolved), raw-bag rule | 315 over 39 of 364 | `calor0270-corpus-ledger.json` |
| Metadata resolution (gate 6) | **817 / 1248 = 65.46 %** — MediatR **129/226 = 57.08 %**, serilog 104/113 = 92.04 %, FV 584/909 = 64.25 % | `metadata-binding-corpus-ledger.json` |
| Higher-order demand | D-A **2**; D-B **3121** over 364 files (2676 lambdas, 311 delegate-typed declarations, 132 delegate invocations) | `higher-order-demand-ledger.json` |
| Resolver keys | 265 bound / 814 string | `effect-resolver-key-ledger.json` |

**Two numbers carry this release.** W3(a) took parse failures to **zero** — gate 9's floor was
`ExcludedParseFailed ≤ 2` and the measurement is 0, and `ModulesEnforced` landed at 304 against a
floor of 250, matching R:§5 gate 9's published expectation of "≈ 256 + 48" exactly. What did *not*
move is everything after parsing: **60 modules still stop at binding**, and metadata resolution is
**unchanged at 65.46 %** — the same 817/1248 gate 6 has read since 0.14. The reach frontier moved
from the parser to the binder, and nobody has measured what is behind it: the 60 modules' failure
causes are **not broken out by code in any committed ledger**. §3.1 R1 is that measurement.

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

**Reach is the MUST tier. Rows soundness is the SHOULD tier.** They are in the same release
because each is the other's precondition for meaning anything.

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
- **IL-derived rows.** Trigger reads 0 (§0.3). Re-registered in §6 against the *enlarged*
  denominator R1 produces, which is the honest way to ask the question again.
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
  364-file denominator; that breakdown predates W3(a) and is not the current 60. *Touches:*
  `Calor0425CorpusLedgerTests.cs`, `calor0425-corpus-ledger.json`, `CHANGELOG.md`.
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
- **R4 — The converter's real-code failures, as a batch.** Four issues, one theme — converted real
  code does not survive the pipeline: **#1128** (the emitter writes a narrow effect row while
  emitting unclassified BCL/LINQ calls, so converted code fails Calor0410), **#1137 + #1118** (a
  class with `§EXT`, or any class body, emits unqualified module-function calls → Calor1002 /
  CS0103 — one root cause, two issues), **#1127** (binder ICE: a named argument matching no
  parameter throws `IndexOutOfRangeException`, `Scope.cs:1677-1686`). *Note:* #1137/#1118 are on
  the critical path for §3.2 S1 — #1136 records "inherited field, unqualified" as *not cleanly
  measurable end-to-end today* precisely because of #1137.
  *Discriminating:* each lands with the fixture that reproduced it.

**Cut line 1.** If R3 overruns, it defers to 0.17.x **with** gate 6 restated as regression-only for
this release, named in the release notes — not silently re-tiered. R1 may not be cut: it is the
measurement every other reach claim is floored against.

### 3.2 SHOULD — rows soundness

- **S1 — #1136's fix set, entire.** The issue is explicit that a partial fix is worse than none:
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
  **Why SHOULD and not MUST:** (b) is a fail-closed change to a shipped diagnostic and will
  produce new errors on converted code whose size is unknown until R1/R2 enlarge the denominator;
  it is sequenced behind them deliberately, and a SHOULD that slips is named in the release notes.
- **S2 — W7 carried: `FunctionBoundType.Row` end-to-end** (R:710-720; D:2700-2708) and
  lambda-parameter rows (lambda parameters invoked in-lambda → Calor0411). Row-family; the reader
  S1(a) needs for `§PROP` overlaps it.
- **S3 — W8 carried: index parity with `calor build`**, interface members indexed, the index-build
  effect pass measured against gate 8's envelope.
- **S4 — W6 carried: Calor0422/0423** ("effect contract unavailable"), re-read against R1's
  breakdown rather than against the pre-W3(a) numbers.

### 3.3 DEFERRED — frozen residual, triggers re-registered against the enlarged denominator

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
*Pre-registered floors:* `ModulesEnforced` ≥ 304 aggregate and per subject (MediatR ≥ 31,
serilog ≥ 88, FluentValidation ≥ 185) — **exact floors, not the 0.16 slack floors**, because the
measurement is now the baseline; gate 6 ≥ 65.46 % aggregate and no subject below its own current
figure. *Outcome map:* HIT (both move, nothing falls) / MISS (either fails to move) /
NOT-ADJUDICATED (a submodule SHA moves, or a ledger's bind rule changes mid-release).
*Discriminating:* revert R2 and `ModulesEnforced` returns to 304 → MISS.

### 4.2 PP-S1 — "no argument shape launders silently"

*Claim:* every one of the twelve argument shapes in #1136's measured table is **charged**
(`Calor0410`) or **refused**, and none produces an uncharged `warning Calor0425` at exit 0.
*Instrument:* a table-driven test over the fixtures #1136 already committed under
`bench/phase0-agent-native/pairs/W-00*/seeded/`, with the frozen multisets in
`ppw-seeded-compiles.json` as the before-state. *Denominator:* twelve shapes, named individually —
five currently escaping, seven currently charged; the seven are the **controls** and must not
change. *Freeze:* #1136's table, which A-1.12 already registered as a confound and which is
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
leg (b) unbuilt), 6 (**resolution floor, re-set: ≥ 65.46 % aggregate, two-sided per subject**),
7 (index/query goldens including E7's leg), 8 (harness capture), 9 (**conversion denominator,
re-set on the measurement: `ExcludedParseFailed` = 0 and `ModulesEnforced` ≥ 304, exact per
subject**), 11 (non-convergence coverage — Calor0406 at both caps), 12 (turn attribution over
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
| Gate 3's CLI-process and `Calor.Sdk` legs unbuilt | R:916-932; #1116 | — | 0.17: unconditional, with gate 3 claiming only its built legs until then |
| Gate 5 leg (b) never built | R:938-940 | — | 0.17.x; gate 5 claims leg (a) only |
| `FunctionBoundType.Row` no end-to-end reader | R:710-720; D:2700-2708 | — | 0.17 SHOULD S2 |
| Lambda params invoked in-lambda → Calor0411 | e4:255-259 | — | 0.17 SHOULD S2 |
| Index folds cross-module charges; interface methods unindexed; index-build cost unmeasured | e5:256-275 | — | 0.17 SHOULD S3 |
| Calor0422/0423 (effect contract unavailable) | N:S2.2; R1's breakdown | — | 0.17 SHOULD S4 |
| Calor0410 demoted under `--permissive-effects` | e4:246-248 | PP-W-rows is closed, so the freeze that protected it has lapsed | **re-adjudicate in 0.17** — the arm it was frozen for no longer exists |
| `ρ_body` under-approximation on an escaping lambda | e4:230-234 | a fixture measured silent | DEFERRED |
| `§FLD`/`§B` rows not index positions; hover declared-only | e5:168-175 | — | 0.17.x |
| Solution-level manifests not consulted by the index | e5:256-258 | a corpus solution with manifests | 0.17.x |
| #965 perf suite kills the CI runner; runs only on the release path | #965 | recurrence on the 0.16.0 release | **release-blocking for 0.17.0 if it recurs**; §9 asks for the 0.16.0 observation, and records an exit-143 kill on the `tests (compiler)` shard (run `33546049628`) that widens the issue beyond the perf suite |
| Flake cluster #948, #959, #884, #859, #1135 | R:978-984 | flake rate attached at the branch cut | §9 — **overdue**, was due at the 0.16 branch cut |
| 3.5.1 null-state slice; #845; #970 tri-state; TIER1A not-run | R:978-984 | maintainer | 0.17.x instrument debt |
| #901 stale benchmark subjects; #929 parser dedent; #943 ref/out call syntax; #906 interpolation invisible to the binder | issue list | — | 0.17.x; #943 is a language addition, not a fix |
| #1139 `§YIELD` in a property accessor; #1132 `§ARR2D` name mismatch; #1121, #1131, #1134, #1142, #1143, #1144 | issue list | — | 0.17.x housekeeping |
| PP-W-rows fixture redesign | W:§6 | A:90-92 supersession registered first | **0.18** |

---

## 7. Backlog disposition — the open issues on 2026-09-01

| Issue | Disposition |
|---|---|
| #1136 | **0.17 SHOULD S1** — the fix set entire; a partial fix is refused by the issue itself |
| #1128, #1137, #1118, #1127 | **0.17 MUST R4**; #1137/#1118 share one root cause and unblock PP-S1's inherited-field shape |
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
| #1094, #903, #1097, #1104 | **closed by 0.16** (W1, W3(a), W3(b), W3(c)) |

---

## 8. What "better than C#" means at the end of 0.17 — testable

C# cannot tell you that a callback passed into a helper does something the helper's signature says
it does not. Calor can — **for every shape you can write it in**, which is what §4.2 makes
testable and what today's compiler fails for five of twelve. And it can tell you over converted
real code, which is what §4.1 makes testable and what today's compiler does for 304 of 364
modules with a third of its metadata unresolved. Neither claim is about rows being clever; both
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
   confirming it: whatever kills the runner is not specific to `Calor.Performance.Tests`. Recorded
   here as an observation, not as an adjudication.

A fourth is created by this draft: **the `--permissive-effects` demotion of Calor0410** (§6) was
frozen through the 0.16.0 release commit specifically because PP-W-rows' arm A depended on it.
That proof point is closed. The freeze has lapsed and the demotion needs a decision on its own
merits.

---

## 10. Adversarial review

**None run.** Draft v1. The v0.16 roadmap took three rounds — engineering, measurement, and
plan/process lenses — and every finding was applied or declined in its own §10. This document is
not comparable to that one until the same has happened, and the numbers in §0 should be
re-verified by the first reviewer against the ledgers rather than taken from this table.
