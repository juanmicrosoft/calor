# The Loop — v0.9 Execution Plan

**Status:** Draft v2 — revised per adversarial review round 1 (verdict on v1: 55%, 2 CRITICAL / 9 MAJOR / 6 MINOR; dispositions in §10). Success metrics (§4) and proof points (§5) remain the review target.
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-23 (v1 and v2 same day)
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) (v4.1 **including the §9 postscript**) and [`agent-native-gates.md`](agent-native-gates.md). This plan adds observational instrument-layer metrics only, via an **instrument-metrics annex** to be added to the gates doc *before* its freeze (§2 D4.4) — additive-only, observational-only, no gate-criterion cross-references. Note the gates are currently **frozen-ready but parked** per strategy §9 (the 2a gate is unfalsifiable at authorable fixture scale); this plan does not pretend otherwise.
**Sibling registration:** [`machine-zone.md`](machine-zone.md) pre-registers experiment **E1/E1a** on the text-vs-structured-edit question. E1 **governs** that hypothesis (§9 of this doc); PP-L3 here is its implementation-grade successor and runs only if E1 leaves H1 alive.
**Target release:** v0.9.0 (the Loop lands here, not v0.8.0). The §1 audit is a snapshot of the current v0.7.0 codebase; the A/B control arm is the *pre-improvement* (WS1-only) **build** — an archived commit, not a released version and not an early *run* — see §3 and §7 risk 3.

---

## 0. Objective

> **Minimize latency × iterations × ambiguity of the agent edit–feedback cycle, and prove the reduction with paired, simultaneous measurements.**

The strategy doc's sub-claims 1–2 (fewer escaped bugs, bigger safe changes) are adjudicated by the (parked) gates. This plan builds the *instrument* those gates run on — the loop the agent actually experiences — and separately proves that the v0.9 loop beats the pre-improvement baseline loop on loop-specific metrics. The two claims are kept apart on purpose: gate metrics tell us whether Calor beats C#; loop metrics tell us whether our tooling investment is paying off, at much lower measurement cost.

