---
layout: default
title: CLI Reference
nav_order: 6
has_children: true
permalink: /cli/
---

# CLI Reference

The `calor` command-line tool provides commands for working with Calor code and analyzing C# codebases for migration.

---

## Installation

Install `calor` as a global .NET tool:

```bash
dotnet tool install -g calor
```

Or update an existing installation:

```bash
dotnet tool update -g calor
```

---

## Available Commands

| Command | Description |
|:--------|:------------|
| `calor` (default) | Compile Calor source files to C# |
| `calor run` | Compile and execute a Calor program in one step (no project file required) |
| `calor test` | Compile Calor sources and run xUnit tests against them (no project file required) |
| `calor check` | Build, test and verify one source snapshot; return failures only |
| `calor --analyze` | Static analysis: find bugs via dataflow, Z3 verification, and taint tracking |
| [`calor assess`](/calor/cli/assess/) | Score C# files for Calor migration potential |
| [`calor init`](/calor/cli/init/) | Initialize Calor with AI agent support and .csproj integration |
| [`calor convert`](/calor/cli/convert/) | Convert single files between C# and Calor |
| [`calor migrate`](/calor/cli/migrate/) | Migrate entire projects between C# and Calor |
| [`calor ids`](/calor/cli/ids/) | Manage unique identifiers (check, assign, index) |
| `calor index` | Build (`build`) or check (`status`) the persistent project index under `obj/calor` |
| `calor query` | Ask the index: `symbol`, `callers`, `callees`, `impact` (`--effects [--row …]` for effect-row blast radius), `contracts`, `assumptions`, `effects` (declared / inferred row and verdict). `--json` on `callers`, `callees`, `impact` and `effects`; the MCP tool `calor_query` answers the same four from the same index |
| [`calor benchmark`](/calor/cli/benchmark/) | Compare Calor vs C# across evaluation metrics |
| [`calor format`](/calor/cli/format/) | Format Calor source files to canonical style |
| [`calor fix`](/calor/cli/fix/) | Bulk, reversible source rewrites (e.g. drop legacy closing-tag IDs) |
| `calor import` | Generate effect manifests (and assumed-provenance contract facts) for a package's public surface |
| `calor review-packet` | Assemble a per-change review packet that leads with the unproven remainder |
| [`calor mcp`](/calor/cli/mcp/) | Start MCP server for AI agent tool integration |
| [`calor self-check docs`](/calor/cli/self-check/) | Verify agent-facing docs against the implementation (keywords, diagnostic codes, effect codes, parseable examples, version); exits nonzero on drift |
| `calor lsp` | Start Language Server Protocol server for IDE features |
| [`calor hook`](/calor/cli/hook/) | Claude Code hook commands (internal) |

Machine-readable diagnostics: the compile and lint commands support
`--format json|sarif` — see [Structured Output](/calor/cli/structured-output/)
for the JSON schema, SARIF mapping, and exit-code contract.

## Single-call validation

```bash
calor check path/to/sources --format json
```

`check` snapshots `.calr` files and the existing `tests/*.cs` xUnit layout,
including `tests/shims/TestShim.calor.cs`. Discovery excludes `bin/`, `obj/`,
and `reference/`. It compiles the snapshot once with strict effects and contract
verification, builds an isolated temporary project, then runs tests with
`--no-build`. It does not repair source or modify project files. A file target
builds without discovering adjacent tests; use a directory to run tests.

The JSON result contains `success`, `exitCode`, `stages`, `failures`,
`testsRun`, and `contractsChecked`. Compiler failures retain the shared
diagnostic shape, including safe-fix edits, declaration IDs, severity, proof
status and counterexamples. Fix paths point to the original source. Build and
runner failures include process errors; failed tests include their TRX details,
not passing-test output. Generated C# validation errors belong to `compile`;
SDK and test-project build errors belong to `build`. Each stage reports
`passed`, `failed`, `incomplete`, `skipped`, `no_tests`, or `timeout`.

Exit 0 requires a successful build, at least one test, all tests passed, and
every contract proven without vacuity. `assumed`, `unknown`, `timeout`,
`unsupported`, `unavailable`, and vacuous proofs remain failures, with their
original proof metadata. No contracts means zero contracts checked, not evidence
about unspecified properties. Exit 3 means no tests; compilation or verification
failures use 1; input/process errors and timeouts use 2. Child build/test exit
codes are preserved. CLI cancellation exits 130; MCP cancellation propagates.
Compilation failure skips building and testing. Build failure skips testing;
verification findings already computed from the snapshot remain in the result.

Text mode prints only failures, or one concise success line. `--timeout` sets
the per-build/test timeout in seconds (1–600, default 600).
`--verification-timeout` sets the solver timeout per contract in milliseconds
(positive, default 5000). These are not a total-call deadline.
The process deadline includes output draining after the root process exits.
Redirected standard input is closed; validation does not answer interactive prompts.

**Only validate trusted code.** Tests and preserved C# execute with the current
user's permissions. Temporary workspaces isolate build files, not execution.
Package restore may access NuGet. Cancellation and timeouts terminate the child
process tree and clean up the temporary workspace.

---

## Compilation (Default Command)

Compile Calor source files to C#:

```bash
calor --input file.calr --output file.g.cs
```

### Options

| Option | Description |
|:-------|:------------|
| `--input`, `-i` | Input Calor file path (required) |
| `--output`, `-o` | Output C# file path (required) |
| `--verbose`, `-v` | Show detailed compilation output |
| `--format`, `-f` | Diagnostic output format: `text` (default), `json`, or `sarif` — see [Structured Output](/calor/cli/structured-output/) |

### Example

```bash
# Compile a single file
calor --input src/MyModule.calr --output src/MyModule.g.cs

# Compile with verbose output
calor -v -i src/MyModule.calr -o src/MyModule.g.cs
```

---

## See Also

- [Getting Started](/calor/getting-started/) - Installation and first program
- [Syntax Reference](/calor/syntax-reference/) - Complete language reference
