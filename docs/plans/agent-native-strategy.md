# Agent-Native Strategy: Making Calor Worth Leaving C# For

**Status:** Draft v4.1 (v4 + round-4 verification patch) — **final paper revision; next artifacts are the gates doc and Phase 0 fixtures**
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-01
**Revised:** 2026-07-01 (rounds 1–4 of adversarial review — see §8 revision log)
**Reviewed against:** independent devil's-advocate critique (ChatGPT, §3); round-1 panel (DA 35%, PL 55%); round-2 panel (DA 55%, PL 72%); round-3 panel (DA 62%, PL 78%); round-4 verification panel (DA 78%, PL 81% — dispositions verified 6/6 substantive; **both ruling a fifth revision round would destroy value**); dispositions in §8
**Related:** [`../design/calor-direction.md`](../design/calor-direction.md) (including the 2026-04-22 postscript), [`path-2-drop-ids-v6-implementation.md`](./path-2-drop-ids-v6-implementation.md), [`mcp-improvements.md`](./mcp-improvements.md), [`phase-2-measurement-protocol-v2.md`](./phase-2-measurement-protocol-v2.md), [`../dependent-types-m4-backlog.md`](../dependent-types-m4-backlog.md)

---

## 0. The claim this document defends

> **Agents can make changes in Calor that humans would not trust them to make in C# — and the system shows exactly why those changes are safe, and exactly what remains unproven.**

Stated in its **narrow, honest form**: the competitor is not C#-the-language, it is **agent-generated evidence in C#** — agents saturating C# with NRT annotations, generated test suites, property-based tests, mutation testing, and analyzer configs. Agents zero annotation cost for C# too. The bet is specifically:

> **Machine-checked proofs and enforced effect discipline produce more trustworthy change, per unit of agent and human effort, than very good agent-generated tests.**

The C# arm gets its full toolkit, explicitly.

The moat decomposes into four testable sub-claims:

1. Calor lets agents make changes with **fewer escaped bugs**.
2. Calor lets agents make **bigger changes safely**.
3. Calor lets humans **trust agent-generated changes faster**.
4. Calor **reduces review burden** enough to justify per-module adoption cost.

Sub-claims 1–2 are machine-adjudicable and anchor all phase gates. Sub-claims 3–4 involve human behavior; they are tracked as **qualitative observations and design targets, not gate criteria**, and any qualitative reading requires at least one **non-maintainer** reviewer (§4 Phase 2a entry conditions).

---

## 1. Audit: where Calor stands today (2026-07-01, v0.6.7)

Fact-checked across three adversarial review rounds; where reality is worse than earlier drafts claimed, the worse version is stated. A process note the panel earned: **rounds 1–3 each found a verified factual error in this section's predecessor** (dead `DiagnosticFormatter`; silent-by-default delegate hole; split-brain enforcement default). That is evidence about audit drift at bus factor 1; the single-sourcing work (Phase 1 item 6) is the systemic fix, and claims below marked *(verified rN)* carry the round that checked them.

### 1.1 Genuinely ahead of C# — with the enforcement caveats stated

- **Effects are compile errors for first-order code — on enforcement-enabled paths.** Interprocedural, SCC-based enforcement (`src/Calor.Compiler/Effects/EffectEnforcementPass.cs`): undeclared effects fail the build with a full call chain (Calor0410) and a machine-applicable fix; BCL/ecosystem coverage via JSON manifests plus IL fallback. Three caveats, each verified:
  - **Split-brain default** *(r3)*: the CLI's `--enforce-effects` defaults **false** (`Program.cs:47–50`, pass gated at `:591`), while `CompilationOptions.EnforceEffects` defaults **true** (`Program.cs:903`) — so the SDK/MSBuild per-module path enforces by default and `calor --input` does not. The Phase 0 gates doc pins the harness surface and flags (§4); the CLI default flip is scheduled with the other strictness changes in 2a item 4.
  - **Delegate hole** *(r2)*: invocation of a delegate-typed value is **silently assumed pure by default**; an advisory exists only under opt-in `--strict-effects` (`EffectEnforcementPass.cs:636–652`). Narrowing fact: lambda *bodies* are charged to the enclosing function at definition site (`InferFromLambda`, `:919`).
  - **Dispatch hole** *(r3)*: there is **no effect-variance rule on overrides** — `ContractInheritanceChecker` covers contracts only; an override with broader effects than its base launders them through dynamic dispatch exactly as a delegate does. Late binding is late binding. Fixed in 2a item 4 (it is a cheap declaration-local subset check), because "the hole is specifically delegate-typed values" was false as previously stated.
  - Standing audit obligation *(r3)*: effect inference's expression switch ends `_ => EffectSet.Empty` (`EffectEnforcementPass.cs:~874`) — any future effectful expression form silently defaults to pure. First-order soundness depends on that switch staying exhaustive; noted here so it is a review-checklist item, not a rediscovery.
- **Contracts are proven, not just checked — for scalar code.** Z3-backed `§Q`/`§S` with concrete counterexamples, quantifiers, refinement/dependent types, k-induction invariant synthesis, Liskov contract-inheritance proving. No heap model, no frames, no aliasing discipline, no termination measures, no vacuity detection (§4 Phase 2b, §5). `Proven` preconditions **already elide runtime checks today** (`CSharpEmitter.cs:~1657`), so §5's elision rule modifies existing behavior. Contracts mentioning **generic-typed values** (including `Option<T>`/`Result<T,E>`-typed ones) fall to `Unsupported` — this constrains Phase 0 fixture authoring and is named there *(r3)*.
- **Taint sinks derive automatically from effect declarations** (`Analysis/Security/EffectSinkMapping.cs`).
- **Match exhaustiveness is an error** (Calor0500); Option/Result are first-class; semantics are versioned (`§SEMVER`).
- **Machine-readable diagnostics: aspiration, not fact** *(r2/r3)*. `Diagnostics/DiagnosticFormatter.cs` (JSON + SARIF) is referenced by nothing in production (test-only); `Commands/AssessCommand.cs:441–499` carries a second, private SARIF model; `verify`/`effects`/`benchmark` use ad-hoc JSON shapes. The MCP server (15 tool classes incl. self-healing `calor_check`) and `calor hook` enforcement are real.
- **A live-agent E2E harness exists**: `tests/E2E/agent-tasks/run-agent-tests.sh`, Claude Code CLI, 2/3 majority voting, 19 task categories (incl. `lambdas-delegates` — priced in 2a item 4).

