---
layout: default
title: mcp
parent: CLI Reference
nav_order: 11
permalink: /cli/mcp/
---

# calor mcp

Start the Model Context Protocol server for coding agents.

```bash
calor mcp --stdio --root /absolute/project/path
```

The server communicates over stdio. Pin `--root` in harnesses and CI: it confines
project sessions, file writes and project-index queries. Without it, write
confinement falls back to the server's working directory. It does **not** sandbox
test execution.

## Options

| Option | Default | Description |
|:-------|:--------|:------------|
| `--stdio` | `true` | Communicate over standard input/output |
| `--verbose`, `-v` | `false` | Write debug logs to stderr |
| `--root` | Working directory | Root for project sessions, writes and index queries |

## Available tools

The server registers **19 tools**. Discover their current input schemas with
`tools/list`; the names below are registered tools, not historical aliases.

| Tool | Purpose and selectors |
|:-----|:----------------------|
| `calor_compile` | Compile Calor to C#; optional verification, analysis and safe diagnostic fixes |
| `calor_verify` | Verify contracts with Z3; preserve each reported proof status |
| `calor_check` | `action`: `diagnose`, `lint`, `typecheck`, `validate`, `loop` |
| `calor_analyze` | `action`: `security`, `assess`, `minimize` |
| `calor_convert` | `mode`: `convert`, `validate`, `roundtrip`, `assess` |
| `calor_batch` | `action`: `convert`, `analyze`, `compile`; conversion `mode`: `standard`, `interop` |
| `calor_help` | `action`: `syntax`, `features`, `error`, `example` |
| `calor_navigate` | `action`: `definition`, `references`, `find`, `info`, `scope` |
| `calor_structure` | `action`: `outline`, `callgraph`, `impact` |
| `calor_query` | Project-index `facet`: `callers`, `callees`, `impact`, `effects` |
| `calor_edit_preview` | Preview an edit with compile, contract, effect and reference checks |
| `calor_format` | `action`: `format`, `ids`; `ids` returns project-aware identity guidance, not assignment |
| `calor_session_open` | Open a project session for cross-file checks |
| `calor_session_close` | Release a project session |
| `calor_file_write` | Confined, checked operations; `action`: `write`, `rename`, `move` |
| `calor_refine` | `action`: `obligations`, `bounds`, `diagnose`, `guards`, `fixes`, `types` |
| `calor_fix` | Return source repaired by compiler-authorized safe diagnostic patches; never writes |
| `calor_migrate` | Project pipeline; `phase`: `assess`, `convert`, `compile`, `fix`, `full`; can write files |
| `calor_self_test` | Run compiler self-tests against embedded golden files |

Older one-purpose tool names are not aliases. For example, use
`calor_check action=typecheck`, `calor_navigate action=definition`, or
`calor_help action=syntax`, not the former standalone tools.

## Compile, diagnose and repair

Compilation-backed tools default to the System-only project context, not the
full SDK namespace set. `calor_compile` accepts
`"options": {"implicitSystem": false}` for standalone validation.
`calor_convert`, `calor_check`, `calor_verify`, `calor_analyze`, `calor_refine`,
`calor_fix`, `calor_batch` and `calor_migrate` accept top-level
`"implicitSystem": false`. Conversion uses the same choice for original C#
binding and emitted C# validation. Parse-only navigation/preview operations do
not perform downstream C# validation.