Non-goals: AST-only storage, direct-to-IL compilation, custom ECMA-335 metadata tables, event-sourced runtime semantics. These come from an external "Agent-Native Calor Architecture" proposal reviewed 2026-07-23 (not an in-repo document; the strategy doc's own deferred list is separate — runtime-evidence loop, canonical non-file program store, full ownership/linear types). PP-L1's decision gate references that external proposal, cited as such.

---

## 1. Audit delta: the loop at v0.7.0 (updates strategy doc §1.2)

**Audit rule (new in v2, per review C1):** every file:line anchor in this section is grep-verified on the audit date against a recorded commit. This audit: **2026-07-23, commit `22be666`**. Rationale: four consecutive strategy-doc review rounds found §1 audit errors, and v1 of *this* plan repeated the failure (see §10 C1).

Progress since the v4.1 audit (which covered v0.6.7):

| §1.2 item | Status at `22be666` |
|---|---|
| No source mapping | **Fixed** — `CSharpEmitter` emits `#line` directives with `#line default` resets (`CodeGen/CSharpEmitter.cs:161–230`) |
| No `calor run` / `calor test` | **Fixed** — `RunCommand`, `TestCommand`, plus `WatchCommand` registered in `Program.cs:253–255` |
| Structured output fragmented / dead `DiagnosticFormatter` | **Substantially fixed** — `JsonDiagnosticFormatter`/`SarifDiagnosticFormatter` wired into `compile` (`Program.cs:127`, `CompilationDriver`), `verify` (`VerifyCommand.cs:229`), `watch` (`WatchCommand.cs:349`), `lint`, `self-check`; commit `b162480` (2026-07-15, "structured output plumbing + SARIF dedup — Phase 1 item 3, part 1") **also unified SARIF behind the shared formatter, including `assess`** — part of WS1's scope is already delivered. Remaining fragmentation: WS1. |
| Verification cliffs silent; `UNKNOWN`/timeout conflated | **Open** — `Z3Verifier.cs:99–117` maps solver exceptions and `UNKNOWN` (timeout or too complex) to the same `Unproven` |
| Diagnostics span-positional, not declaration-anchored | **Open** — WS1 |

Loop-specific gaps that remain:

1. **No single envelope.** `effects` and `benchmark` use ad-hoc JSON shapes; **no MCP tool** (`Mcp/Tools/*.cs` — zero formatter references in `Mcp/`) emits the shared `JsonDiagnosticFormatter` schema. An agent that learns to parse `calor compile --format json` learns nothing transferable to `calor_check` or `calor_verify`. *(v1 also claimed `AssessCommand.cs:300` carried a private SARIF model; false at the audit date — fixed by `b162480`. See §10 C1.)*
2. **Diagnostics don't name nodes.** Envelopes carry spans, not the enclosing declaration ID (`f001`), even though IDs are the language's addressing primitive and `Ids/IdScanner.cs` can produce the mapping cheaply.
3. **Counterexamples are not reliably in the machine path.** Z3 produces concrete models, but nothing guarantees the JSON envelope for a refuted obligation carries the model; `UNKNOWN`, timeout, and solver error are conflated; `Unsupported` fallbacks remain silent at scattered exception-fallback sites across `Z3Verifier.cs`/`ContractTranslator.cs` (strategy §1.2 item 4: "a blacklist by accident").
4. **Preview exists, apply doesn't.** `EditPreviewTool` gives a safe/safe_with_warnings/breaking verdict on whole-source before/after strings. No node-addressed read, no transactional apply, no project context — every MCP tool call is a stateless single-string operation (`McpServer.cs` is a ~200-line stateless dispatcher).
5. **No warm state in the MCP server.** `Incremental/BuildStateCache.cs` (format 2.0, effect summaries, output-hash validation) serves the CLI and MSBuild; the MCP server re-does everything per call.
6. **No loop telemetry.** The E2E harness (`tests/E2E/agent-tasks/`) and Phase 0 infrastructure (`bench/phase0-agent-native/`) record task outcomes and iteration counts, but not per-iteration feedback latency, envelope validity, or whether the agent's next edit targeted the node the diagnostic named.
7. **Write-path robustness absent** *(new in v2, per review M3)*: strategy §9 explicitly elevated fault-tolerant parsing + canonical-formatter auto-heal "by the epochs' evidence." No prior workstream contained it; it is now D2.5.

---

## 2. Workstreams

Ordering rationale: WS1 defines the data format every other workstream produces and every metric consumes; WS4's baseline build must be archived **before** WS2/WS3 merge, or the A/B isolation in PP-L5 is unrecoverable.

### WS1 — One envelope everywhere (size: M; part already landed via `b162480`)

- **D1.1 Envelope schema v1**, documented in `docs/cli/` with an **enumerated denominator**: the schema doc lists every diagnostic-producing CLI command and MCP tool by name (the `Commands/` directory holds 24 command files; the list is maintained there, not inferred by grep). Fields: schema version; diagnostic code; span; **enclosing declaration ID** (nearest ancestor with an ID via `IdScanner`; null when IDs absent — IDs stay optional per language policy); severity; machine-applicable fix hint where one exists; `verification` payload for contract diagnostics.
- **D1.2 Verification payload with a choke point** *(revised per review M7)*: proof status as a closed enum — `proven | refuted | unknown | timeout | unsupported` — assigned at a **single exit path**: all verification outcomes route through one status-assigning function, and the conformance test verifies the choke point has no bypasses (grep for construction of the result type outside it). This is what makes "no silent cliffs" a stable property rather than whack-a-mole enumeration of known fallback sites. `refuted` **must** carry the concrete Z3 model when the solver produced one.
- **D1.3 Adoption sweep** over the full D1.1 denominator — including `effects`, `benchmark`, `fix`, `ids`, `analyze-convertibility`, and every MCP tool — not the v1 shortlist. (SARIF unification for `assess` already landed in `b162480`.)
- **D1.4 Schema conformance test**: round-trips every enumerated command's JSON output through the schema validator in CI, so drift is a build failure. D1.4 — not grep — is the enforcement mechanism for "zero ad-hoc shapes."
- **D1.5 Verification-outcome fixture corpus** *(new per review M7)*: a committed set of `.calr` fixtures known to produce each of the five statuses (refuted-with-model, unknown, timeout, unsupported, proven), so M-E2/M-E3 have a defined CI corpus instead of "whatever the build happens to verify."

Exit criteria: M-E1 = 100 % over the enumerated denominator; M-E2/M-E3 measured on D1.5 and reported.

### WS2 — Verified mutation loop in the MCP server (size: L — sized concerns in §7 risk 2)

- **D2.1 Project sessions**: session-scoped project context in the MCP server (open a **directory; project-file format TBD and priced inside this deliverable** — no `.calorproj` exists today), holding parsed ASTs + bound state, with **dirty-state invalidation** when files change behind the session's back (the analogous "load-time reconciliation" problem was priced at phase scale in strategy 2a item 2 — it is named here so it can't be discovered mid-build).
- **D2.2 `calor_get_node`**: read a declaration (source + structure) by ID.
- **D2.3 `calor_edit_apply`**: given a node ID and replacement source, run the `EditPreviewTool` check set against the project context, then apply atomically or reject with the envelope (counterexamples included). Default applies `safe`/`safe_with_warnings`, rejects `breaking`.
- **D2.4 Whole-file fallback parity**: the same transactional check-then-apply for full-file writes, so PP-L3 compares *edit granularities* under identical checking.
- **D2.5 Write-path robustness** *(new per review M3)*: fault-tolerant parser mode + canonical-formatter auto-heal for common serialization slips (indentation, spacing) — the item strategy §9 elevated on epoch evidence. Sits in WS2 because it is the write path's other half: E1 (machine-zone) may show a cheap exemplar or auto-heal captures most of the benefit structured edits were hypothesized to deliver.
- **Scope gate from E1** *(new per review M3)*: if machine-zone E1 kills H1 (the text-serialization-tax hypothesis), D2.2/D2.3 descope to navigation + transactional file-level apply (D2.4 + D2.5) — which is PP-L3's miss path arriving early and cheaply.

