# Plan: MCP Tool Improvements for Agent Productivity (v3)

**Version:** 3 (revised after four critiques)
**Date:** 2026-04-21

## Problem

The compile-fix-recompile loop is the biggest token sink for coding agents writing Calor. The compiler already computes most of what the agent guesses at — IDs, effects, closing-tag forms, manifest mappings, scope — but today the agent discovers these by failing. Every round-trip is the agent re-reading the module, guessing, and paying a full compile.

**Critical framing:** Agents are not trained on Calor. They have C#/TS/Python priors but `§E{db:w}` is opaque glyphs. Every tool response must assume zero Calor priors. This changes what we build: vocabulary catalogs and a primer are prerequisites, not nice-to-haves.

**Goal:** Close the gap. The compiler tells the agent what it needs before the agent asks, and every failure response is mechanically applicable without re-reasoning.

---

## Design Principles (from critiques)

1. **Don't build new tools when an option on an existing tool works.** 15 tools → 30 tools increases agent decision cost. Prefer `calor_compile` with `autoFix: true` over a separate `calor_autofix` tool.
2. **Text blocks for source, JSON for metadata.** MCP supports multiple content blocks. Don't JSON-escape Calor source with `\n`. Return source as a text block with real newlines.
3. **Enrichments are opt-in.** A 3KB effect summary the agent ignores 80% of the time wastes more tokens than it saves. Use request flags (`includeEffectSummary: true`).
4. **Every fix is a concrete edit, not a description.** `SuggestedFix` with `TextEdit` objects already exists in the compiler. Surface it.
5. **Uniform tool-result envelope.** Three states: `success` (ok), `partial` (some fixes applied, some diagnostics remain), `failure` (tool-level error). Failures: `{"ok": false, "toolError": {"code": "...", "message": "...", "suggestion": "..."}}`. Partial: `{"ok": "partial", "fixesApplied": [...], "remainingDiagnostics": [...]}`. Implement as concrete work item: audit and update all 15 existing tools.
6. **Disk writes are opt-in.** Default: return fixed source in response. Only write when `write: true` is passed. Tools with `write: true` update their MCP metadata to reflect the write side-effect (agents/clients may rely on read-only annotations for caching/retry).
7. **Fix confidence is server-enforced.** When `autoFix: true`, the server applies `high` fixes automatically and returns `medium`/`low` as suggestions. Agents never implement confidence policy — the server decides. Confidence is derived from diagnostic code range (see Phase 1.1), not a field on `SuggestedFix`.
8. **New options always default to prior behavior.** Unknown options are ignored. Removing options requires a breaking-change bump.
9. **Tool descriptions include workflow hints.** Every tool's MCP description ends with "Typically used after X" / "Typically followed by Y."

---

## Existing Infrastructure (from critique — don't rebuild)

| Component | Status | Location |
|-----------|--------|----------|
| `DiagnosticWithFix` class | EXISTS | `Diagnostic.cs:513` |
| `SuggestedFix` with `List<TextEdit>` | EXISTS | `Diagnostic.cs:545` |
| `DiagnosticBag.ReportWithFix()` | EXISTS | `DiagnosticBag.cs:109` |
| `ReportMismatchedIdWithFix()` | EXISTS | `DiagnosticBag.cs:133` |
| `ReportExpectedClosingTagWithFix()` | EXISTS | `DiagnosticBag.cs:236` |
| `ReportDuplicateDefinitionWithFix()` | EXISTS | `DiagnosticBag.cs:277` |
| `CheckTool` surfaces fixes | EXISTS | `CheckTool.cs:152-198` |
| `IdGenerator` with ULID prefixes | EXISTS | `Ids/IdGenerator.cs` |
| MCP resources (3 registered) | EXISTS | `McpMessageHandler.cs:76-96` |
| `CompileTool` verify/analyze flags | EXISTS | `CompileTool.cs` |
| Auto-fix in `ConvertTool` | EXISTS | `ConvertTool.cs:419+` |
| Auto-fix in `MigrateTool` | EXISTS | `MigrateTool.cs:48+` |

---

## ID System: ULID-Based (NOT Sequential)

