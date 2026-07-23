# The Loop — v0.8 Execution Plan

**Status:** Draft v1 — for review (success metrics and proof points in §4–§5 are the review target)
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-23
**Parent:** [`agent-native-strategy.md`](agent-native-strategy.md) (v4.1) and [`agent-native-gates.md`](agent-native-gates.md). This plan does **not** modify any frozen gate criterion; everything it adds to measurement is observational instrument-layer metrics (§4), registered under the gates doc's supersession discipline before any comparison run.

---

## 0. Objective

> **Minimize latency × iterations × ambiguity of the agent edit–feedback cycle, and prove the reduction with paired measurements.**

The strategy doc's sub-claims 1–2 (fewer escaped bugs, bigger safe changes) are adjudicated by the frozen gates. This plan builds the *instrument* those gates run on — the loop the agent actually experiences — and separately proves that the v0.8 loop beats the v0.7 loop on loop-specific metrics. The two claims are kept apart on purpose: gate metrics tell us whether Calor beats C#; loop metrics tell us whether our tooling investment is paying off, at much lower measurement cost.

Non-goals (kill list, per strategy doc): AST-only storage, direct-to-IL (revisited only via the PP-L1 decision gate below), custom metadata tables, event-sourced runtime semantics.

---

## 1. Audit delta: the loop at v0.7.0 (updates strategy doc §1.2, audited 2026-07-23)

Progress since the v4.1 audit (which covered v0.6.7):

| §1.2 item | Status at v0.7.0 |
|---|---|
| No source mapping | **Fixed** — `CSharpEmitter` emits `#line` directives with `#line default` resets (`CodeGen/CSharpEmitter.cs:161–230`) |
| No `calor run` / `calor test` | **Fixed** — `RunCommand`, `TestCommand`, plus `WatchCommand` registered in `Program.cs:253–255` |
| Structured output fragmented / dead `DiagnosticFormatter` | **Partially fixed** — `JsonDiagnosticFormatter`/`SarifDiagnosticFormatter` now wired into `compile` (`Program.cs:127`, `CompilationDriver`), `verify` (`VerifyCommand.cs:229`), `watch` (`WatchCommand.cs:349`), `lint`, `self-check`. Remaining fragmentation is the subject of WS1. |
| Verification cliffs silent; `UNKNOWN`/timeout conflated | **Open** — WS1 |
| Diagnostics span-positional, not declaration-anchored | **Open** — WS1 |

Loop-specific gaps that remain, with file anchors:

1. **No single envelope.** `AssessCommand.cs:300–325` still carries a private SARIF path; `effects`/`benchmark` use ad-hoc JSON; **no MCP tool** (`Mcp/Tools/*.cs`) emits the shared `JsonDiagnosticFormatter` schema — each returns its own shape. An agent that learns to parse `calor compile --format json` learns nothing transferable to `calor_check` or `calor_verify`.
2. **Diagnostics don't name nodes.** Envelopes carry spans, not the enclosing declaration ID (`f001`), even though IDs are the language's addressing primitive and `Ids/IdScanner.cs` can produce the mapping cheaply.
3. **Counterexamples are not reliably in the machine path.** Z3 produces concrete models, but there is no guarantee the JSON envelope for a refuted obligation carries the model; `UNKNOWN` and timeout are conflated (`Z3Verifier.cs:~117`); `Unsupported` fallbacks remain silent (strategy §1.2 item 4).
4. **Preview exists, apply doesn't.** `EditPreviewTool` gives a safe/safe_with_warnings/breaking verdict on whole-source before/after strings. There is no node-addressed read, no transactional apply, and no project context — every MCP tool call is a stateless single-string operation.
5. **No warm state in the MCP server.** `Incremental/BuildStateCache.cs` (format 2.0, with effect summaries and output-hash validation) serves the CLI and MSBuild, but the MCP server re-does everything per call. Feedback latency under agent load is unmeasured.
6. **No loop telemetry.** The E2E harness (`tests/E2E/agent-tasks/`) and Phase 0 infrastructure (`bench/phase0-agent-native/`) record task outcomes and iteration counts, but not per-iteration feedback latency, envelope validity, or whether the agent's next edit targeted the node the diagnostic named.

