---
layout: default
title: Envelope Schema
parent: CLI Reference
nav_order: 16
permalink: /cli/envelope-schema/
---

# Diagnostic Envelope Schema v1.1 (loop plan D1.1)

This document is the normative definition of the **one envelope** every Calor
surface emits for machine consumers, and the **enumerated denominator** of
surfaces that must emit it. It exists so that an agent that learns to parse
`calor compile --format json` has learned to parse every other command and MCP
tool too. The conformance test
(`tests/Calor.Compiler.Tests/EnvelopeConformanceTests.cs`, loop plan D1.4) is
the enforcement mechanism: it round-trips each adopted surface's output through
the schema and fails CI on drift. This table — not a grep — is the denominator
for the M-E1 envelope-coverage metric.

See [Structured Output](/calor/cli/structured-output/) for the stdout/stderr
contract, NDJSON streaming, SARIF mapping, and exit codes.

## Document shape

```json
{
  "version": "1.1",
  "command": "verify",
  "diagnostics": [ /* diagnostic entries, possibly empty */ ],
  "summary": { "total": 1, "errors": 0, "warnings": 1, "info": 0 },
  "data": { /* command-specific payload, optional */ }
}
```

- `version` — envelope schema version, currently `"1.1"`. Consumers must
  tolerate unknown additive fields within a major version.
- `command` — the producing surface (optional; emitted by data-carrying
  commands so a stream of documents is self-describing).
- `diagnostics[]` — always present, possibly empty. Entry shape below.
- `summary` — counts over `diagnostics[]`; `total` equals the array length.
- `data` — command-specific payload for data-carrying commands (scores,
  benchmark numbers, manifests). Its shape is owned by the command's own doc;
  the envelope guarantees only that diagnostics never hide inside it.

Null fields are omitted everywhere (`camelCase`, `WhenWritingNull`).

## Diagnostic entry

Implemented once in `src/Calor.Compiler/Diagnostics/DiagnosticEnvelope.cs`
(`EnvelopeDiagnostic`); CLI commands serialize it via `JsonDiagnosticFormatter`,
MCP tools embed the same objects in their result DTOs.

```json
{
  "code": "Calor0712",
  "message": "Postcondition may be violated in function 'BadDiv'. Counterexample: x=1, result=10",
  "severity": "warning",
  "location": { "file": "/abs/path/demo.calr", "line": 4, "column": 5, "length": 16 },
  "declarationId": "f001",
  "verification": {
    "status": "refuted",
    "counterexample": {
      "rendered": "Counterexample: x=1, result=10",
      "bindings": [ { "name": "x", "value": "1" }, { "name": "result", "value": "10" } ]
    }
  },
  "suggestion": "…",
  "fix": { "description": "…", "edits": [ { "filePath": "…", "startLine": 4, "startColumn": 7, "endLine": 4, "endColumn": 12, "newText": "…" } ] }
}
```

- `code` — stable `CalorNNNN` code (band table in
  [Structured Output](/calor/cli/structured-output/)).
- `severity` — `"error" | "warning" | "info"`.
- `location` — 1-based line/column; `file` may be null; `length` in characters.
- `declarationId` — ID of the nearest enclosing declaration (`f001`, `m001`,
  class/method/property IDs), resolved by byte-offset containment over
  `IdScanner` spans (`Ids/DeclarationIdResolver.cs`). **Null when IDs are
  absent** — IDs stay optional per language policy — or when the position falls
  outside every ID-bearing declaration (e.g. lexer errors before any AST
  exists).
- `verification` — present only on contract diagnostics (`Calor0710`–`Calor0718`
  band). `status` is the **closed five-status vocabulary**; see below.
- `suggestion` / `fix` — machine-applicable fix hint when one exists;
  `fix.edits[]` are 1-based, end-exclusive text edits.

## Verification payload and the five-status vocabulary

`verification.status` is one of exactly:

| Status | Meaning | Payload guarantees |
|:-------|:--------|:-------------------|
| `proven` | The obligation holds; runtime check may be elided | — |
| `refuted` | Proven violable | `counterexample` present whenever the solver produced a model (model-less refutations, e.g. an unsatisfiable precondition, carry `reason` instead) |
| `unknown` | Inconclusive, **not** a timeout (too complex, incomplete theory, solver error, solver unavailable) | `reason` carries the solver's own explanation when available |
| `timeout` | The solver hit its time budget | `reason` carries the solver's unknown-reason string |
| `unsupported` | Not translatable to the solver (unsupported type/construct) | `reason` carries the translation diagnosis |

Every solver-evidence status is assigned at a single choke point —
`ProofOutcome.Assign` in `src/Calor.Compiler/Verification/ProofOutcome.cs`
(loop plan D1.2). The type's constructor is private and the conformance suite
verifies no construction site exists outside that file, which is what makes
"no silent cliffs" a stable property rather than an enumeration of known
fallback sites. **Stated precisely**: two other in-file methods also mint
statuses — `Rehydrate` (cache/telemetry deserialization) and
`FromLegacyContractStatus` (pre-outcome cache entries) — both restore a status
that was originally assigned by `Assign`, carry no solver evidence of their
own, and are confined to the same reviewed file; the guarantee is
"single file, three documented entry points", not "the type system makes
bypass impossible". Non-proven outcomes are always surfaced as diagnostics:
refuted as warnings (`Calor0711`/`Calor0712`), timeout / unknown / unsupported
as info (`Calor0717` / `Calor0716` / `Calor0718`).