Production IDs are ULID-based: `f_01JWDG3K...`, `m_01JWDG3K...`. Sequential IDs like `f001` are test-only and flagged by Calor0804. The plan's ID tools generate ULIDs with correct prefixes — not sequential numbers.

Prefixes (from `IdGenerator.cs`): `f_` (function), `m_` (module), `c_` (class), `i_` (interface), `p_` (property), `mt_` (method), `ctor_` (constructor), `e_` (enum), `op_` (operator overload). That's 9 prefixes — no others exist in the codebase.

`calor_generate_ids` is **stateless** — ULIDs are globally unique by construction (time-based + random). No project-wide scan needed for collision avoidance.

---

## Phase 0: Measurement Harness + Agent Primer

**Must ship before any feature.** Without measurement, we can't distinguish features that save 500 tokens/task from ones that cost 200 tokens/response while saving nothing.

### 0.1 Benchmark corpus (`bench/mcp/`)

20-30 frozen tasks across common agent workflows:

| Category | Example tasks |
|----------|--------------|
| Parser fix | Files with wrong closing tags, missing IDs, mismatched prefixes |
| Effect mismatch | Functions with undeclared effects, missing manifests |
| ID errors | Duplicate IDs, wrong prefixes, test IDs in production |
| Convert→compile | C# files needing conversion then compilation |
| Green-field | Write a module from a spec |
| Fix-up | Take broken Calor, fix it to compile |

### 0.2 Metrics (per task, per feature)

| Metric | What it measures |
|--------|-----------------|
| Round-trips to green | MCP calls before successful compilation |
| Total tokens consumed | Input + output across all calls |
| Task success rate | % reaching correct compilation |
| Response size | Average bytes per MCP response |

### 0.3 Operational specification

**Task format:** Each task is a directory containing: input `.calr` file (possibly with injected errors), task spec (what the agent should achieve), and expected green output. Error-injection tasks have a `before.calr` (broken) and `after.calr` (correct).

**Agent harness:** Automated script that replays agent↔MCP interactions with feature flags. Pinned model for reporting (specify which). Re-run on at least one smaller model to ensure improvements aren't artifacts of a strong driver.

**Metric collection:** Instrument MCP server to log per-request: request/response size in bytes, elapsed time, diagnostic count by code, fixes offered vs applied, autoFix passes. Automated, not manual.

**Reproducibility:** Minimum 5 runs per task, report median + IQR. Establish baseline variance before committing to thresholds — if 30% is within the noise floor, the threshold is meaningless.

**Corpus must include non-targeted categories:** At least 2 tasks that Phases 1-2 don't directly target (e.g., "read and summarize existing module," "propose cross-file refactor") to catch regressions in unoptimized paths.

### 0.4 Per-feature hypotheses and go/no-go

Before building: "Feature X should reduce round-trips by ≥Y% on category Z." Thresholds derived from baseline measurement (e.g., "30% of baseline median"), not fixed numbers. After building: A/B comparison with and without feature flag. No success-rate regression allowed. ≤1KB response growth unless opt-in.

### 0.5 Phase 0 exit criteria

Phase 0 is done when: (a) 20+ tasks exist across all categories including non-targeted, (b) baseline metrics collected with ≥5 runs, (c) variance established, (d) CI runs the harness automatically, (e) two consecutive baseline runs produce comparable results (within measured variance).

### 0.6 Agent primer resource: `calor://primer`

Read-once at session start. 10-20 canonical examples covering:
- Module shape with ULID IDs
- Function with effects and contracts
- Class with methods
- Closing-tag rules (§/F not §/IF)
- ID prefix table
- Effect code table
- Section tag grammar

Realistic size: ~6-10KB (a single function example with effects + contracts is ~400 chars; 10-20 examples need space). Untrained agents pattern-match from examples — this is their Rosetta Stone.

### 0.7 Vocabulary catalogs (MCP resources)

| Resource | Content |
|----------|---------|
| `calor://types` | Valid Calor built-in types (`i32`, `str`, `bool`, `Option<T>`, `Result<T,E>`, etc.) |
| `calor://tags` | Section tag grammar — `§F`, `§M`, `§IF`, `§C`, `§E`, etc. with opening/closing forms |
| `calor://id-prefixes` | Which prefix is for which construct |
| `calor://effects` | All valid effect codes with descriptions and examples |