Exit criteria: an agent completes a multi-edit E2E task exclusively through MCP (requires the D4.2 arm-constraint capability), with M-L2 and M-L4 measured.

### WS3 — Warm feedback (size: M)

- **D3.1 Warm project state**: sessions reuse `BuildStateCache` semantics in memory — reparse/rebind only dirtied files, reuse effect summaries, keep the Roslyn workspace alive for in-memory emit.
- **D3.2 Latency instrumentation**: every MCP check/apply and `watch` rebuild logs edit→envelope wall time into the loop telemetry stream.
- **D3.3 Latency fixture with governance** *(revised per review m4)*: a pinned ~10 k-line multi-module generated Calor project, with stated content criteria — contract density, effect-declaration density, and cross-module reference depth matched to the current sample/test corpus percentiles (recorded in the fixture's README) — an owner, a regeneration policy (regenerate + re-baseline on minor version bumps), and a stated sample count for P50/P99 (≥ 200 timed edits per measurement).

Exit criteria: PP-L1 measured on D3.3; results published in the v0.9 release notes whichever way they land.

### WS4 — Loop instrumentation, baseline, and measurement (size: M)

- **D4.1 Loop telemetry schema**: per-iteration JSONL records — task, arm, iteration n, feedback latency, envelope schema-valid?, diagnostics returned (codes + node IDs), edit target node IDs, edit mechanism (MCP node / MCP file / raw file), apply verdict, and *(new per reviews M6/m5)* the **full rejected-edit payload + content-addressed project snapshot reference** for replay, plus **raw-file-edit node attribution** (parse before/after, diff, map spans to IDs — priced here, not assumed).
- **D4.2 Harness integration**: `tests/E2E/agent-tasks/` and `bench/phase0-agent-native/run-pair.sh` emit D4.1 records, **plus an arm-constraint capability** *(new per review M4)*: the harness can restrict an arm's permitted edit mechanism (MCP-node-only / MCP-file-only / raw), required by PP-L3's arms and WS2's exit criterion. Gate-relevant runs are untouched; telemetry is write-only observation.
- **D4.3 Baseline build + threshold epoch** *(role narrowed per review C2)*: before WS2/WS3 merge, (a) **archive the WS1-only commit** — this build is the control *arm* for M5, checked out and run **at M5 time**, not now; (b) run a **threshold epoch** on it — telemetry shakedown plus the data that freezes [P] thresholds for PP-L4/PP-L5. This epoch is *not* the PP-L5 control run; comparing it longitudinally against a later epoch is exactly what the parent's measurement protocol forbids.
- **D4.4 Metric registration**: the §4 definitions and §5 thresholds enter the gates doc via a new **instrument-metrics annex** (additive-only, observational-only, own version counter), added *before* the gates doc's freeze — the gates doc's §7 supersession rule has no mechanism for post-freeze additions, so the annex must exist first *(per review M1)*.
- **D4.5 Feasibility dry-run** *(new per review M5)*: before D4.4 freezes any [P] threshold, a variance estimate (pattern: `bench/phase0-agent-native/epochs/feasibility-dry-001`) must show the threshold is decidable at the authorized spend on the stated task count and N. Iterations-to-green is small-integer and censored; with modification tasks already at 1.0× parity (machine-zone measured ledger), plausible baseline medians are 2–4 iterations, and 15 % of 3 is sub-integer — the dry-run decides whether the PP-L5 threshold, task count, or N moves. "An undetectable gate is unfalsifiability wearing statistics" (strategy §6) applies to instrument metrics too.
- **D4.6 Reject-replay harness** *(new per review M6)*: force-applies archived rejected edits (from D4.1 payloads) against their snapshots and runs build/verify/held-out tests, producing M-L4. Below a minimum of **20 rejects per epoch**, M-L4 is *reported, not adjudicated*.

Exit criteria: baseline build archived with pins; thresholds frozen via D4.5; annex registered.

---

## 3. Sequencing

Calendar boxes are estimates recorded per the parent's planning discipline ("every phase has a calendar box and budget line"); they are confirmed or corrected at each milestone kickoff, and epoch spend is authorized through the `phase-2-spend-authorisation.md` process **with numbers entered before M2 kickoff** — three epochs total (threshold, comparison, PP-L6 smoke), not two as v1 under-counted.

| Milestone | Contents | Box (est.) | Depends on |
|---|---|---|---|
| **M1** | WS1 complete (envelope, choke point, conformance test, fixture corpus) | 3 wk | — |
| **M2** | WS4 D4.1–D4.3, D4.5: telemetry, **baseline build archived**, threshold epoch, feasibility dry-run | 2 wk | M1 |
| **M3** | WS2 mutation loop (scope-gated by machine-zone E1) | 6–8 wk | M1; E1 verdict |
| **M4** | WS3 warm feedback + latency fixture | 3 wk | D2.1 |
| **M5** | **One simultaneous comparison epoch** (§5 PP-L5, PP-L3 sub-epoch), PP adjudication, published report | 2–3 wk | M2–M4, D4.4 |

**The hard rule, restated correctly** *(per review C2)*: what must precede WS2/WS3's merge is **archiving the control build** (build provenance), not running the control epoch (epoch timing). At M5, the control commit is checked out and both arms run **simultaneously, same day, same pins, same tasks** — per-task paired ratios, per the parent's rule that raw longitudinal comparisons are not evidence.

**M5 arm design** *(per review C2)*:
- **Arm A**: archived WS1-only build (from D4.3a).
- **Arm B**: Arm A's commit **+ WS2/WS3 merged** — an isolation build, so the delta is attributable to WS2+WS3 and nothing else (main is not frozen; v0.9 HEAD contains unrelated changes and is *not* the attribution arm).
- **Arm C (optional, separately labeled)**: v0.9 HEAD — supports the *product* claim "the v0.9 toolchain beats the WS1-only toolchain," which is honest but is not a WS2+WS3 attribution claim.

---

## 4. Success metrics (definitions)

**Toolchain metrics** (properties of the build, measured in CI — no agent, no model):

- **M-E1 Envelope coverage** — % of the D1.1-enumerated denominator whose JSON output validates against schema v1. Target 100 %, enforced by D1.4.
- **M-E2 Counterexample attach rate** — over the D1.5 corpus: of `refuted` scalar obligations, % whose envelope carries a concrete model.
- **M-E3 Cliff visibility** — over the D1.5 corpus: % of obligations reporting one of the five statuses, with the choke-point bypass test (D1.2) as the structural guarantee — the corpus checks behavior; the choke point makes the property stable *(per review M7)*.
- **M-L1 Feedback latency** — edit→envelope wall time, P50/P99, ≥ 200 timed edits on the D3.3 fixture, warm session. *(Reclassified from loop metrics in v1 — it needs no agent.)*

**Loop metrics** (properties of agent runs, per epoch, pinned model). Conventions follow the gates doc **with one explicit redefinition** *(per review m6)*: a "pair" here is *the same task run on two builds* (Calor-only A/B), not the gates doc's C#/Calor fixture pair; per-pair means and median-of-paired-ratios carry over unchanged; censoring at budget+1 carries over unchanged.

- **M-L2 First-apply validity** — % of agent edit attempts that parse + bind on first application, split by edit mechanism.
- **M-L3 Diagnostic actionability** — of failing iterations whose envelope named ≥ 1 node ID, % where the next edit touched a named node. Raw-file edits are attributed via D4.1's before/after node mapping, so the denominator does not silently narrow to MCP-mediated edits *(per review m5)*. Goodhart-prone; never gates alone.
- **M-L4 Reject precision** — via D4.6 replay; minimum 20 rejects per epoch to adjudicate, else reported-only.
- **M-L5 Iterations-to-green / tokens-to-green** — as defined in the gates doc §2, with the pair redefinition above. **Censored-fraction rule** *(per review m6)*: an arm's censored fraction may not exceed the other arm's by more than 5 points absolute; beyond that the epoch is reported but PP-L5 is not adjudicated on it.

---

## 5. Proof points (go/no-go claims — review these)

**[P]** thresholds are provisional until D4.5's feasibility dry-run confirms decidability and D4.4 freezes them; the dry-run may move a threshold, the task count, or N **before** freezing — never after.

| # | Claim | Measurement | Threshold | On hit | On miss |
|---|---|---|---|---|---|
| **PP-L1** | Warm feedback is fast enough that backend latency is a non-issue | M-L1 on D3.3 | P50 ≤ 300 ms, P99 ≤ 1 s | Direct-to-IL (external proposal, §0) permanently retired; latency argument closed with data | Profile; only if the ceiling is Roslyn emit itself does a backend conversation reopen |
| **PP-L2** | Every failure the agent sees is machine-actionable | M-E1, M-E2, M-E3 | M-E1 = 100 %; M-E2 ≥ 90 %; M-E3 = 100 % (choke-point-backed) | WS1 exits; schema v1 frozen | Ship blocker: v0.9 does not release with silent cliffs or schema drift |
| **PP-L3** | Node-addressed edits beat checked whole-file rewrites | Named **sub-epoch of M5**: arm B constrained MCP-node vs arm B constrained MCP-file (D4.2 arm-constraint), identical checking (D2.4) | M-L2(node) ≥ 95 %; tokens/accepted-edit lower by **[P]** (frozen from post-M3 pilot runs, pre-registered before the sub-epoch — the M2 baseline has no node mechanism and cannot source this threshold) | `calor_edit_apply` becomes the recommended agent path | Node addressing demoted to navigation-only; D2.4+D2.5 remain. **Runs only if machine-zone E1 leaves H1 alive** (§9) |
| **PP-L4** | Diagnostics steer the agent | M-L3 | ≥ 70 % **[P]** (frozen from the D4.3 threshold epoch) | Evidence node-anchored envelopes work | Transcript review of misses; likely fix is envelope content, not more metrics |
| **PP-L5** | WS2+WS3 reduce iterations | M-L5 median paired ratio, **M5 arm A vs arm B, simultaneous** | ≥ 15 % fewer median iterations-to-green **[P]** (subject to D4.5 — may be re-based on the measured baseline median), censored rule per §4 | Loop program continues into v0.10 with the same discipline | The tooling bet is not paying off as built — stop, analyze transcripts, re-plan before v0.10 spends more |
| **PP-L6** | Loop work didn't corrupt the science | *(narrowed per review M2)* (a) automated harness-config invariance check (gates doc §0.2 machinery) on a smoke epoch; (b) neutral-task iterations-to-green parity, arm A vs arm B, adjudicated by the gates doc §6.1 bootstrap | (a) zero config drift; (b) no significant regression per §6.1 | — | Release blocker regardless of PP-L1–L5. **Stated limit:** the escaped-bugs dimension is unmonitorable at authorable fixture scale (strategy §9: zero-vs-zero) until `real-scale-benchmark-design.md` lands — PP-L6 does not pretend to cover it |

---

## 6. Decision gates summary

- **PP-L1 hit → direct-to-IL retired permanently**, with the external proposal cited as the source of the idea and this data as its disposal.
- **E1 kills H1 → WS2 descopes before it is built** (D2.2/D2.3 dropped; D2.4/D2.5 proceed). PP-L3 never runs; its miss-path outcome is adopted at zero measurement cost.
- **PP-L3 miss → don't build v0.10 on node-addressed editing.**
- **PP-L5 miss → freeze loop investment** pending transcript analysis. If iterations are spent on verification `unknown`s rather than bad diagnostics, v0.10's priority flips from loop tooling to verification tiers.
- **PP-L6 is unconditional** within its stated coverage.

## 7. Risks

1. **Goodhart on M-L3** — mitigated: never gates alone; PP-L4 miss-path is transcript review; M-L5 anchors.
2. **WS2 sizing** — the MCP server is a ~200-line stateless dispatcher; D2.1 adds session lifecycle, a project model over a format that does not yet exist, dirty-state invalidation, transactional apply, and parity paths. The 6–8 wk box is the honesty mechanism: if kickoff scoping busts it, the E1 scope gate and PP-L3's miss path define what to cut (D2.2/D2.3), not schedule slip.
3. **Baseline invalidation** — the control is an **archived build run simultaneously at M5**, not an early epoch (v1 had this wrong — §10 C2). The M2 threshold epoch's results are never compared longitudinally against M5's.
4. **Measurement cost** — **three** epochs (threshold, comparison incl. PP-L3 sub-epoch, PP-L6 smoke) plus D4.5 dry-runs and D4.6 replays; authorized with numbers via `phase-2-spend-authorisation.md` before M2.
5. **Audit drift at bus factor 1** — v1 of this plan demonstrated the failure mode in its own §1 (§10 C1). Mitigations: the §1 anchor-verification rule (grep-verified, commit-stamped), D1.4 conformance in CI, re-audit at M5.
6. **Registration drift between sibling docs** — two live pre-registrations existed for the text-vs-structured question (this plan's PP-L3 and machine-zone's E1). §9 resolves governance; any future overlap goes through the same explicit-governance rule.

## 8. Relationship to the milestones that follow

Milestone map — **version ↔ parent-phase mapping made explicit** *(per review M9)*; versioning runs 0.9 → 0.10 → 0.11, not toward 1.0:

| Version | Theme | Parent-strategy phase terrain | Note |
|---|---|---|---|
| **v0.9** | **The Loop** (this doc) | Phase 1 dev-loop items + instrument work | Builds the instrument |
| **v0.10** | The Guarantees | Phase 2b terrain (verification depth) | **Deliberate deviation, argued below** |
| **v0.11** | The Wedge | Phase 2a terrain (onboarding/adoption) | Judged on loop metrics |

**The deviation, stated plainly** *(per review M9)*: the parent gates Phase 2b **behind** a passed 2a gate — depth only after wedge demand is proven (the Spec# lesson, strategy §2.1). This map schedules Guarantees-terrain before Wedge-terrain, which inverts that gating. The argument for the inversion: the 2a gate is **parked as unfalsifiable at authorable scale** (strategy §9), so "wait for a passed 2a gate" currently means "wait indefinitely," while verification-tier work is what the loop's own telemetry (PP-L5 miss-path analysis) may demand next. This is a supersession of parent-strategy sequencing and needs sign-off as such at v0.10 planning — recorded here so it is decided, not drifted into.

### Package ingestion (`calor import <package>`) — placement

A recurring idea — *import a .NET package and generate its annotations* — is **not one milestone item**. It splits along the effect/contract seam, and each half lands where its safety story lives. This productizes existing machinery (`calor effects suggest`, the IL analyzer in `Effects/IL/`, and the v4 plan in [`dotnet-ecosystem-effect-manifests.md`](dotnet-ecosystem-effect-manifests.md)) into a first-class command, rather than new design.

| Half | Milestone | Rationale | Machinery today |
|---|---|---|---|
| **Effect manifest generation** | **v0.11 (The Wedge)** — early, as a *prerequisite* of onboarding | The Wedge cannot consume a real C# solution without effect manifests for its NuGet dependencies; this *is* the "curated top-N package manifests" line | Mostly exists — IL derivation for concrete chains (prototype: definitive for BCL leaves), `effects suggest` templates; the gap is curated interface-level manifests for dynamic dispatch (`ILogger`/`IMediator`/`DbContext`, where union-all is sound but too broad) |
| **Contract synthesis (`§Q`/`§S`)** | **v0.10 (The Guarantees)** — gated behind provenance tiers | Synthesizing behavioral contracts from signatures/XML docs yields plausibly-wrong contracts; a trusted wrong contract poisons verification. Must be tagged `assumed` (assumption, never proof), never `verified` without human audit | New |

Both halves plug into this plan's instrument: `calor import` emits the WS1 envelope and stamps **provenance** on every annotation, so an agent can distinguish `derived` (sound: IL-traced effects, nullability) from `assumed` (synthesized guess) programmatically. The honest limit stays the v4 plan's limit — IL analysis resolves concrete call chains, not irreducible dynamic dispatch — so ingestion **surfaces** what it could not resolve rather than silently under-approximating.

## 9. Registration reconciliation with `machine-zone.md`

The text-vs-structured-edit question has one governing registration: **machine-zone E1/E1a** (three prompt arms — baseline, in-context exemplar, structured-edit interface — on the two 2.7× green-field pairs; `epochs/e1a-attribution/` exists). E1 adjudicates the *hypothesis* (H1: a text-serialization tax exists that structured edits would eliminate). This plan's PP-L3 is the *implementation-grade successor*: it measures the real `calor_edit_apply` only if E1 leaves H1 alive. If E1 kills H1 — e.g., the cheap exemplar alone collapses the ratio — WS2 descopes per §2's scope gate and PP-L3 never runs. One question, one governing registration, one conditional follow-up; no duplicate live registrations.

## 10. Revision log

**Draft v2 (2026-07-23)** — adversarial review round 1 (independent agent; verdict on v1: 55 %). Dispositions:

- **C1 (accepted)**: §1 falsely claimed `AssessCommand` carried a private SARIF model; fixed by `b162480` 8 days before the audit date. §1 corrected, `b162480` credited as delivered WS1 scope, anchor-verification rule added.
- **C2 (accepted)**: PP-L5 was a raw longitudinal comparison (forbidden by parent protocol) with a treatment arm (v0.9 HEAD) that did not isolate WS2+WS3. M5 redesigned as one simultaneous epoch; arm B is baseline+WS2/WS3 isolation build; v0.9 HEAD demoted to optional product-claim arm; D4.3's epoch narrowed to threshold-freezing.
- **M1 (accepted)**: "frozen gates" framing corrected (frozen-ready, parked per strategy §9); instrument-metrics annex must be added to the gates doc pre-freeze.
- **M2 (accepted)**: PP-L6 narrowed to config-invariance + §6.1-adjudicated neutral-task parity; escaped-bugs dimension explicitly out of coverage until real-scale redesign.
- **M3 (accepted)**: §9 reconciliation with machine-zone E1 added; E1 governs; WS2 scope-gated on E1; write-path robustness added as D2.5.
- **M4 (accepted)**: PP-L3 rescheduled as an M5 sub-epoch with D4.2 arm-constraint capability; threshold source corrected to post-M3 pilots (M2 baseline cannot source it).
- **M5 (accepted)**: D4.5 feasibility dry-run required before threshold freeze; detectability threat (sub-integer 15 % at 2–4 iteration medians) stated.
- **M6 (accepted)**: D4.6 reject-replay harness + snapshot capture in D4.1; 20-reject adjudication floor.
- **M7 (accepted)**: D1.2 choke-point single exit path; D1.5 fixture corpus for M-E2/M-E3.
- **M8 (partially accepted)**: calendar boxes added as kickoff-confirmed estimates; epoch count corrected to three; spend routed through the authorization process with numbers required before M2. Dollar figures are deliberately not invented in this doc.
- **M9 (accepted)**: version↔phase mapping added; the Guarantees-before-Wedge inversion of parent 2a→2b gating flagged as a deliberate deviation requiring sign-off at v0.10 planning.
- **m1 (accepted)**: kill list re-attributed to the external proposal; strategy doc's actual deferred list stated.
- **m2 (accepted)**: `.calorproj` corrected to "format TBD, priced in D2.1."
- **m3 (accepted)**: D1.3 sweep expanded to the enumerated denominator; grep dropped as enforcement in favor of D1.4.
- **m4 (accepted)**: D3.3 governance (content criteria, owner, regeneration policy, sample count).
- **m5 (accepted)**: raw-file-edit node attribution priced into D4.1.
- **m6 (accepted)**: censored-fraction rule defined; pair redefinition made explicit; M-L1 reclassified as a toolchain metric.