---

## 2. Workstreams

Ordering rationale: WS1 defines the data format every other workstream produces and every metric consumes; WS4's baseline epoch must run **before** WS2/WS3 land, or the A/B comparison in PP-L5 is unrecoverable.

### WS1 — One envelope everywhere (size: M)

Single-source, versioned JSON envelope for every diagnostic-producing surface.

Deliverables:

- **D1.1 Envelope schema v1** (documented in `docs/cli/`, validated in CI by `self-check`): schema version; diagnostic code; span; **enclosing declaration ID** (nearest ancestor with an ID, via `IdScanner`; null if IDs absent — IDs stay optional per language policy); severity; machine-applicable fix hint where one exists; and a `verification` payload for contract diagnostics.
- **D1.2 Verification payload**: proof status as a closed enum — `proven | refuted | unknown | timeout | unsupported` — separating the currently conflated `UNKNOWN`/timeout and naming the `unsupported` cliff instead of silently keeping the runtime check. `refuted` **must** carry the concrete Z3 model (variable assignments) when the solver produced one.
- **D1.3 Adoption sweep**: `compile`, `verify`, `effects`, `assess` (delete the private SARIF model at `AssessCommand.cs:300`), `run`, `test`, `watch` all emit schema v1; every MCP tool result embeds the same envelope for its diagnostics.
- **D1.4 Schema conformance test**: one test that round-trips every command's JSON output through the schema validator, so drift is a build failure, not a rediscovery (this is the single-sourcing fix the strategy doc's revision log asks for).

Exit criteria: zero ad-hoc diagnostic JSON shapes in `src/` (grep-enforceable); envelope coverage metric (§4, M-E1) reads 100 %; refuted-with-model attach rate (M-E2) measured and reported.

### WS2 — Verified mutation loop in the MCP server (size: L)

From "preview a whole-file rewrite" to "transactionally apply a node-addressed edit."

Deliverables:

- **D2.1 Project sessions**: MCP server gains a session-scoped project context (open a directory/`.calorproj`, hold parsed ASTs + bound state), replacing per-call single-string statelessness. This is the prerequisite for both apply and warmth (WS3).
- **D2.2 `calor_get_node`**: read a declaration (source text + structure) by ID.
- **D2.3 `calor_edit_apply`**: given a node ID and replacement source for that node, run the `EditPreviewTool` check set (compile, contracts, effects, references) against the *project* context, then **apply atomically or reject with the envelope** (including counterexamples). Verdict thresholds configurable: default applies `safe` and `safe_with_warnings`, rejects `breaking`.
- **D2.4 Whole-file fallback parity**: the same transactional check-then-apply for full-file writes, so the A/B in PP-L3 compares *edit granularities* under identical checking, not "checked vs unchecked."

Exit criteria: an agent can complete a multi-edit task in the E2E harness exclusively through MCP (no raw file writes), with first-apply validity and reject precision measured (§4, M-L2/M-L4).

### WS3 — Warm feedback (size: M)

- **D3.1 Warm project state**: sessions from D2.1 reuse `BuildStateCache` semantics in memory — reparse/rebind only dirtied files, reuse effect summaries, keep the Roslyn workspace alive for in-memory emit.
- **D3.2 Latency instrumentation**: every MCP check/apply and every `watch` rebuild logs edit→envelope wall time into the loop telemetry stream (WS4).
- **D3.3 Latency fixture**: a pinned ~10 k-line multi-module Calor project (generated, checked in under `bench/`) as the standard latency workload — P50/P99 mean nothing without a pinned workload.

Exit criteria: PP-L1 measured on the fixture; results published in the v0.8 release notes whichever way they land.

### WS4 — Loop instrumentation and the baseline epoch (size: M)