Tiny, read-once per session. Prevents the agent from inventing non-existent types, tags, or effects.

---

## Phase 1: Make Every Failure Instantly Fixable

### 1.1 Surface existing fix patches in `calor_compile` response

The infrastructure exists (`DiagnosticWithFix`, `SuggestedFix`, `TextEdit`). `CompileTool` currently reads only flat `result.Diagnostics` and ignores `result.Diagnostics.DiagnosticsWithFixes`.

**Actual work:**
1. Wire `DiagnosticsWithFixes` into `CompileTool`'s response (~20 lines)
2. Add `ReportWithFix()` calls to `EffectEnforcementPass` for Calor0410/0411 (currently uses plain `Report()`) — new work
3. Add `ReportWithFix()` calls to remaining ID validation gaps — new work

**Response format:**
```json
{
  "diagnostics": [
    {
      "code": "Calor0410",
      "message": "Forbidden effect db:w in function SaveUser",
      "fix": {
        "description": "Add §E{db:w} declaration",
        "edits": [{"line": 3, "col": 1, "newText": "  §E{db:w}\n"}]
      }
    }
  ]
}
```

**Fix confidence policy (server-enforced, derived from diagnostic code range):**

| Code range | Confidence | autoFix behavior | Rationale |
|------------|-----------|------------------|-----------|
| Calor01xx (parser) | `high` | Auto-apply | Syntax fixes are mechanical |
| Calor08xx (ID) | `high` | Auto-apply | Prefix/format fixes are deterministic |
| Calor04xx (effects) | `medium` | Return as suggestion | Inferred effects may over-approximate |
| Everything else | `low` | Return as informational | Requires human/agent judgment |

No new fields on `SuggestedFix` or `DiagnosticWithFix` — confidence is derived from the diagnostic code. The server applies `high` fixes in `autoFix` mode; `medium` and `low` are returned in the response for the agent to decide.

### 1.2 `autoFix: true` option on `calor_compile`

Not a new tool — a new option on the existing `calor_compile`.

Compile → apply all high-confidence fixes → recompile, up to N passes. Returns final source + fix history.

**Convergence mechanism:** Max N passes (default 3, matching `PostConversionFixer` pattern). Stop early if: (a) zero diagnostics, (b) no fixes were applicable in this pass, or (c) source unchanged after applying fixes. Track which diagnostic codes were fixed to detect cycles.

**Why not "strictly decreasing diagnostic count":** A correct fix can *increase* diagnostics. Fixing a malformed closing tag lets the parser see more of the file, revealing previously-masked errors. This is standard parser behavior. Max-passes is the proven pattern (already used in `PostConversionFixer`).

**Implementation:** New multi-pass loop borrowing `PostConversionFixer`'s pattern (`Migration/PostConversionFixer.cs:31-99`). Not just calling existing `CheckTool.ApplyFixes` in a loop — that's single-pass.

**Fix conflict resolution:** Apply edits in reverse line order. If two fixes target overlapping spans, take the higher-priority one. Priority follows compilation-phase dependency order: parser (Calor01xx) > ID (Calor08xx) > effects (Calor04xx) — because later phases depend on earlier phases being correct.

**No disk side effects.** Returns fixed source in the response. Agent decides whether to write it. Opt-in `write: true` writes to disk.

### 1.3 Batch fix apply

Option on `calor_compile`: `applyFixes: "all"` applies all fix patches and returns unified diff. Not one-at-a-time.

---

## Phase 2: Eliminate Effect Guessing

### 2.1 Effect summary in compile response (opt-in)

New flag on `calor_compile`: `includeEffectSummary: true`.

Per-function effect analysis included in response:

```json
{
  "effectSummary": {
    "SaveUser": {
      "declared": ["db:w"],
      "computed": ["db:w", "cw"],
      "missing": ["cw"],
      "callChains": {"cw": ["SaveUser → Logger.LogInformation"]}
    }
  }
}
```

Data is computed by `EffectEnforcementPass.CheckEffects()` but currently discarded — `_computedEffects` is `private readonly` and not exposed through `CompilationResult`. Surfacing requires: (a) adding a public accessor to `EffectEnforcementPass` or returning effect data through `CompilationResult`, (b) plumbing through `Program.Compile()` → `CompileTool`, (c) serializing `EffectSet` via `ToDisplayString()`. Not trivial but straightforward. Opt-in to avoid bloating every response.