### 1.2 Behind C# — dev-loop holes that undercut the pitch

**While these hold, the "agent-first" claim is premature.**

1. **No source mapping.** Zero `#line` directives in `CSharpEmitter.cs`; Roslyn errors, stack traces, and debugger sessions land in `.g.cs` files our own hooks forbid agents from editing.
2. **No `calor run`, no `calor test`.** (Also blocks Phase 0 — see the execution prerequisite in §4.)
3. **Structured output fragmented and partly dead code** (§1.1). No watch mode; CLI recompiles everything.
4. **Verification cliffs are silent.** Float/call/string-op contracts degrade to `Unsupported` at scattered exception-fallback sites **across `Z3Verifier.cs` and `ContractTranslator.cs`** (a blacklist by accident); `VerificationResult.cs` documents "Keep runtime check **silently**"; Z3 `UNKNOWN` and timeout conflated (`Z3Verifier.cs:~117`); index-bounds models only the negative half; no `old()`, frames, purity rule, termination, or vacuity check.
5. **Diagnostics are span-positional**, not declaration-anchored.
6. **Immediate items — before any Phase 0 measurement:**
   - `NullDereferenceChecker` precedence bug: **fixed 2026-07-01** (predicate made self-consistent with the safe-fallback list and order-independent; the bug-pattern test suites pass — `BugPatternTests` + `BugDetectionImprovementTests`). *Working-tree edit; commit pending.*
   - `CLAUDE.md` staleness: **fixed 2026-07-01** (closer-tag teaching replaced with current indent-only guidance; hardcoded stale version removed; wrong lint code removed; round-4 accuracy correction applied — 13 closer kinds hard-error, others tolerated-but-optional). *Working-tree edit; commit pending.*
   - **Obligations `FactCollector` flow-insensitivity** *(discovered r4 — pending)*: guard facts are collected flow-insensitively at function scope (`Verification/Obligations/FactCollector.cs:50–92`) and all asserted for every obligation (`ObligationSolver.cs:124–141`). Consequences: else-branch obligations discharged under then-guards; sibling `§EI` guards produce an UNSAT fact set that **vacuously discharges every obligation in the function**; facts survive assignments and loop exit. This is a third vacuity channel that neither the Phase 1 `§Q`-sat check nor 2b's consistency check covers. Contract-check *elision* is unaffected (it rides the body-blind `Z3Verifier`, not obligations), but the blast radius is refinement/index-bounds obligations — wedge-fixture terrain. **Minimum mitigation before Phase 0 fixtures:** fact-set SAT pre-check before trusting UNSAT discharge; proper fix (dominating-guards-only scoping + assignment kill) alongside. Supersedes the milder statement of this defect in [`dependent-types-m4-backlog.md`](../dependent-types-m4-backlog.md) item 1.
7. **Agent-facing docs are not single-sourced** — the mechanism that produced item 6b. Phase 1 item 6.

### 1.3 The migration answer already exists (and is under-communicated)

Per-module adoption inside existing C# repos is the current architecture: `.calr` beside `.cs` via the MSBuild SDK; C#→Calor migration with `CSharpInteropBlockNode`; `calor assess`; the round-trip harness; effect manifests at the boundary.

What coexistence does **not** dissolve: pre-1.0 instability, bus factor 1, no external users, `§`-syntax review burden, exit costs. Mitigations: (a) **"eject to C# losslessly at any time"** as a first-class tested feature (contracts/effects degrade to runtime checks and comments; the degradation is documented); (b) the adoption playbook (2a) written from the adopter's side.

**The first-adopter condition, hardened:**
- Search runs **in parallel with Phase 1**, deadline co-terminal with the Phase 1 gate. **No adopter by the deadline → the 2a kill *action* executes** (tooling-maintenance mode) — with its own conclusion wording: *demand unproven*, not "bet falsified"; adopter failure is a demand/BD result and produces no benchmark evidence either way *(r3)*.
- "Adopter" requires **at least one non-maintainer reviewer**. The maintainer's own projects are pilot substrate only.
- Candidates by evidential value: willing OSS project; internal Microsoft team; (pilot only) maintainer's own projects.

---

## 2. Thesis: why the moat is real — and what it is not

Models are trained on billions of lines of C# and ~zero Calor; Calor cannot win on familiarity, ecosystem, or tokens. Roslyn can copy the *mechanics* of this plan (indexes, symbol edits, speculative apply, blast radius). It cannot copy the *semantic content* — effects, contracts, refinements are not in C#'s language. "Which proofs does this change invalidate?" is unanswerable in a language with no proofs.

### 2.1 The history, both halves

