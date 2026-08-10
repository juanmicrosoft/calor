# Telemetry

Calor telemetry measures command reliability and compiler feature usage. It is
anonymous, schema-enforced, **off by default**, and never required to use the
CLI, SDK, MCP server, or watch mode.

## Opt in or out

| Action | Configuration |
|---|---|
| Opt in | Set `CALOR_TELEMETRY=1` (or `true`) |
| Force opt out | Pass `--no-telemetry`, or set `CALOR_TELEMETRY_OPTOUT=1` |
| Preview locally | Pass `--telemetry-preview` |

`--no-telemetry` overrides opt-in and preview. SDK use emits nothing unless the
host explicitly initializes telemetry; normal SDK use does not do so.

## Operator and endpoint configuration

Opt-in alone is insufficient. The operator must also provide a valid Azure
Application Insights connection string through
`CALOR_TELEMETRY_CONNECTION_STRING`. Calor source and packages contain no
instrumentation key or endpoint credential. The connection string must contain
a GUID `InstrumentationKey`; an `IngestionEndpoint`, when present, must use
HTTPS. Missing or invalid configuration disables telemetry.

For project-provided production builds, the Calor project operates the
configured Application Insights resource. For redistributed or internally
hosted builds, the party setting `CALOR_TELEMETRY_CONNECTION_STRING` is the
operator and data recipient.

Endpoint/channel failures never affect compiler behavior. Configuration,
schema validation, value redaction, serialization, or send failures all fail
closed to no telemetry.

## Preview

Run any public CLI command with `--telemetry-preview`:

```bash
calor convert input.cs --telemetry-preview
calor --input input.calr --telemetry-preview
```

Preview does not require opt-in or endpoint configuration. It writes one JSON
payload per line to **stderr**, uses the same schema and sanitization path as
production telemetry, and creates no network telemetry client. Keeping preview
on stderr preserves command stdout formats such as JSON and SARIF.

## Schema and complete payload inventory

The current application schema is **version 1.0**. The mechanical snapshot is
[`telemetry-schema-v1.json`](telemetry-schema-v1.json). Tests compare that file
byte-for-byte with the runtime schema, so adding/removing an event or field
requires an explicit snapshot and privacy-review update.

Every payload has:

- `schemaVersion`: `1.0`
- `eventName`: one of the names below
- `properties`: only the event's listed low-cardinality string fields
- `metrics`: only the event's listed non-negative numeric fields
- `context`: exactly the global fields listed below

| Event | Property fields | Metric fields |
|---|---|---|
| `CommandSucceeded`, `CommandFailed` | `command`, `exitCode`, `error`, `verbose`, `list` | `durationMs`, `fileCount`, `issueCount`, `errorCount`, `blockerCount`, `totalContracts`, `provenContracts`, `verifyContracts`, `verifyProven`, `verifyDisproven`, `verifyDurationMs` |
| `CompilationPhase` | `command`, `phase`, `success` | `durationMs`, `tokenCount`, `functionsAnalyzed`, `bugPatternsFound`, `taintVulnerabilities` |
| `DiagnosticOccurrence` | `command`, `code`, `severity`, `category` | — |
| `DiagnosticCoOccurrence` | `command`, `codeA`, `codeB` | `count` |
| `Exception` | `command`, `exceptionCategory`, `phase` | — |
| `CompileOptions` | `command`, `strictApi`, `requireDocs`, `enforceEffects`, `strictEffects`, `permissiveEffects`, `contractMode`, `verify`, `noCache`, `analyze`, `strictBindInference` | `verificationTimeout`, `experimentalFlagCount` |
| `UnsupportedFeatures` | `command` | `totalUnsupportedCount`, `distinctFeatureCount` |
| `UnsupportedFeature` | `command`, `feature` | `count` |
| `InputProfile` | `command`, `hasContracts`, `hasEffects`, `hasModules`, `sizeCategory` | `lineCount`, `estimatedTokenCount` |
| `SessionStarted` | — | — |
| `SessionEnded` | `commandSequence` | `sessionDurationMs`, `commandCount` |
| `ConversionAttempted` | `command`, `success` | `inputLines`, `durationMs`, `issueCount`, `unsupportedCount` |
| `ConversionGap` | `command`, `feature` | `line` |
| `SyntaxHelpQuery` | `command`, `resolvedCategory`, `isHit` | `featureLength`, `resultCount`, `matchedSectionCount` |
| `CompilationOutcome` | `command`, `success` | `errorCount`, `warningCount` |
| `HookAllow`, `HookBlock` | `command`, `hook`, `decision`, `fileExtension`, `agent` | — |

Global context fields are `schemaVersion`, `os`, `architecture`,
`dotnetVersion`, `calorVersion`, `semanticsVersion`, `operationId`, and
`codingAgent`. `operationId` is a new random 12-hex-character value per process
invocation and has no stable user or machine linkage. `codingAgent` is mapped
to a fixed vocabulary; unknown or multiple configured names become `none`.
Application Insights role and node tags are pinned to `calor-cli`.

## Never collected

Calor does not collect source/generated code, literals, identifiers, project
or file names, paths, command arguments, environment values, diagnostic text,
exception type/message/stack, machine/user IDs, hostnames, IP addresses, or
content hashes. Diagnostic data is code/category/count only. Unsupported
feature and help categories must come from compiler-owned fixed registries.
Unknown events, fields, enum values, or arbitrary caller properties cause the
whole affected event to be dropped.

## Retention

Calor does not implement a separate retention period in the client. Retention
is controlled by the configured Azure Application Insights workspace and the
operator's Azure/project policy. This repository does not currently publish a
project-specific guaranteed retention duration. Operators must configure,
document, and honor retention/deletion policy for their endpoint; users who do
not accept that provider/operator policy should leave telemetry disabled.

Schema changes must follow the
[telemetry privacy/security review checklist](security/telemetry-privacy-review.md).