## The denominator

Every CLI command (`src/Calor.Compiler/Commands/` plus the root compile
command) and every MCP tool (`src/Calor.Compiler/Mcp/Tools/`), by name.
Classes:

- **E (envelope)** — must emit this document (CLI) or embed
  `EnvelopeDiagnostic[]` for all source-anchored diagnostics (MCP tools).
- **D (data-carrying)** — no source-anchored diagnostics; must still emit the
  top-level envelope with `diagnostics: []` and its payload under `data`.
- **X (exempt)** — output format owned by an external protocol or is not
  machine-consumed; rationale given. Exemptions are deliberate and reviewed,
  not defaults.

### CLI commands

| Command | Class | Envelope adopted? | Notes |
|:--------|:------|:------------------|:------|
| compile (root) | E | **Yes** (`--format json\|sarif`) | declarationId + verification live |
| `lint` | E | **Yes** (`--format json\|sarif`) | |
| `watch` | E | **Yes** (NDJSON, `--format json`) | one document per rebuild |
| `self-check` | E | **Yes** (`--format json\|sarif`) | docs-drift findings |
| `verify` | E | **Yes** (`--format json`) | envelope wrapper with `command: "verify"`; per-contract five-status (+`legacyStatus` for one release) and counterexamples under `data` |
| `assess` | E | **Yes** (`--format json\|sarif`) | JSON wraps the assessment under `data`; SARIF shared |
| `convert` | E | **Yes** (`--format json`) | conversion issues as `Calor1343` envelope diagnostics; direction/features/benchmark under `data` |
| `format` | E | **Yes** (`--format json`) | real parser diagnostics + `Calor1340`-band; per-file statuses under `data` |
| `ids` | E | **Yes** (`check --format json`) | `Calor0800`-band findings as envelope diagnostics with `declarationId`; `index` artifact (`calor.ids.json`) unchanged |
| `effects` | D | **Yes** (`--json`) | resolve/list/suggest stdout wrapped under `data`; manifest file stays raw |
| `benchmark` | D | **Yes** (`--format json`) | quick/project/full payloads under `data`; string-interpolated JSON replaced with real serialization |
| `coverage` | D | **Yes** | `CoverageResult` under `data` |
| `feature-check` | D | **Yes** | payloads under `data` |
| `analyze-convertibility` | D | **Yes** | file/directory payloads under `data` |
| `fix` | D | **Yes** (`--format json`) | operation summary under `data`; `migration.log.json` unchanged |
| `migrate` | D | No | `--report` file, own report schema |
| `evaluation` | X | — | internal A/B harness registry; consumed only by the epoch tooling, schema owned by `bench/` |
| `hook` | X | — | agent-gate responses whose dialect is dictated by the host agent (`--format gemini` etc.) |
| `init` | X | — | scaffolding; human-oriented status only |
| `run` / `test` | X | — | exec passthrough; program output is the program's |
| `self-test` | X | — | golden-diff harness, human-oriented |
| `lsp` | X | — | LSP protocol (JSON-RPC) owned by the LSP spec |
| `mcp` | — | — | server; its tools are the surfaces, enumerated below |

### MCP tools

| Tool | Class | Envelope adopted? | Notes |
|:-----|:------|:------------------|:------|
| `calor_compile` | E | **Yes** | `diagnostics[]` are envelope entries with `declarationId` |
| `calor_check` | E | **Yes** | envelope entries; `commonMistake` hints moved to a sibling `hints[]` array |
| `calor_verify` | E | **Yes** | five-status per contract (+`legacyStatus`), structured counterexamples, `proofStatusCounts` |
| `calor_refine` | E | **Yes** | `proof_status` + `counterexample_bindings` added (snake_case retained) |
| `calor_analyze` | E | **Yes** | issue groups are envelope entries with `declarationId` |
| `calor_edit_preview` | E | **Yes** | `compilationResult.errors` are envelope entries; verdict payload unchanged |
| `calor_convert` | E | No | conversion issues, flat |
| `calor_batch` | E | No | per-file error strings |
| `calor_migrate` | E | No | per-file `[Code] Ln: msg` strings |
| `calor_navigate` | E | No | parse errors as strings |
| `calor_structure` | E | No | parse errors as strings |
| `calor_format` | E | No | ids issues, own shape |
| `calor_fix` | D | No | applied-fix report |
| `calor_help` | X | — | documentation lookup; no source-anchored diagnostics |
| `calor_self_test` | X | — | golden-diff scenarios |

**M-E1 (envelope coverage)** = adopted E+D surfaces / all E+D surfaces. The
"Envelope adopted?" column is flipped by the WS1 adoption sweep (loop plan
D1.3); WS1 exits at 100 %.

## Versioning rules

- Additive fields bump the minor version (`1.0` → `1.1`); consumers must not
  reject unknown fields.
- Removing or renaming a field, or changing a field's type, bumps the major
  version and requires a migration note in `CHANGELOG.md`.
- The five-status vocabulary is **closed**: adding a status is a major bump.

## Change log

- **1.1** — added `declarationId`, `verification` (five-status choke-point
  payload), optional top-level `command` and `data`. First version governed by
  this document.
- **1.0** — pre-envelope shared JSON schema (compile/lint/watch/self-check).