**Failure precedent (Spec#/Code Contracts), honestly.** Contracts on .NET died of several causes: annotation burden, a fragile IL rewriter, a slow noisy checker, no language integration, team dissolution. Agents eliminate annotation-*writing* cost; Calor's language integration eliminates the rewriter/integration causes. But agents **amplify prover-appeasement cost** — an agent facing `Unproven` will weaken the contract or contort code until the prover accepts. **We expect prover incompleteness, not annotation cost, to be the binding constraint**; iterations-to-green is a gate metric to surface exactly that. Spec# also sank on **framing and invariants over an aliased heap** — steered around by design in 2b, not by hope.

**Success precedent (SPARK/Ada), both lessons.** SPARK succeeded on this plan's shape (per-unit adoption, modular verification, assumption tracking) *and* had two things Calor lacks: an **exogenous buyer** (certification credit) — hence the named-adopter condition — and **language restriction** (anti-aliasing, side-effect-free functions) rather than full-heap verification — hence the wedge targets the sound subset and 2b adopts a scoped aliasing restriction.

> **Calor makes proof and effect discipline the default primitive of the language, not an IDE feature layered on top — and initially, only over the subset of the language where that discipline is sound.**

The product is the workflow; the language is the enforcement substrate.

---

## 3. First external critique (ChatGPT): dispositions

| # | Critique | Disposition |
|---|----------|-------------|
| 1 | Center on what Calor makes *impossible* | **Accepted** (§0). |
| 2 | Agent-first claim premature | **Accepted** (§1.2). |
| 3 | "C# can't copy this" overstated | **Half accepted** (§2). |
| 4 | Migration gravity underestimated | **Accepted with evidence** (§1.3). |
| 5 | "Verified nonsense" risk | **Accepted in full** (§5). |
| 6 | Null elimination socially risky | **Accepted**; staged, rides an edition (Phase 3). |
| 7 | One wedge; prove demand first | **Accepted** (2a). |
| 8 | Evals first, frozen, adversarial | **Accepted in full** (Phase 0). |
| 9 | Too compiler-centered | **Accepted for sequencing**; runtime-evidence deferred post-1.0. |
| 10 | Moat is workflow, not language | **Accepted** (§2). |

Panel dispositions: §8.

---

## 4. The plan

Every phase has a calendar box, budget line, pre-registered gate thresholds, and a kill action (§6). Thresholds are frozen in `agent-native-gates.md` before Phase 0 measurement; §6 publishes the planning envelopes that constrain its author, the supersession rule that defines "frozen," and the **feasibility requirement**: the gates doc must include a power/detectability calculation showing each frozen threshold is decidable at the frozen API budget — otherwise the threshold or category count moves *before* freezing, never after *(r3)*.

### Immediate items — **executed 2026-07-01** (see §1.2 item 6)

### Phase 0 — Freeze the benchmark (calendar box: 4–6 weeks)

Freeze an adversarial task suite before building anything else; record the baseline even though Calor will lose today.

**Honest scoping.** Phase 0 contains real engineering:
- **Execution prerequisite:** a minimal harness-internal execution path (template `.csproj` + `Calor.Sdk` + `dotnet test`, hand-wired once). Shipped CLI surface remains Phase 1.
- **Starting asset:** extend `tests/E2E/agent-tasks/run-agent-tests.sh` (not `bench/mcp/harness/`, which skips agent tasks).
- **Harness configuration is pinned in the gates doc** *(r3)*: effects enforcement **on**, permissive mode **off**, contract mode, Z3 present — because the CLI and SDK defaults disagree today (§1.1) and an unpinned surface would measure a language with no effect enforcement at all.

**Fixture provenance — operationalized both ways.**
- Each task ships as a **behavioral specification** (description + held-out tests), not source to convert. Each arm gets an idiomatic starting codebase authored independently.
- **Held-out tests are arm-shared**: one black-box suite against the behavioral spec, two runners. Per-arm test authorship is prohibited (it would make escaped-bugs incomparable).
- **Difficulty equivalence is measured on both arms** *(r3 — v3 measured only half)*: (i) at baseline, the *C# arm's* per-pair success rate must fall within a pre-registered band of its category mean; out-of-band pairs are re-authored or dropped (max M rounds; >N% failures fires the Phase 0 kill row). (ii) **Calor-side symmetry audit:** pre-registered structural parity constraints at authoring time (spec-derived scope; comparable declaration counts and branching), plus a re-run of the equivalence check on the *Calor arm* at the first epoch where its neutral-task success is non-degenerate; out-of-band pairs are quarantined from gate evidence. A maintainer authoring both sides must not be able to (even innocently) write smaller, cleaner Calor fixtures unchecked.
- **Pre-registered categories** with selection rules (choosing among them later is permitted; inventing post-hoc ones is not): contract-preserving refactors / contract-dense algorithmic changes (the 2a wedge); **first-order effect-centric tasks** (manifest-checked BCL boundaries — Calor's most differentiated *sound* capability, so the moat is not tested only on property-based testing's home turf); effect-safe DB writes / security boundaries (gate-eligible only after 2a item 4 + 2b item 1); API changes with consumers; tasks C# should dominate; neutral tasks — at pre-registered proportions.
- **Fixture-authoring constraint** *(r3, sequencing fixed r4)*: contracts mentioning generic-typed values are `Unsupported` today (§1.1); wedge-category fixtures are authored against the **modeled-forms whitelist**, which is therefore a **Phase 0 prerequisite** — the documentation-grade enumeration is cheap and must exist before fixtures are authored against it (v4 had it in Phase 1, which put fixture authoring ahead of the artifact it depends on; the whitelist's pricing/serialization work stays Phase 1, enforcement rearchitecture stays 2b). Likewise, the **`Calor.Runtime` combinator manifest entries** (see 2a item 4) **do not exist today** and must land before first-order effect-centric fixtures are authored — without them, `opt.Map(...)`-style code hits the unknown-call path and fails enforcement.

**Baseline fairness.** The C# arm gets Roslyn analyzers, NRT, tests, and full freedom to generate its own evidence.

**Measurement protocol.** Model pinning per epoch; both arms simultaneously on the same pinned model, compared arm-vs-arm (an **epoch** = one gate's complete paired run; arm-delta difference-in-differences across epochs is permitted evidence, raw longitudinal comparisons are not). Held-out split opened only at gates. Power analysis restored and joint-feasibility-checked against the budget (§6). Gate metrics: task success, escaped bugs (arm-shared tests), iterations-to-green, tokens. **Runtime overhead of retained contract checks recorded per-arm.** Review time: qualitative only. Z3-absent runs never count toward a gate.

**Exit criterion:** suite frozen, gates doc frozen (with feasibility calc), baseline published including the losses.

### Phase 1 — Cost of admission (calendar box: 8–10 weeks, hard, with shed order)

**Gate-load-bearing (never shed):**
1. **Source maps** (`#line` in `CSharpEmitter`; stack-trace remapping).
2. **`calor run` / `calor test` CLI commands** (`calor test` wraps `dotnet test`; the `§TEST` *construct* is 2a — additive, non-breaking).
3. **Structured-output plumbing sufficient for measurement** + taxonomy enum/serialization (§5) + **vacuity checks** (`§Q`-sat, plus the obligations fact-set SAT pre-check from the immediate items if not already landed) + **whitelist pricing/serialization** (the documentation-grade whitelist itself is a Phase 0 prerequisite *(r4)*; this item productizes it; enforcement rearchitecture stays 2b). Full unification honestly priced: pick one SARIF implementation (`DiagnosticFormatter` vs `Commands/AssessCommand.cs`'s private model), delete the other, wire the survivor, schema unvalidated until a command ships on it.

**Sheddable to 2a in order:** 4. `calor watch` + CLI incrementality; 5. fault-tolerant parser mode (a parser-architecture feature) + canonical formatter as auto-heal; 6. single-sourcing *system* for the agent-facing spec (content already fixed).

**Exit criterion (†):** pinned-model simultaneous arms — `.g.cs` dead-end rate below ceiling; neutral-task iterations-to-green within the pre-registered percentage of the C# arm.

### Phase 2a — The wedge, sound subset first (calendar box: ~3 months, with shed order)

*(v3 said ~2 months; round 3 correctly called that the same estimating sin v1/v2 were mocked for, given 2a also absorbs Phase 1 shed items.)*

**Entry conditions:** Phase 1 gate passed; named adopter secured per §1.3.

Scope: **pure/contract-dense modules plus first-order effect-checked code** inside an existing C# repo. And stated plainly *(r3)*: **enforced Calor is first-order by design until Phase 3** — a function that receives and invokes a function-typed parameter is an error under enforcement; `map`/`filter`-style callback APIs are out of the enforced wedge. This is a deliberate SPARK-style restriction (SPARK lived without function pointers for decades), not an oversight; definition-site lambda charging keeps the common inline-lambda idiom (`opt.Map(x → x+1)` where the callee is manifest-covered) working. The benchmark treats it honestly: neutral-task fixtures may use higher-order style only via interop/permissive paths, and the neutral tolerance absorbs a real expressiveness penalty — it is visible in the numbers, not hidden.

```
propose change (declaration-addressed edit)
  → blast radius (callers, effect diffs, invalidated proofs, API surface)
  → re-prove affected contracts (incremental, cached)
  → run impacted tests
  → emit review packet
```

**Never-shed core:** items 1–4. **Sheddable to 2b in order:** items 5→7.

1. **Semantic index (subset):** call graph, effect summaries, contract results persisted to `obj/calor/` (SQLite); `calor query` + MCP tools.
2. **Declaration-addressed edits** with signature-qualified addresses, positional lambda scheme, rename-aware remapping **plus load-time reconciliation** by (address, hash) for tool-bypassing text edits; `--check` speculative mode.
3. **Review packet v1** over the §5 taxonomy, leading with the unproven remainder; includes **assumption-provenance tracking** in the verifier (priced as verifier work) and the packet's **interop/permissive-fraction line** (see item 4).
4. **Close the effect holes — coherently, as one strictness batch** *(r3 restructure, r4 completion)*:
   - **Delegate invocation:** unconditional **error** under enforcement. No annotation escape hatch in 2a (effect-annotated function types don't exist; that design goes to the Phase 3 TIER2D-vs-binder evaluation and the 2b design doc).
   - **Override and interface-implementation effect-variance** *(r4 completed the spec)*: override effect set ⊆ base effect set, **and** implementation effect set ⊆ interface-declared effect set (interface dispatch launders identically) — declaration-local checks, JML/Eiffel behavioral-subtyping shape. The machinery exists (`MethodNode.IsVirtual/IsOverride`, `MethodSignatureNode.Effects`, `EffectSubtyping.Encompasses`), but `ContractInheritanceChecker` is interface-only today, so the base-chain traversal is new (small) work. **Priced separately** *(r4 — not declaration-local)*: call-site resolution — calls through base/interface-statically-typed receivers must charge the static type's declared `§E` (today they fall into the unknown-call chain and fail loud under strict policy, so there is no silent hole, but without this work OO dispatch stays outside usable enforcement). **External C# base classes/interfaces** carry no `§E` and route to the same Unknown/`Assumed` channel as interop.
   - **Interop is effect-*unknown*, not effect-invisible** *(r3 — verified inversion: today interop blocks contribute `EffectSet.Empty`, i.e. the unanalyzed tier feeds *pure* summaries into the enforced tier)*: a function containing interop contributes an Unknown-effect marker that propagates as `Assumed` through the SCC pass, like manifest assumptions; the packet surfaces interop/permissive fraction per module.
   - **`KnownPureMethodNames` mutator purge** *(r4)*: the bare-name purity fallback currently includes mutators (`Add`, `Remove`, `Clear`, `Sort`, `Insert`, delegate-taking `ForEach`, …) that are correctly `mut`-manifest-entered when the receiver type resolves — so the same call is `mut` or pure depending on whether type resolution succeeds. Purge mutators from the list; route the remainder through §5's `Assumed` provenance (already listed as an `Assumed` source; this schedules the operational half).
   - **CLI enforcement default flipped to on** here, with the other strictness increases, priced together *(r3 N2)*.
   - *Migration policy:* converted delegate-dense code → interop-wrap (now honest: `Assumed`, not silently pure) or `--permissive-effects` (which §5.2 names an explicit waiver). *In-repo cost:* `Calor.Enforcement.Tests`, the E2E `lambdas-delegates` category, round-trip flows. *Gate interaction:* strictness increases land before the 2a gate epoch; both gate arms measure the same post-change compiler.
   - *Manifest convention* *(r3, tense corrected r4)*: `Calor.Runtime` combinators (`Map`, `AndThen`, `Match`, …) **will be** manifest-entered as **"pure modulo arguments, which are charged at the definition site."** **These entries do not exist today** (verified r4: `Manifests/` is BCL/ecosystem only) — they are a Phase 0 prerequisite (see Phase 0 fixture-authoring constraint), and the convention is written down now so it doesn't become a soundness dispute at the gate.
5. **Contract purity rule:** contract-referenced functions must declare `§E{}`; the reads-half (no mutable-state reads) goes to the 2b design doc — `§E{}` alone is necessary, not sufficient, and content-hash proof caching is incorrect without the reads half (also 2b).
6. **Review-packet trust sub-experiment** with the adopter's non-maintainer reviewers; if Assumed-heavy packets don't reduce review effort, the wedge stays proof-dense and the go-to-market says so.
7. **`§TEST` construct** and the **adoption playbook** (tested eject story, bus-factor disclosure, syntax guide).

**Cut from 2a:** runtime-evidence pipeline (post-1.0); API-diff engine beyond `ApiStrictnessChecker` (2b); counterexample→tests (2b).

**Exit criterion (†):** on pre-registered wedge categories incl. first-order effects: **escaped-bugs advantage at or above threshold, with iterations-to-green within the §6 prover-appeasement allowance** *(r3 — v3's exit criterion demanded advantage on both metrics while §6 permitted Calor to be worse on iterations; reconciled: escaped bugs is the advantage metric, iterations is a bounded-cost metric)*; neutral-task regression within tolerance.

### Phase 2b — Verification depth for the wedge (calendar box: ~3–4 months, entered only on a passed 2a gate)

In dependency order:

1. **Aliasing before frames before `old()`** — scoped honestly *(r3)*. The design doc opens with aliasing and confronts two facts: (i) the pairwise parameter rule ("no two mutable reference parameters alias") is **not** SPARK's full discipline — SPARK also had parameter–global anti-aliasing and no general access types; under reachability aliasing (`f(a, b)` where `b == a.Items`) the pairwise rule holds and frames still break. Therefore **2b frames are scoped to what the rule actually supports: scalars, arrays, and disjoint whole objects** — which is also what Z3's array theory models — with reachability-aliasing cases surfacing as named `Assumed` assumptions, not as proofs. (ii) **The C#-caller boundary:** exported Calor functions are callable from arbitrary C#; alias preconditions are compiler-checked at Calor call sites, enforced by **runtime `ReferenceEquals` entry checks** for the shallow pairwise case at exported boundaries, and `Assumed` for reachability. Ownership remains a 2b-blocking design constraint in exactly this restricted form; a full ownership system stays post-1.0. Then: extend `§E` to heap/parameter write-sets (may-upper-bounds under union-join; over-approximation havocs more, never less).
2. **`old()`** re-priced as a heap-model project (field-map encoding in `ContractTranslator`), with specified runtime semantics (snapshot-at-entry, field-value capture, no deep copies).
3. **Calls-in-contracts** with the full guard set: purity incl. reads; a **termination measure** (`§DEC` — additive construct; **note: §DEC on functions is not a termination story by itself** — contract-callable functions containing loops need loop variants, and k-induction synthesizes invariants, not termination; the SCC machinery from the effects pass is reusable for mutual recursion *(r3)*); assumption-set consistency sat-check.
4. **Counterexamples → tests**; **API-diff engine** over the semantic index.
5. **Closed-whitelist `Unsupported`:** rearchitect the exception-fallback sites **across `Z3Verifier` and `ContractTranslator`** into the positive whitelist (the documentation-grade list ships in Phase 1; this is the enforcement rearchitecture).
6. **Exceptional postconditions — semantics stated now:** *`§S` holds only on normal return; exceptional paths are unverified and surface as an assumption.* JML `signals`-style specs go on the absent-list.
7. **Known-limitation debt** from [`dependent-types-m4-backlog.md`](../dependent-types-m4-backlog.md).

**Deliberately absent until evidence demands** (arrival is a plan change, not a surprise): ghost state/lemmas; object-invariant methodology (the hard Spec# problem); quantifier triggers (moves up if 2b proofs time out on quantified specs); JML-style exceptional specs; **async/concurrency** (effects across `await`, delegate hole × continuations, `old()` across suspension points); loop-variant syntax beyond §DEC if 2b's contract-callable functions turn out loop-light.

**Exit criterion:** 2a gate metrics on 2b-exercising categories, same thresholds.

### Phase 3 — Expand only what the benchmark justifies

- **TIER2D (effect rows) vs .NET binder work** — the comparative evaluation the [`calor-direction.md`](../design/calor-direction.md) postscript demanded, with two new inputs: how often 2a's delegate/override errors bite in benchmark runs, and the **effect-annotated function-type design** as an explicit sub-question.
- **Floats via Z3 FP theory**; narrow-type promotion fix.
- **Real array bounds** (`BoundArrayAccessExpression`, both bounds, refinement-connected).
- **Syntax editions before any further breaking change** (Rust-editions precedent; per-module). **The null ban rides an edition**, and its real design cost is named *(r3)*: a type-system default change needs an interop state at the edition boundary — C# NRT's "oblivious" and Kotlin's platform types are the precedents, and NRT is sitting in the target language.
- **Staged null elimination:** visible → preferred (auto-fix) → banned at an edition boundary, with evidence.
- **Declaration-anchored diagnostics**; persistent dispositions per §5's (address, hash) scheme.
- **Provenance/intent metadata** (`§DOC`); broader `calor query`; `calor refactor change-signature` with checked rewrite + re-verification.

### Deferred (post-1.0)

Runtime-evidence loop; canonical non-file program store; full ownership/linear types (2b's restriction is the wedge-scoped subset).

---

## 5. Verification honesty (hard requirement, all phases)

Agents overfit to the prover; a human who reads "Proven" and assumes safety when the proof was narrow is worse off than with no prover.

### 5.1 Proof status (one axis) and disposition (an orthogonal axis)

**Status** (produced by the verifier, on every surface):

| Status | Meaning | Notes |
|---|---|---|
| `Proven` | Holds unconditionally under the modeled semantics | Only status permitted to elide runtime checks. Carries a **`vacuous` flag** when the precondition set is unsatisfiable — and a vacuous `§Q` additionally raises a **compiler diagnostic** (the function is uncallable: its retained runtime check throws on every call), not just packet metadata *(r3)*. Known-uncovered: implication-antecedent vacuity inside `§S` (named, deferred) |
| `Disproven` | Concrete counterexample | Always included. No disposition may attach |
| `Assumed` | Proof conditional on assumptions (callee summaries, effect manifests, `KnownPureMethodNames`, aliasing assumptions, **interop-unknown markers**) | Transitive; listed per-proof; never aggregates into `Proven`; never elides checks |
| `Unknown` | Solver returned unknown, **with a reason field** (quantifier instantiation, nonlinear arithmetic, **solver error/crash**) *(r3 — solver exceptions previously had no home)* | Remediation: rewrite the spec (or report the crash) |
| `Timeout` | Resource limit hit | **Derived from harness-side wall-clock/rlimit bookkeeping, not Z3 `ReasonUnknown` strings** |
| `Unsupported` | Outside the **closed, enumerated whitelist** of modeled forms | Loud, never silent. Whitelist: documentation-grade list in Phase 1; enforcement rearchitecture in 2b |
| `Unavailable` | Z3 not present | Never counts toward a gate |

**Disposition** (recorded by a human, orthogonal): **`Justified(who, why, when)`**, valid on `Assumed`/`Unknown`/`Timeout`/`Unsupported`, structurally impossible on `Disproven`. **Keying, now defined** *(r3 — this definition is load-bearing)*:
- Key = (signature-qualified declaration address, `H`) where `H` = hash of the **normalized contract-expression AST** (α-renamed, canonical operand order — surface-text hashing would churn on whitespace; unnormalized ASTs would churn on refactors) **+ the content-addressed assumption set**: each assumption is hashed by *content* (the callee's contract/effect signature, the manifest entry's body — not its name), canonically ordered. Content addressing is what makes editing callee `Foo`'s contract invalidate justifications that assumed it — name-addressed sets would resurrect the stale-justification bug one level down.
- **No compiler-version salt**: semantic changes in how assumptions are *computed* surface as content changes in the assumption set itself; a compiler-version salt would invalidate every justification on every upgrade and produce SPARK-style justification fatigue (humans re-justifying by reflex), which defeats the mechanism. Two pins *(r4)*: (i) **Justified-eligible assumption sets contain declaration-sourced content only** (callee contracts, `§E` declarations, manifest entries) — *synthesized* artifacts (k-induction invariants, derived aliasing facts) are prover output that churns across upgrades with no source change, which would silently break the no-salt promise; residuals depending on synthesized assumptions are re-justified per epoch, disclosed as such. (ii) The principled middle ground if semantics shifts need capturing is a **`§SEMVER` semantics-version salt** (invalidates exactly when modeled semantics change, never otherwise) — recorded as the designated option for the 2a design, not decided here.
- Invalidated on hash change; load-time reconciliation by (address, hash); re-listed (not re-litigated) in every packet touching the declaration; re-audited at release boundaries.

### 5.2 Rules

1. **Elision:** only unconditional, non-vacuous `Proven` elides runtime checks. This changes current behavior (`CSharpEmitter` already elides on `Proven`); the `vacuous` flag and assumption-provenance must be **plumbed to the emitter's elision branch** — named owner: 2a item 3 *(r3)*.
2. **Explicit waivers, both of them** *(r3 — v3 caught contracts, missed effects)*: `--contract-mode off` strips all checks and **voids this section's contract guarantee**; `--permissive-effects` assumes unknown calls pure and **voids the effect guarantee**. Any review packet for a module compiled with either says so on its first line, and reports the interop/permissive fraction. `release` mode retains checks for all non-`Proven` statuses.
3. **The packet leads with the unproven remainder** (everything not `Proven`; `Justified` items listed with dispositions).
4. **Prover-model divergences from C# runtime semantics** are defects until fixed.
5. **Anti-gaming:** escaped-bug measurement uses arm-shared held-out tests.
6. **Pricing:** enum/serialization/vacuity/whitelist-doc — Phase 1 item 3. Assumption-provenance + emitter plumbing — 2a item 3. Whitelist enforcement rearchitecture — 2b item 5. `Justified` persistence/reconciliation — 2a items 2–3.

---

## 6. Budget, gates, and kill criteria

Staffing: one maintainer plus agents; API spend dominates measurement cost.

**Planning envelopes (constrain the gates doc; exact values † frozen there):**
- Phase 1: `.g.cs` dead-end ceiling — envelope 0–10% of runs; neutral iterations-to-green within 15–40% of the C# arm.
- Phase 2a: **escaped-bugs relative reduction ≥ 20–40% (the advantage metric)**; **iterations-to-green within 10–25% of the C# arm (the prover-appeasement allowance — a bounded cost, not an advantage requirement)** *(r3 reconciliation)*; neutral regression tolerance 5–15%.
- Phase 0 fairness: equivalence band, M re-authoring rounds, N% pair-failure kill threshold, and the Calor-side symmetry constraints — set before any pair is authored.
- **Joint feasibility:** the gates doc must show each frozen threshold is statistically decidable at the frozen API budget (power calculation); if not, threshold or category count moves before freezing. An undetectable gate is unfalsifiability wearing statistics *(r3)*.

**Gates-doc supersession rule:** supersession only for a **documented empirical defect in the measurement protocol itself** (the `phase-2-measurement-protocol` v1→v2 standard), never threshold changes after seeing arm results; requires a written defect analysis in the successor. Residual honestly acknowledged *(r3)*: at bus factor 1, "empirical defect" is self-judged — the written-analysis requirement and published envelopes are the constraint, and this residual is of the same kind as bus factor 1 itself: disclosed, not solvable by a document.

| Phase | Calendar box | Primary spend | Gate | Kill action if gate fails |
|---|---|---|---|---|
| 0 | 4–6 weeks | Harness eng.; API ceiling † | Suite + gates doc frozen (incl. feasibility calc); baseline published; ≤N% pairs out-of-band | >N% pairs fail equivalence after M rounds: publish the finding and stop — an unbuildable fair suite is itself a result |
| 1 | 8–10 weeks, hard, shed order stated | Compiler eng. | Envelope thresholds, pinned-model simultaneous arms | Ship as tooling maintenance; do not enter 2a; re-evaluate |
| — | (parallel with 1) | Adopter search | Named adopter with non-maintainer reviewer by the Phase 1 gate | No adopter → 2a kill *action* (maintenance mode), conclusion **"demand unproven"** — not benchmark falsification *(r3)* |
| 2a | ~3 months, shed order stated | Compiler + index eng.; adopter time | Escaped-bugs advantage ≥ †; iterations within allowance †; neutral within tolerance † | **Named decision point:** maintenance mode; publish the negative result, scoped: *the bet is falsified for the tested wedge — pure/contract-dense + first-order effect-checked code — at current model capability*; nothing broader |
| 2b | 3–4 months | Verification eng. (largest single bet) | 2a metrics on 2b-exercising categories | Freeze depth at 2a level; reassess at next model generation |
| 3 | Per-item | Per-item | Per-item, evidence-gated | Item-level: don't build |

Total calendar bet through 2b: **~10–13 months** (2a widened in v4).

**Parity-kill clause:** an *epoch* is one gate's complete paired run. If across two consecutive epochs the arm delta (difference-in-differences, per metric) is flat or converging on the wedge categories, execute the current phase's kill action rather than extending the box.

---

## 7. Risks

| Risk | Mitigation |
|------|------------|
| C#+agent-generated-evidence closes the gap | Narrow bet tested head-on; full C#-arm toolkit; parity-kill clause. |
| 2b verification underpriced | Aliasing→frames→`old()` order; frames scoped to scalars/arrays/disjoint objects; design doc opens with aliasing + the C#-boundary problem; absent-list explicit. |
| Verifier gaming / false confidence | §5: elision rule with emitter plumbing owned, vacuity flag + uncallable-function diagnostic, transitive content-addressed assumptions, closed whitelist, arm-shared tests, both waivers surfaced. |
| Effect-soundness regressions | All three holes (delegate, dispatch, interop-invisibility) closed together in 2a item 4; `InferFromExpression` catch-all named as a standing audit obligation. |
| Prover appeasement dominates | Iterations-to-green is a bounded-cost gate metric with an explicit allowance; surfaced, not hidden. |
| Runtime cost of retained checks | Measured per-arm from Phase 0. |
| First-order restriction penalizes expressiveness | Owned in 2a scope statement; visible in neutral-task numbers; escape hatch design deferred to Phase 3 evaluation, not improvised. |
| Wedge tested on weak terrain | First-order effect-centric category is gate-bearing; kill conclusions scoped to the tested wedge. |
| Benchmark self-deception | Frozen suite; arm-shared tests; two-sided difficulty equivalence with kill threshold; held-out split; pinned simultaneous arms; supersession rule; joint feasibility; losses published. |
| Adopter stall / self-satisfaction | Deadline routing to kill action with "demand unproven" wording; non-maintainer reviewer required. |
| Humans find Calor unpleasant | Trust sub-experiment; tested eject story; null staging on editions; syntax guide. |
| Bus factor 1 | Disclosed; single-sourced spec + gates doc reduce key-person knowledge; self-judged supersession residual acknowledged; not otherwise solvable by a document. |
| Model drift | Arm-vs-arm on pinned models; defined arm-delta diffs only. |
| Audit drift (3 rounds, 3 §1 errors) | Verified-round tags in §1; single-sourcing (Phase 1 item 6); the whitelist and gates doc convert prose claims into checkable artifacts. |

---

## 8. Revision log and panel dispositions

### v1 → v2 (round 1: DA 35%, PL 55%) — summary
Phase 0 re-scoped (execution prerequisite, correct starting asset); review-time demoted to qualitative; 2a/2b split with budget/kill table; TIER2D demoted per the direction-doc postscript; fixture-provenance rules; frames-before-`old()` with `§E` unification; purity/termination/consistency guards; taxonomy with `Assumed`/`Justified`/elision; editions-vs-`§SEMVER`; signature-qualified addressing. Full table in git history (v2).

### v2 → v3 (round 2: DA 55%, PL 72%) — summary
Delegate escape hatch removed (unconditional error + migration policy); adopter condition hardened; first-order effect category added; status/disposition orthogonality with hash-keyed `Justified`; aliasing promoted to 2b-blocking; vacuity detection; `ContractMode` precedence; planning envelopes + supersession rule; factual corrections (dead `DiagnosticFormatter`, silent-by-default delegate hole). Full table in git history (v3).

### v3 → v4 (round 3: DA 62%, PL 78% — both declaring diminishing returns)

| Finding (abridged) | Source | v4 disposition |
|---|---|---|
| §4 2a exit criterion contradicted §6's appeasement allowance (advantage on iterations vs up-to-25%-worse) | DA-N1 | Reconciled: escaped bugs = advantage metric; iterations = bounded-cost metric (§4, §6) |
| "`--enforce-effects` (the default)" false; split-brain CLI-false/SDK-true; Phase 0 surface unpinned; default flip unscheduled | DA-N2, PL-7 | §1.1 corrected with the split-brain stated; Phase 0 pins harness flags; CLI default flip scheduled inside 2a item 4 |
| 2a box (~2 months) not credible; no shed order; pressure-relief valve with no relief valve | DA-N3 | Widened to ~3 months; never-shed core (items 1–4) + shed-to-2b order; total bet restated 10–13 months |
| Difficulty equivalence measured only on the C# arm; maintainer could author asymmetric fixtures unseen | DA-N4 | Two-sided: structural parity constraints at authoring + Calor-arm equivalence re-run at first non-degenerate epoch, quarantine rule |
| Unconditional delegate error outlaws higher-order enforced Calor; cost framed as test chore | DA-N5 | Owned explicitly: "enforced Calor is first-order by design until Phase 3," SPARK precedent, benchmark treatment stated |
| Adopter-failure kill wording over-concludes (demand failure ≠ bet falsification) | DA-N6 | Separate conclusion wording ("demand unproven") in §1.3 and §6 |
| Envelopes published but detectability unconstrained — a frozen gate could be statistically undecidable | DA-N7 | Joint-feasibility requirement added to §6 |
| "Content already fixed" was false; immediate items unexecuted | DA-N8 | **Executed 2026-07-01**: null-checker fix (78 tests pass) + CLAUDE.md fix; §1.2 item 6 updated truthfully |
| `Unsupported` sites attributed to two different files in two sections | DA-N9 | "Across `Z3Verifier` and `ContractTranslator`" both places |
| Vacuous-flag emitter plumbing unowned; supersession self-judged; `InferFromExpression` catch-all a standing risk | DA-N10 | Owner named (2a item 3); residual acknowledged (§6); audit obligation recorded (§1.1) |
| `Justified` hash undefined (surface text? name-addressed assumptions? version salt?) | PL-1 | §5.1 keying defined: normalized-AST + content-addressed canonically-ordered assumption set; no version salt; rationale recorded |
| Pairwise anti-aliasing ≠ SPARK's discipline; reachability aliasing breaks frames; C#-caller boundary unaddressed | PL-2 | 2b item 1 rescoped: frames limited to scalars/arrays/disjoint objects; reachability → `Assumed`; `ReferenceEquals` entry checks at exported boundaries |
| Interop blocks are effect-*invisible* (contribute `EffectSet.Empty` into enforced summaries) — the cure relocated the hole | PL-3 | Interop → Unknown-effect marker propagating as `Assumed`; packet interop-fraction line; `--permissive-effects` named an explicit waiver (§5.2) |
| Sound subset defined by an accidental blacklist until 2b — the gate bets on soundness the plan defines afterwards | PL-4 | Documentation-grade whitelist moved to Phase 1 item 3; fixtures authored against it; rearchitecture stays 2b |
| Solver errors have no taxonomy home; vacuous `§Q` deserves a compiler diagnostic; antecedent vacuity uncovered | PL-5 | `Unknown` widened with reason field; uncallable-function diagnostic added; antecedent vacuity named known-uncovered |
| Virtual dispatch is the delegate hole in OO clothing; no effect-variance on overrides; silence forbidden by the doc's own method | PL-6 | §1.1 corrected; override-subset rule ships in 2a item 4 |
| §DEC alone isn't termination (loop variants); editions' real cost is the null-ban boundary semantics | PL-8 | Both noted in 2b item 3 / Phase 3 |
| Generics×contracts unmentioned but constrains Phase 0; runtime combinator manifest semantics unstated | PL-9 | Phase 0 fixture-authoring constraint added; "pure modulo arguments" convention stated in 2a item 4 |

**Rejected/limited from round 3:** nothing rejected.

### v4 → v4.1 (round 4, verification: DA 78%, PL 81% — dispositions verified 6/6 substantive; both fixes verified real against source; **both ruling a fifth round would destroy value**)

| Finding (abridged) | Source | v4.1 disposition |
|---|---|---|
| Whitelist sequencing inverted: fixtures authored in Phase 0 against a Phase 1 artifact | DA-MAJOR-1 | Documentation-grade whitelist made a Phase 0 prerequisite; pricing/serialization stays Phase 1; rearchitecture stays 2b |
| CLAUDE.md closer sentence overclaimed ("only §/C and §/LAM remain"; 13 kinds hard-error, others tolerated) | DA-MINOR-1 | CLAUDE.md corrected to guidance form ("never write structural closers") with accurate mechanics |
| "78 tests" not reproducible; fixes uncommitted | DA-MINOR-2/3 | Wording corrected (named suites); commit-pending status stated honestly |
| **Obligations `FactCollector` flow-insensitive** — sibling-guard UNSAT vacuously discharges every obligation in a function; a third vacuity channel no planned check covered | PL-CRITICAL(repo) | Added to immediate items with the SAT-pre-check mitigation required before Phase 0 fixtures; supersedes the milder backlog statement |
| Override rule half-specified: interface impls unstated; call-site static-type resolution not declaration-local and unpriced; external C# bases unrouted | PL-MAJOR | 2a item 4 completed: impl ⊆ interface rule; call-site resolution priced separately; external bases → Unknown/`Assumed` |
| `Calor.Runtime` combinator manifests don't exist; doc's present tense was drift (4 rounds, 4 drift findings) | PL-MINOR | Tense corrected; entries made a Phase 0 prerequisite |
| `KnownPureMethodNames` contains mutators (incl. delegate-taking `ForEach`); resolution-dependent purity | PL-MINOR | Mutator purge scheduled in 2a item 4's strictness batch |
| Justified content-addressing ill-defined for synthesized assumptions; `§SEMVER` salt is the principled option | PL-MINOR | §5.1 pinned: declaration-sourced content only; `§SEMVER`-salt recorded as the designated design option |

**Loop termination.** Four rounds: DA 35→55→62→78; PL 55→72→78→81. Round 4 was a verification round: all checked dispositions substantive, both executed fixes verified against source, the central closing claim ("remaining gap is empirical, not architectural") upheld by both panelists with one earned asterisk — a four-round streak of audit-drift findings, whose systemic fix (single-sourcing, whitelist and gates artifacts) this plan already schedules. Both panelists ruled a fifth revision round value-destroying. **The document stands at v4.1. The remaining confidence gap is closable only by data: build `agent-native-gates.md` (one adversarial review before freezing), fix the FactCollector defect, land the combinator manifests and whitelist, author the Phase 0 fixtures, and publish the baseline — including the losses.**