### 2.2 `calor_effects_for` — lookup by symbol

New option on existing `calor_help` (per Design Principle #1 — no new tool):

```json
// Input
{"calls": ["DbContext.SaveChanges", "File.WriteAllText"]}

// Output  
{"perCall": {
   "DbContext.SaveChanges": {"effects": ["db:w"], "source": "bcl-framework-interfaces"},
   "File.WriteAllText": {"effects": ["fs:w"], "source": "bcl-io"}
 },
 "union": ["db:w", "fs:w"],
 "unknown": []}
```

For unknown symbols: `"MyLib.DoThing": {"effects": null, "unknown": true, "manifestTemplate": {"type": "MyLib.SomeService", "methods": {"DoThing": ["TODO"]}}}`. Include a real example alongside the template so untrained agents can pattern-match.

### 2.3 Effect inference option on `calor_compile`

New flag: `inferEffects: true`. Returns proposed `§E{...}` per function with a diff from what's declared.

**Circular dependency note:** This requires parseable source. If the agent is mid-composition (no closing `§/F`), the parser errors. Accept a simpler input: a list of `§C{...}` call sites as an alternative to full source. Or pair with `calor_check` partial validation.

### 2.4 Cascade prediction in diagnostics

When a fix is suggested, include downstream impact as concrete edits (not just descriptions):

```json
{
  "fix": {"edits": [...]},
  "cascade": [
    {"function": "ProcessBatch", "file": "Batch.calr",
     "fix": {"edits": [{"line": 5, "newText": "  §E{db:w}\n"}]}}
  ]
}
```

Untrained agents can't interpret "will also need §E{db:w}" without the exact edit.

**Response cap:** Top 5 callers by call-site count + `"truncated": true` + pointer to `calor_structure action=impact` for the full list. Prevents a widely-called function from flooding the agent's context with hundreds of edits.

**Phase 5 dependency:** Cross-file cascade prediction requires parsing all project files. Without Phase 5's session cache, this means re-parsing per compile call. Document that cross-file cascades are slow-but-correct until Phase 5; same-file cascades work immediately.

---

## Phase 3: Get It Right the First Time

### 3.1 `calor_scaffold` — generate correct skeletons

Input: structured intent. Output: syntactically correct Calor with ULID IDs, correct closing tags, effects auto-resolved from manifests.

```json
// Input
{"kind": "module", "name": "UserService",
 "members": [
   {"kind": "function", "name": "SaveUser",
    "params": [{"name": "user", "type": "User"}],
    "returns": "Result<Unit,Error>",
    "calls": ["DbContext.SaveChanges", "ILogger.LogInformation"]}
 ]}
```

Output as **text block** (not JSON-escaped), with metadata in a separate JSON block:

```
[text/calor]
§M{m_01JWDG3K...:UserService}

§F{f_01JWDG3L...:SaveUser:pub}
  §I{User:user}
  §O{Result<Unit,Error>}
  §E{db:w,cw}
  ...
§/F{f_01JWDG3L...}

§/M{m_01JWDG3K...}

[application/json]
{"idsAllocated": {"m": ["m_01JWDG3K..."], "f": ["f_01JWDG3L..."]},
 "effectsResolved": {"f_01JWDG3L...": ["db:w", "cw"]}}
```

**Type validation:** If the agent passes an invalid type (e.g., `Task<int>` from C# muscle memory), reject with a suggestion pointing to `calor://types`.

**Non-deterministic by design:** IDs are fresh ULIDs per call. Two calls with the same intent produce different IDs. This is correct — ULIDs are globally unique.

**Test determinism:** Optional `seed` parameter that deterministically derives IDs from the seed value. For benchmark fixtures and golden-file tests only. Not for production use.

**Guarantee:** Scaffold output is always a valid parse, even when referenced types don't exist yet. If the agent passes an invalid type (e.g., `Task<int>` from C# muscle memory), reject with a suggestion pointing to `calor://types`.

### 3.2 Effect inference on `calor_convert` (not a new tool)

Per Design Principle #1: fold into `calor_convert` with `includeEffects: true` option. Existing `calor_convert` produces Calor without `§E{...}`. This option adds them.

### 3.3 Enriched compile success response (opt-in)

New flag: `includeContext: true`. Returns forward-looking context:

```json
{"symbolsIntroduced": [...],
 "idsUsed": {"f": {"count": 3, "latest": "f_01JWDG3L..."}},
 "effectsDeclared": ["db:w", "cw"]}
```

**Response size management:** Without session state, return aggregate stats (`count`, `latest`), not full lists. A 200-function project's full ID list is 2KB+ of waste. Full lists available only when session state (Phase 5) lands.

### 3.4 `calor_generate_ids` — ULID generation with correct prefixes

Not "next sequential ID" — generate fresh ULIDs with the right prefix:

```json
// Input
{"needs": [{"kind": "module", "count": 1}, {"kind": "function", "count": 3}]}

// Output
{"ids": {
  "module": ["m_01JWDG3K..."],
  "function": ["f_01JWDG3L...", "f_01JWDG3M...", "f_01JWDG3N..."]
}}
```

---

## Phase 4: Workflow Collapse

### 4.1 Finalize option on `calor_compile`

Extend `calor_compile` with `finalize: true` flag (per Design Principle #1). Runs compile + autoFix + verify + analyze. Returns combined report.

**Single-pass:** autoFix runs first (may change source). Verify and analyze run against the post-fix source. If autoFix changed source, the response includes both the original and fixed versions. The agent should re-call finalize after reviewing auto-applied fixes if further verification is needed.

**Verification failures are not auto-fixable.** autoFix covers parse/ID/effect fixes only. Z3 counterexamples and verification failures require agent reasoning — they're returned as diagnostics, not fix patches.

**Long-running:** Finalize with Z3 verification can take 10+ seconds. If MCP streaming is available, stream intermediate results (compile done → autoFix done → verify done → analyze done). Otherwise return one final blob.

### 4.2 Structured edit operations

New option on `calor_compile`: `ops` array with typed operations. v1 ops:

| Op | Idempotent? | Notes |
|----|-------------|-------|
| `add_effect` | Yes (set union) | |
| `replace_body` | Yes | |
| `add_function` | **No** (creates duplicate) | Use `upsert_function` instead |
| `rename_function` | Yes | |
| `delete_function` | Yes | |
| `raw_text` | N/A | Escape hatch for unlisted ops |

Growth path: new ops added per release. `raw_text` ensures agents aren't blocked by missing ops.

**Op selection must be data-driven:** Before shipping `calor_edit`, run the benchmark corpus and count which ops realistic agent trajectories actually need. Ship those as v1. If >50% of edits fall through to `raw_text`, the tool hasn't solved text-offset errors — iterate on the op set.

### 4.3 Snippet validation (extend existing `calor_check`)

`calor_check` supports **snippet validation** via its `validate` action (wraps a code fragment in synthetic context and runs diagnostics). This is not full partial-program analysis — it's scoped to syntax and basic semantic checks. Improve by adding scope hints:

```json
{"fragment": "§F{...}...", "scope": {"params": [{"name": "user", "type": "User"}]}}
```

### 4.4 Batch convert-and-compile

Extend `calor_batch` to support convert+compile in one call.

### 4.5 `calor_diff_effects` and `calor_impact`

`calor_impact` overlaps with existing `calor_structure action=impact`. Enhance the existing tool rather than creating a new one.

`calor_diff_effects`: given two versions of a function, return added/removed effects. New lightweight tool.

---

## Phase 5: Discoverable State + Session Cache

**Critique identified:** Phases 3-4 features that reference "IDs in use," "index deltas," or "read once per session" implicitly depend on session state. This phase makes the dependency explicit.

### 5.1 Minimal session cache (prerequisite for later phases)

In-memory cache in the MCP server, keyed by file content hash:
- Parsed AST per file
- ID set per file
- Effect declarations per function

**Invalidation:** File hash changes → stale. Explicit `calor_session_reset` clears all.

**Consistency:** Cache may be stale if files change outside MCP. No guarantee of consistency with disk — this is documented. Compile always uses fresh source (not cache).

**Not full session state** (Phase 6 deferred). Just enough to avoid re-scanning all files for ID lists and cross-file references.

### 5.2 MCP resource: `calor://project/index`

Project state snapshot using the session cache:

```json
{"modules": [...], "functions": [...],
 "idsInUse": {"f": {"count": 42, "latest": "f_01JWDG3L..."}}}
```

### 5.3 MCP resource: `calor://manifests/lookup`

Query the effect manifest by symbol. Backed by the existing `EffectResolver`.

### 5.4 Unknown-method diagnostic with manifest template

When compile hits an uncovered .NET call, return a manifest template with a real example:

```json
{"suggestion": {
  "template": {"type": "MyLib.SomeService", "methods": {"DoThing": ["TODO"]}},
  "example": {"type": "System.IO.File", "methods": {"WriteAllText": ["fs:w"]}}
}}
```

---

## Phase 6: Full Session State (Deferred)

Treat a conversation as a session. Server caches parsed modules, reserved IDs, pending edits. Agent references session, not disk. Server streams deltas.

**Deferred** because it's architecturally heavy. Phases 1-4 work without it (slower but correct). Phase 5's minimal cache covers the practical needs.

Questions to answer when designed: where does state live (in-process memory vs on-disk), invalidation model (file hash? mtime?), consistency guarantees (can stale state produce wrong results?), interaction with agent context window (can the agent ask "what changed since my last call?"), session garbage collection (timeouts? explicit close?).

---

## Workflow Guidance

### Tool description hints

Every tool's MCP description field ends with workflow context:

```
"description": "Compile Calor source to C#. Typically the first tool called after writing code. Follow with calor_check if you need type checking."
```

### MCP resource: `calor://workflows`

Structured JSON (not prose — smaller models parse structured data reliably, prose is flaky):

```json
{"tasks": {
  "write-new-function": {"steps": [
    {"tool": "calor_generate_ids", "args": {"needs": [{"kind": "function", "count": 1}]}, "why": "Get a ULID with correct prefix"},
    {"tool": "calor_help", "args": {"action": "effects_for", "calls": ["..."]}, "why": "Resolve effects before writing"},
    {"action": "write_code", "why": "Write the function using scaffold or directly"},
    {"tool": "calor_compile", "args": {"autoFix": true}, "why": "Compile and auto-fix syntax/ID/effect errors"}
  ]},
  "fix-errors": {"steps": [
    {"tool": "calor_compile", "args": {"autoFix": true}, "why": "Auto-fix all high-confidence errors"},
    {"condition": "remaining diagnostics", "action": "read diagnostics and fix manually"},
    {"condition": "unknown external calls", "tool": "calor_help", "args": {"action": "effects_for"}}
  ]},
  "convert-csharp": {"steps": [
    {"tool": "calor_convert", "args": {"includeEffects": true}, "why": "Convert with auto-inferred effects"},
    {"tool": "calor_compile", "args": {"autoFix": true}, "why": "Verify and fix"}
  ]}
}}
```

---

## What NOT to Build

- **`calor_explain_error`** — waste of tokens. Structure diagnostics to be self-applicable.
- **LSP improvements** — no human reads Calor. Move effort into MCP.
- **`calor_propose_next` / `calor_complete`** — completion-style oracle tools. Prefer workflow resources.
- **Sequential ID tools** — production IDs are ULIDs. Don't build `calor_next_id` with sequential numbering.

---

## Implementation Priority

| Phase | Effort | Hypothesis | Pre-req |
|-------|--------|------------|---------|
| **Phase 0** | Low | Baseline measurement enables all future decisions | None |
| **Phase 1** | Low | ≥30% round-trip reduction on fix-up tasks | Phase 0 |
| **Phase 2** | Low-Medium | ≥40% round-trip reduction on effect-error tasks | Phase 0 |
| **Phase 3** | Medium | ≥50% reduction on green-field tasks | Phase 0 |
| **Phase 4** | Medium | ≥30% reduction on multi-phase workflows | Phase 0 |
| **Phase 5** | Medium | Enables cross-file features without full scans | Phase 1-2 shipped |
| **Phase 6** | High | Largest long-term token savings | Phase 5 validated |

Each phase ships behind feature flags. A/B against the benchmark corpus. Go/no-go at each gate. If a feature doesn't meet thresholds after tuning, don't ship it.