- **D4.1 Loop telemetry schema**: per-iteration records (task, arm, iteration n, feedback latency, envelope schema-valid?, diagnostics returned [codes + node IDs], edit target node IDs, edit mechanism [MCP node / MCP file / raw file], apply verdict) written as JSONL next to the existing harness outputs (`runs.jsonl` convention from the Phase 2 monitoring setup).
- **D4.2 Harness integration**: `tests/E2E/agent-tasks/` and `bench/phase0-agent-native/run-pair.sh` emit D4.1 records. Gate-relevant runs are untouched — telemetry is write-only observation.
- **D4.3 Baseline epoch (v0.7 loop)**: before WS2/WS3 merge, run the E2E task set on v0.7.0-era tooling with telemetry on, pinned model, N repetitions per the gates doc's re-run rules. This is the control arm for PP-L5 and the source of provisional→final thresholds for PP-L4.
- **D4.4 Metric registration**: the §4 metric definitions and §5 thresholds are appended to the gates doc as *instrument metrics* (observational; no gate criterion changes) before the comparison epoch runs — same pre-registration discipline, same "no post-hoc exclusion" rule.

Exit criteria: baseline epoch archived under `bench/` with pins recorded; thresholds frozen.

---

## 3. Sequencing

| Milestone | Contents | Depends on |
|---|---|---|
| **M1** | WS1 complete (envelope everywhere, conformance test) | — |
| **M2** | WS4 D4.1–D4.3: telemetry + **baseline epoch on the v0.7 loop** | M1 (envelope validity is a recorded field) |
| **M3** | WS2 mutation loop | M1; D2.1 unblocks WS3 |
| **M4** | WS3 warm feedback + latency fixture | D2.1 |
| **M5** | Comparison epoch (v0.8 loop vs M2 baseline), PP adjudication, published report | M2–M4, D4.4 |

The one hard rule: **M2 before M3/M4 merge to main.** Shipping the improvements before the baseline exists destroys the A/B.

---

## 4. Success metrics (definitions)

All machine-adjudicable; loop metrics follow the gates doc's conventions (per-pair means, paired ratios, censoring at budget+1) so numbers are comparable across documents. Envelope metrics are properties of the toolchain; loop metrics are properties of agent runs.

**Envelope metrics** (measured in CI, per build):

- **M-E1 Envelope coverage** — % of diagnostic-producing CLI commands and MCP tools whose JSON output validates against schema v1. Target: 100 %, enforced by D1.4.
- **M-E2 Counterexample attach rate** — of contract diagnostics with status `refuted` on scalar obligations, % whose envelope carries a concrete model. (Denominator excludes `unknown`/`timeout`/`unsupported`, which are separately counted — see M-E3.)
- **M-E3 Cliff visibility** — % of verification obligations whose outcome is reported as one of the five explicit statuses (i.e., no silent fallback path remains). Audited by a test enumerating fallback sites in `Z3Verifier.cs`/`ContractTranslator.cs`.

**Loop metrics** (measured per harness epoch, pinned model):

- **M-L1 Feedback latency** — edit→envelope wall time, P50/P99, on the pinned latency fixture (D3.3), warm session.
- **M-L2 First-apply validity** — % of agent edit attempts that parse + bind on first application, split by edit mechanism (MCP node edit / MCP file edit / raw file write).
- **M-L3 Diagnostic actionability** — of failing iterations where the envelope named ≥1 node ID, % where the agent's next edit touched a named node. A *proxy* metric, Goodhart-prone (§7); it never gates anything alone.
- **M-L4 Reject precision** — of `calor_edit_apply` rejections, % that were true positives (applying the rejected edit anyway — replayed offline — produces a failing build/verify/held-out-test state). False rejects are loop poison: they burn agent iterations on compliant edits.
- **M-L5 Iterations-to-green / tokens-to-green** — exactly as defined in the gates doc §2 (harness-observed, silent held-out tests, censored at budget+1), reused verbatim, reported per arm.

---

## 5. Proof points (the go/no-go claims — review these)

Thresholds marked **[P]** are provisional until the M2 baseline epoch freezes them (D4.4); the others are absolute. Each proof point states its decision rule — what we do on hit *and* on miss — so the epoch adjudicates itself.

