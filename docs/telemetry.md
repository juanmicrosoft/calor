# Telemetry

Calor's CLI telemetry is **opt-in and off by default**. A default invocation
of `calor` sends nothing, to anyone, ever.

## Enabling and disabling

| Action | How |
|---|---|
| Enable | `CALOR_TELEMETRY=1` (or `true`) in the environment |
| Force-disable (overrides enable) | `--no-telemetry` flag, or `CALOR_TELEMETRY_OPTOUT=1` |

## What is sent when enabled

All payloads are metadata-only. The exact inventory (audited against the code
in the #834 review — an item not on this list being transmitted is a bug):

- **Command names**, the sequence of commands in a session, exit codes, and
  wall-clock durations (total and per compiler phase).
- **Diagnostic codes** (e.g. `Calor0410`) with severity, per-code counts, and
  code co-occurrence pairs — **never diagnostic message text**, which can embed
  source fragments, identifiers, literals, and file paths.
- **Exception type names** (e.g. `System.IO.IOException`) — **never exception
  messages or stack traces**.
- **Aggregate input profiles**: line count and size bucket
  (small/medium/large/xlarge), estimated token count, and boolean feature flags
  (has-contracts, has-effects, has-modules). No source content.
- **Compile configuration**: which compiler flags/modes were active (a
  fixed-vocabulary flag map), compilation success, and error/warning counts.
- **Conversion/migration metadata**: unsupported-feature names from the
  compiler's own fixed registry, with counts and line numbers.
- **Help-query shape**: query **length** and hit/miss for `calor_help` lookups,
  plus matched section titles from the compiler's own documentation — never the
  query text itself.
- **Hook decisions** (`calor hook` agent-integration events): the hook name,
  allow/block decision, file extension (`.calr`/`.cs`), and agent name.
- **Session metadata**: a random per-invocation operation ID (not tied to
  machine or user identity), OS description and process architecture as
  reported by .NET, the .NET, Calor, and Calor-semantics versions, and the
  coding-agent name when one identifies itself (e.g. `claude-code`). The
  Application Insights cloud role AND internal node name are pinned to the
  constant `calor-cli` — the SDK's defaults would transmit your machine's
  hostname through either tag, and both are explicitly scrubbed (pinned by a
  serialization-level test).

## What is never sent

Source code, file paths, file names, identifiers, diagnostic messages,
exception messages, stack traces, environment variables, machine hostnames, or
anything derived from the content of your files beyond the aggregate profile
above — including content hashes: earlier builds sent SHA hashes of the input
source and generated output for determinism tracking; those enable exact-file
identification and were removed in the same change that made telemetry opt-in.

## Where it goes

Enabled telemetry is sent to the Calor project's Azure Application Insights
instance. The connection string is visible in
`src/Calor.Compiler/Telemetry/CalorTelemetry.cs`.

## History

Before v0.11 (W1 Slice 2, issue #792), telemetry was **default-on** and sent
raw diagnostic messages and full exception payloads. That posture was reversed:
opt-in default, stripped payloads, and this document. If you ran an earlier
version without `CALOR_TELEMETRY_OPTOUT=1`, those versions did transmit
diagnostics as described in the issue.