This setting never rewrites preserved text or relaxes effect checks.
`CS0104` reports ambiguous short names; aliases and fully qualified names remain
escape hatches. A saved C# output needs the same context in its real project.
See [compilation-context imports](../semantics/dotnet-backend.md#compilation-context-imports)
for project, CLI, low-level API and LSP/editor policy.

Requests use current native Calor: lowercase declarations, ordinary constructor
calls, infix expressions and indentation. Declaration IDs belong in
`.calor/ids.json`, not source.

```json
{"name":"calor_compile","arguments":{"source":"module Calc\n  func Add (int:x, int:y) → int\n    !\n    return x + y\n"}}
```

```json
{"name":"calor_check","arguments":{"action":"diagnose","source":"module Calc\n  func Add (int:x, int:y) → int\n    !\n    return x + y\n","apply":true}}
```

`diagnose` returns `success`, counts, shared diagnostic entries and optional
common-mistake hints. With `apply: true`, it also returns `fixedSource` and
`fixesApplied`. All diagnostics and `isError` describe the freshly checked source.
`calor_compile options.autoFix` and `calor_fix` use the same safe-patch machinery.

```json
{"name":"calor_fix","arguments":{"source":"module Calc\n    func Add (int:x, int:y) → int\n        !\n        return x + y\n","errors":["safe_diagnostics"]}}
```

`calor_fix` returns `success`, `fixedSource`, `wasModified`, `fixCount`,
`diagnostics`, and, when patches were applied, `fixes` as diagnostic-description
strings. `fixCount` counts edits, not descriptions. Up to three patch batches
are applied, with a fresh compilation after each. `filePath` supplies diagnostic
source context or reads a file when `source` is absent; the tool never saves it.
If errors remain, `success` is false and `isError` is true even when no fix exists.

Only patches marked `isSafeToApply` by the compiler are applied. Invalid spans,
overlapping edits and cross-file patches fail explicitly. Guessed constructor
types, fuzzy names, effect-contract changes and ambiguous indentation are not
automatic repairs. Strings, comments and counted raw C# payloads are not searched
and rewritten as source syntax. Valid `new object()` calls remain unchanged.

The `new_object` rule is retired and rejected. The `id_conflicts` rule is also
rejected: these tools never rename declarations or call targets, or alter
sidecar identities. For persisted identities, use
`calor ids sync PROJECT --register FILE` and `calor ids check FILE`.
`calor_format action=ids` returns this guidance as an error, not a passed check.

Migration's standalone `fix` phase and the `full` pipeline use the same
compiler-authorized patches with their existing migration compilation policy.
They write only when the repaired file compiles. Unresolved errors are reported
as failures, including when no safe patch exists; failed candidates leave the
file unchanged and report zero persisted fixes. A successful per-file fix is
not a project-wide build or contract proof.

## Build, test and verify in one call

```json
{"name":"calor_check","arguments":{"action":"loop","path":"/absolute/source/directory","timeoutSeconds":600,"verificationTimeoutMs":5000}}
```

`loop` requires `path`, not `source`. It returns the same failure-only result as
[`calor check`](/calor/cli/#single-call-validation), without applying fixes.
Missing tests and non-proven contracts are not success.

**Use trusted inputs.** This action executes arbitrary tests with your
permissions and may restore NuGet packages. `calor_check` advertises neither
read-only nor idempotent behavior. Listing tools or inspecting schemas does not
execute tests. `--root` does not sandbox them.

## Analysis and migration

```json
{"name":"calor_analyze","arguments":{"action":"assess","source":"public class Counter { public int Next(int x) => x + 1; }"}}
```

`assess` scores C# migration potential; it does not prove a conversion is correct.
`security` analyzes Calor for bug patterns, dataflow and taint issues.
`minimize` accepts Calor `source` containing converter-owned C# frames, a
`filePath`, or raw `csharpCode`. It reports possible native constructs.
Its `calorEquivalent` values are current syntax templates from the shared teaching
reference, not literal replacement patches. Preserve modifiers, constructor
selection, effects and metadata; convert the original C# and check the result.
Unsupported shapes may still require exact converter-owned C# preservation.

`calor_convert` converts C# to Calor. Use `mode=validate` for conversion followed
by validation, `mode=roundtrip` for round-trip checking, or `mode=assess` for
convertibility scoring. `conversionMode` is a separate `standard`/`interop`
selector. These are not retired standalone tool names.

Conversion results distinguish `interop`, `rescue`, `passthrough`, `lossy` and
`dropped` paths. Read each path's outcome and diagnostics rather than treating
written output as a passed build. `calor_convert` does not rescue round-trip
failures by default; `passthroughOnError` opts into preservation.
`calor_batch` and `calor_migrate` do not rescue project round-trip failures.
See the [conversion reference](/calor/cli/convert/) for result fields and defaults.

## Proof and bounds limitations

Verification distinguishes `proven`, `refuted`, `assumed`, `unknown`, `timeout`,
`unsupported` and `unavailable`. Only `proven` is proof. Missing metadata or
source context must not be interpreted as purity or safety.

`calor_refine action=bounds` currently checks only its supported indexed sites.
Its `safe: true` field is **not** proof that every array access is safe: an empty
site set or failed compilation can still return that value. Check compilation,
site coverage and per-site outcomes separately. Unknown or unsupported cases
remain unproven.

## Examples-first help and resources

```json
{"name":"calor_help","arguments":{"query":"dictionary"}}
```

`syntax` is the default action; `feature` aliases `query`. The result contains
at most two closest complete programs from the canonical teaching reference,
with `programId`, exact `calor` source and C# context. `construct`, `calorSyntax`
and `description` retain syntax guidance and caveats. Lookup does not execute
code. The two-example bound is not a measured token budget.

Unknown topics and known topics without a canonical executable example return
`found: false` and an empty `examples` array. Use `action=features` for conversion
capabilities; preserved C# is not native authoring syntax.
`query=overview` and `query=reference` return prose in `sections`.

| Resource | Content |
|:---------|:--------|
| `calor://reference` | Complete generated current-language reference |
| `calor://primer` | Fixed subset of complete examples and common mistakes |
| `calor://syntax-reference` | Syntax lookup and LSP hover metadata |
| `calor://tags` | Current construct syntax and delimiter guidance |
| `calor://type-mappings` | C# and Calor type mappings |
| `calor://effects` | Effect-code examples |
| `calor://id-prefixes` | Tooling-owned identity guidance; source ID prefixes are retired |
| `calor://workflows` | Agent editing workflows |

`src/Calor.Compiler/Resources/Teaching/reference.json` authors the generated
teaching resources. `calor self-check docs --teaching-only --fix` regenerates
them; without `--fix`, it rejects stale output.
`CALOR_REFERENCE_FILE` overrides only explicit full-reference requests.
It cannot inject examples into normal topic lookup.

## Client configuration

Configure an MCP client to launch the tool over stdio. For example, a Claude
Desktop configuration can contain:

```json
{
  "mcpServers": {
    "calor": {
      "command": "calor",
      "args": ["mcp", "--stdio", "--root", "/absolute/project/path"]
    }
  }
}
```

For VS Code, use the same entry under `servers` in `.vscode/mcp.json`.
`calor init --ai claude` and `calor init --ai gemini` generate their respective
client configuration.

## Protocol

The server implements MCP `2025-03-26` over stdio with newline-delimited JSON
(one JSON-RPC message per line), not Content-Length headers.
It supports initialization, tool discovery/calls, resource discovery/reads,
prompt discovery/reads and ping. Tool calls use their registered input schemas;
diagnostic-producing results embed shared
[diagnostic envelope entries](/calor/cli/envelope-schema/) inside tool-specific
result objects. An MCP result is not automatically a CLI command envelope.
