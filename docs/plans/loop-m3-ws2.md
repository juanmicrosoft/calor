# M3 Kickoff — WS2 Verified Mutation Loop (Descoped)

**Status: COMPLETE (2026-07-27).** All four slices merged (#796 kickoff+allowlist,
#797 D2.1+D2.4, #798 D2.5, #799 harness+gate) plus the watchdog hardening
(#800), and the exit criterion is met:
`bench/phase0-agent-native/epochs/ws2-exit-e2e-001/VERDICT.md` — 5/5 valid
runs exclusively through MCP, M-L2(mcp-file) = 100 % (heal-adjusted and
strict forms identical), multi-edit substantiated by 4–5 declaration changes
per transactional write on W3-004, M-L4 reported-only at 0 rejects.

**Kickoff record (2026-07-24):**
**Parent:** `loop-plan-v0.9.md` §2 WS2, §3 M3 · **Scope authority:** Call 1 record (`loop-m2-baseline.md`), Annex A freeze (#795)

## 1. Scope as descoped by Call 1

Machine-zone E1 killed H1 (the text-serialization-tax hypothesis), so the §2
scope gate fires: **D2.2 (`calor_get_node`) and D2.3 (`calor_edit_apply`) are
dropped; PP-L3 is retired** and never runs. M3 builds the miss path directly:

| Deliverable | Content | Status at kickoff |
|---|---|---|
| **D2.1** Project sessions | Session-scoped project context in the MCP server: open a directory, hold parsed ASTs, dirty-state invalidation on behind-the-back file changes | Nothing exists — `McpServer.cs` is a stateless per-call dispatcher |
| **D2.4** File-level transactional apply | Check-then-apply for whole-file writes: run the `EditPreviewTool` check set, apply atomically on `safe`/`safe_with_warnings`, reject `breaking` with the schema v1.1 envelope | Check set + envelope machinery exist; no MCP tool writes an existing `.calr` file |
| **D2.5** Write-path robustness | Fault-tolerant parse mode + canonical-formatter auto-heal for serialization slips | `SourceHealer` (CLI `format --heal`) exists; no fault-tolerant parser mode; heal not wired into any MCP path |

**Exit criterion (restated for the descope):** an agent completes a multi-edit
E2E task exclusively through the MCP file tools (D4.2 `--edit-mechanism
mcp-file` arm-constraint), with **M-L2 (first-apply validity, file mechanism)**
and **M-L4 (reject precision, reported-only below 20 rejects per Annex A)**
measured. The plan's original wording included node-mechanism splits; those
left with D2.2/D2.3.

## 2. Priced decisions (recorded at kickoff, per §3 kickoff discipline)

**No project-file format in v0.9.** D2.1's parent text priced "project-file
format TBD" inside the deliverable. Decision: a session opens a **directory**
and its `.calr` file set (recursive glob); no `.calorproj` is introduced.
Rationale: compilation is already file-list-driven (`CompilationDriver`), no
consumer needs manifest semantics for the exit criterion, and inventing a
format would be unpriced scope. Revisit trigger: WS3 D3.3 (10k-line fixture)
or v0.10 multi-project needs.

**Dirty-state invalidation is stat-on-access, not a watcher.** Each tool call
that touches session state re-stats its files (mtime/size gate; SHA-256 hash
to suppress reparses of touched-but-unchanged files — the
`BuildStateCache.IsFileUpToDate` pattern) and reparses only changed files. A
filesystem watcher adds lifecycle complexity a single-client stdio server
does not need. Stated limitation (BuildStateCache semantics): an edit that
preserves both mtime and size is not detected until the stat next changes.
Revisit trigger: WS3 D3.1 warm-state latency work, where re-stat cost appears
in M-L1.

**Write confinement is canonical-path containment.** Every `calor_file_write`
must land, after symlink resolution, under the session root — or under the
server's working directory when no session is given; session roots are
themselves confined under the server's working directory, since a session
confers write access. Writes to the same file are serialized in-process and
on-disk content is revalidated against what was checked before the rename;
races with *external* writers on the final rename remain (only OS file locks
would close them) and are accepted for a single-client stdio server.

**Sessions are optional on the write path.** `calor_file_write` works without
a session (checks scoped to the single file, original = current disk content);
with a `sessionId`, reference checks widen to the session's file set. This
keeps the harness agent UX to one required call while making D2.1 context
additive.

**Box confirmation:** the 6–8 wk box was sized for full WS2 (§7 risk 2 names
D2.2/D2.3 among the sized concerns). Descoped M3 is re-boxed at **3–4 wk**.

## 3. Slicing (each PR merges green on its own)

1. **PR 1 (this PR):** kickoff record + `.calor-csharp-allowlist` entries for
   the planned new C# files — the calor-first guard requires entries on main
   at the merge base before the implementation PR can add the files.
2. **PR 2 — D2.1 + D2.4 core:** `Mcp/Sessions/` (session manager + project
   session with dirty invalidation), `calor_session_open`/`calor_session_close`
   tools, `calor_file_write` (heal → parse → check set → verdict → atomic
   tmp+move apply, envelope on reject), registry test updates, unit tests.
3. **PR 3 — D2.5:** fault-tolerant parse mode (partial AST + diagnostics
   instead of hard failure) surfaced through the write path's reject envelope;
   heal already wired in PR 2.
4. **PR 4 — harness:** `run-pair.sh` registers the MCP server for `mcp-file`
   arms and the D2.4 hard gate opens (the PreToolUse hook blocking raw
   `Edit`/`Write` on `.calr` stays); then the exit-criterion E2E run with
   M-L2/M-L4 telemetry (loop telemetry v2 already records edit mechanism and
   apply verdicts).

New C# files are compiler-internal MCP server surfaces (session lifecycle,
protocol tools) — same allowlist rationale as the #754 envelope machinery.
If PR 3 needs a new file, its allowlist entry rides in PR 2.

## 4. Non-goals

- Warm/incremental rebind (WS3 D3.1) — sessions here cache parse state only;
  perf is out of scope until M4.
- Node-addressed anything (retired with PP-L3).
- MCP write access for non-`.calr` files — the write tool refuses them, and
  confines every write per the canonical-path decision in §2, matching the
  harness hook's boundary.
- Binding in the check set — the write-path checks are parse-level (plus
  contracts/effects/reference heuristics); running the binder project-wide at
  check time is WS3 warm-state territory. M-L2 ("parse + bind") is measured
  by the harness on the resulting build, not inferred from the tool verdict.
