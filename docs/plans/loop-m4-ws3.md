# M4 Kickoff — WS3 Warm Feedback

**Status: exit criterion MET (2026-07-27), pending merges.** PP-L1 measured
on the D3.3 fixture per `bench/phase0-agent-native/latency/ml1-001/VERDICT.md`
(PR #805): MCP write path (adjudicating) **P50 2 ms / P99 9 ms** vs
thresholds 300 ms / 1 s — **PASS**; watch rebuild (reported) P50 27 ms /
P99 32 ms excluding the recorded 200 ms debounce. Result published in the
CHANGELOG [Unreleased] notes. Per plan §5, PP-L1's pass permanently retires
the direct-to-IL backend proposal. M4 closes when #802 (D3.1), #803 (D3.2),
#804 (D3.3), and #805 (M-L1 run) merge.

**Kickoff record (2026-07-27):**
**Parent:** `loop-plan-v0.9.md` §2 WS3, §3 M4 · **Depends on:** D2.1 (merged,
#797) · **Prior milestone:** M3 complete (`loop-m3-ws2.md`, WS2 exit epoch
`ws2-exit-e2e-001`)

## 1. Scope

| Deliverable | Content | Status at kickoff |
|---|---|---|
| **D3.1** Warm project state | Sessions reuse `BuildStateCache` semantics in memory: reparse only dirtied files, reuse effect summaries across tool calls | Parse state is already warm (#797: stat-on-access + hash gate, changed-files-only reparse). Nothing else is: `CheckProjectReferences` rebuilds a `CallGraphAnalysis` over every neighbor's AST on every write/preview call, and no effect summary is ever computed on the session path |
| **D3.2** Latency instrumentation | Every MCP check/apply and `watch` rebuild logs edit→envelope wall time into the loop telemetry stream | No wall time is stamped anywhere. `mcp-write/1` (FileWriteTool journaling, #799) is the natural carrier on the MCP side; `watch` emits its NDJSON envelope at the end of `Rebuild` with no timing. `mcp-write/1` also has no machine JSON Schema yet (only `loop-telemetry/2` does) |
| **D3.3** Latency fixture with governance | Pinned ~10k-line multi-module generated Calor project; content criteria (contract density, effect-declaration density, cross-module reference depth) matched to current sample/test corpus percentiles and recorded in the fixture README; an owner; a regeneration policy; a stated sample count (≥200 timed edits per measurement) | Nothing exists. Closest precedent is `tests/Calor.Performance.Tests/SyntheticCodeGenerator.cs` (single-module, no contract/effect density). No corpus-percentile machinery exists |

**Exit criterion:** PP-L1 measured on D3.3 — M-L1 edit→envelope wall time,
P50/P99 over ≥200 timed edits, warm session; threshold P50 ≤ 300 ms,
P99 ≤ 1 s. Results published in the v0.9 release notes whichever way they
land.

## 2. Priced decisions (recorded at kickoff, per §3 kickoff discipline)

**"Rebind + Roslyn workspace" is descoped as written — warm state is what the
shipped loop actually computes.** The parent D3.1 text ("reparse/rebind only
dirtied files … keep the Roslyn workspace alive for in-memory emit") was
written before Call 1. Two facts have since changed under it. (a) The compile
pipeline emits C# *text* (`CSharpEmitter`); no Roslyn compilation object
exists in the driver, `watch`, or MCP path — Roslyn appears only in ancillary
tools (self-check exemplar compile, migration, evaluation metrics). There is
no workspace to "keep alive," and introducing one would be unpriced new
infrastructure with no consumer. (b) The Call 1 descope dropped D2.2/D2.3 —
the node-addressed tools that would have consumed warm bound state; the
shipped check set (parse + contracts/effects heuristics + call-graph
references) never binds. (c) "Reuse effect summaries" is already delivered
where summaries are consumed: `BuildStateCache` 2.0 persists per-module
`EffectSummary` and the driver's warm path (CLI + `watch`) reuses them for
cross-module enforcement; the session path has no effect-summary consumer,
and computing them warm would be dead state. Decision: D3.1 = warm
*derived* state on the session's parse cache — reuse of the session's
hash-verified cached parse for the original side of write-path checks
(today `CheckAndApplyAsync` re-parses the on-disk file the session already
holds parsed) and a per-file call-target index for the project-reference
walk — no binder state, no Roslyn, no session-side effect summaries.
Revisit trigger: the PP-L1 miss path itself
(plan §5: "only if the ceiling is Roslyn emit does a backend conversation
reopen") — if P99 misses and profiling shows the ceiling is downstream
`.g.cs` compilation, that conversation reopens with data.

**Warm derived state is keyed to the parse cache — no second invalidation
scheme.** The call-target index is computed lazily from a file's parse
state and stored on `SessionFileState`; it is invalidated exactly when
parse state is invalidated (the existing stat+hash gate), and cached-parse
reuse is hash-verified against the just-read on-disk content. No new invalidation semantics, no watcher (the M3 decision
stands; M-L1 will price the per-call re-stat cost, which was M3's stated
revisit trigger — the trigger is answered by measurement, not redesign).

**Warm additions must not change verdicts (check-set parity).** D3.1 is a
performance change only: `CheckProjectReferences` consuming cached call
targets must produce identical results to today's per-call walk, enforced by
a parity test over fixtures with cache warm vs cold. Cross-module *effect*
enforcement stays out of the write-path check set — it would change
verdicts, which is check-set semantics, not warm feedback (deferred to
v0.10 alongside binding-in-the-check-set, restating the M3 non-goal). M-L1
measures the check set as shipped in M3.

**PP-L1 adjudicates on the MCP write path; `watch` is instrumented and
reported.** M-L1's boundary must be stated before measurement. MCP:
tool-call receipt → envelope serialized (the whole
`CheckAndApplyAsync` body: session refresh, path gate, heal, parse, check
set, atomic apply). `watch`: rebuild start → envelope written — which
excludes the debounce window (default 200 ms), a *configured delay*, not
feedback cost; the debounce setting is recorded alongside any reported
number. Rationale for adjudicating on MCP only: it is the loop mechanism
WS2 shipped and the harness constrains arms to (`--edit-mechanism
mcp-file`), and folding a configurable 200 ms sleep into a 300 ms P50 gate
would measure a config default, not the toolchain. Both surfaces get D3.2
instrumentation; both get reported in the release notes.

**Fixture is a directory session; the `.calorproj` revisit trigger fires and
is declined.** M3 deferred the project-file question to "WS3 D3.3 or v0.10
multi-project needs." Evaluated: the fixture is a single directory tree of
generated modules (~10k lines across ~25–40 files — well under the
session's 2000-file/512 KB-per-file caps), opened as a directory session
like every other consumer. No manifest semantics are needed to measure
latency. Trigger re-arms for v0.10.

**Fixture and measurement live in `bench/phase0-agent-native/latency/`;
generation is deterministic.** Generator + corpus-stats scripts are Python
(no calor-first-guard exposure), seeded so regeneration is reproducible;
the generated fixture is *committed* (pinned — measurements run against
bytes in git, not against a generator's output du jour). The timed-edit
script is likewise generated and committed: a seeded sequence of ≥200
single-file body edits (mostly verdict-`safe`, a stated minority
constructed to reject) so P50/P99 reflect the mix the loop actually sees.
M-L1 needs no agent and no model pins — it is a toolchain metric; runs are
recorded under `latency/ml1-<seq>/`, not as epochs.

**Box confirmation:** the parent's 3 wk box was sized with rebind + Roslyn
workspace in scope. Re-boxed at **2–3 wk**.

## 3. Slicing (each PR merges green on its own)

1. **PR 1 (this PR):** kickoff record. No allowlist entries: D3.1 lands
   inside the existing session/tool files, the fixture and measurement
   scripts are Python/bash, and instrumentation edits existing files — M4
   plans no new C# product source.
2. **PR 2 — D3.1:** lazy per-file call-target index on `SessionFileState`
   (invalidated with parse state); write-path original parse reuses the
   session's hash-verified cached parse; `CheckProjectReferences` consumes
   the index; cold/warm parity tests; invalidation-coupling tests.
3. **PR 3 — D3.2:** stopwatch spanning `CheckAndApplyAsync` →
   `mcp-write/2` with `latency_ms` (+ phase breakdown: refresh, check,
   apply); machine JSON Schema for the mcp-write record (closing the gap
   noted in §1); `watch` `RebuildResult` gains elapsed ms, surfaced in the
   NDJSON envelope stream; `extract_metrics`/`validate-telemetry` and
   telemetry-doc updates.
4. **PR 4 — D3.3:** corpus-stats script (densities + reference depth over
   `samples/` + test corpus, percentiles recorded), seeded fixture
   generator, committed fixture + README (content criteria vs corpus
   percentiles, owner, regeneration policy: regenerate + re-baseline on
   minor version bumps, sample count), committed edit script.
5. **PR 5 — M-L1 run:** measurement driver (warm session, ≥200 timed edits,
   MCP path; watch pass reported alongside), P50/P99 vs PP-L1 thresholds,
   verdict record under `latency/ml1-001/`, release-notes entry drafted
   whichever way it lands.

## 4. Non-goals

- Binder/bound state in sessions — no consumer after the Call 1 descope
  (left with D2.2/D2.3).
- Roslyn workspace / in-memory dll emit — no existing home, no consumer;
  reopens only on the PP-L1 miss path with profiling evidence.
- Cross-module effect enforcement in the write-path check set — verdict
  change, not warm feedback; v0.10.
- Filesystem-watcher invalidation for sessions — stat-on-access stands;
  its cost is measured by M-L1 rather than redesigned preemptively.
- `.calorproj` / project-file format — trigger evaluated and declined (§2);
  re-arms for v0.10 multi-project needs.
- Model-driven latency measurement — M-L1 is agentless by definition
  (plan §4).