| # | Claim | Measurement | Threshold | On hit | On miss |
|---|---|---|---|---|---|
| **PP-L1** | Warm feedback is fast enough that backend latency is a non-issue | M-L1 on D3.3 fixture | P50 ≤ 300 ms, P99 ≤ 1 s | **Direct-to-IL permanently retired**; latency argument closed with data | Profile; only if the ceiling is Roslyn emit itself does a backend conversation reopen — with this data attached |
| **PP-L2** | Every failure the agent sees is machine-actionable | M-E1, M-E2, M-E3 | M-E1 = 100 %; M-E2 ≥ 90 %; M-E3 = 100 % | WS1 exits; envelope schema v1 frozen | Ship blocks: v0.8 does not release with silent cliffs or schema drift |
| **PP-L3** | Node-addressed edits beat whole-file rewrites | M-L2 + tokens per accepted edit, MCP-node arm vs MCP-file arm (identical checking, D2.4) | M-L2(node) ≥ 95 %; tokens/accepted-edit ≥ 30 % lower **[P]** | `calor_edit_apply` becomes the recommended agent path in `calor init` guidance | Keep transactional file-level apply; demote node addressing to navigation-only; saves us from building on an unproven editing model |
| **PP-L4** | Diagnostics steer the agent, not just inform it | M-L3 | ≥ 70 % **[P]** | Evidence that node-anchored envelopes work; cite in strategy doc §1.1 | Qualitative transcript review of misses; likely fix is envelope content (hints), not more metrics |
| **PP-L5** | The v0.8 loop beats the v0.7 loop | M-L5 median paired ratio, v0.8 arm vs M2 baseline, same tasks/model/budget, Calor-only | ≥ 15 % fewer median iterations-to-green **[P]**, censored fraction not worse | The loop program continues into v0.9 (verification tiers) with the same measurement discipline | The tooling bet is not paying off as built — stop, analyze transcripts, and re-plan before v0.9 spends more |
| **PP-L6** | Loop work didn't corrupt the science | All frozen gate metrics on a smoke epoch | No frozen gate metric regresses beyond the gates doc's noise rule | — | Regression is a release blocker regardless of PP-L1–L5 |

Two adversarial notes for the review, pre-empted: (1) PP-L5 compares Calor-to-Calor, so it cannot be contaminated by task-pair bias between languages — it reuses the same pairs both arms; (2) PP-L3's 95 % is deliberately near-absolute because a *checked* node edit that fails to parse means the addressing model itself is broken, not the agent.

---

## 6. Decision gates summary

- **PP-L1 hit → kill direct-to-IL forever.** This is the cheapest way to permanently close the most expensive item on the old proposal.
- **PP-L3 miss → don't build v0.9 on node-addressed editing.** The mutation API stays, but as plumbing, not as the strategic bet.
- **PP-L5 miss → freeze loop investment** until transcript analysis explains where iterations actually go. If iterations are spent on verification `unknown`s rather than bad diagnostics, the v0.9 priority flips from loop tooling to verification tiers.
- **PP-L6 is unconditional** — instrument work never gets to move the science.

## 7. Risks

1. **Goodhart on M-L3** — an agent can "touch the named node" uselessly. Mitigation: M-L3 is never a gate alone; PP-L4's miss-path is transcript review, and M-L5 (which can't be gamed without actually going green) anchors PP-L5.
2. **MCP statefulness refactor (D2.1) is the riskiest engineering** — it touches every tool. Mitigation: sessions are additive (stateless single-string paths remain for existing tools); land behind a capability flag; `SelfTestTool` extended to exercise session lifecycle.
3. **Baseline invalidation** — if WS1 envelope changes alter agent behavior, the M2 baseline must run on the *envelope-bearing* v0.7-loop build (M1 before M2 exists precisely for this; the baseline isolates WS2+WS3, not WS1). Stated here so nobody "fixes" the ordering later.
4. **Measurement cost** — two epochs (baseline + comparison) at gates-doc rigor, plus re-runs. Cheaper than one wrong architecture bet; the spend goes through the same authorization process as Phase 2 (`phase-2-spend-authorisation.md`).
5. **Audit drift at bus factor 1** — the strategy doc's revision log found factual drift three rounds running. D1.4's conformance test and this doc's §1 audit table (dated, file-anchored) are the mitigations; §1 should be re-audited at M5.

## 8. Relationship to v0.9

On PP-L5 hit, v0.9 ("The Guarantees") inherits this instrument: verification tiers (async Z3, never blocking the edit loop), capability-parameter evolution of `§E`, and contract provenance tiers are all *measured on the same loop telemetry* — which is the point of building the instrument first.
