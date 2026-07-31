# Telemetry

Calor's CLI telemetry is **opt-in and off by default**. A default invocation
of `calor` sends nothing, to anyone, ever.

## Enabling and disabling

| Action | How |
|---|---|
| Enable | `CALOR_TELEMETRY=1` (or `true`) in the environment |
| Force-disable (overrides enable) | `--no-telemetry` flag, or `CALOR_TELEMETRY_OPTOUT=1` |

## What is sent when enabled

All payloads are metadata-only. The exact inventory:

- **Command names** and exit codes (`compile`, `verify`, …) and wall-clock durations.
- **Diagnostic codes** (e.g. `Calor0410`) with severity — **never diagnostic
  message text**, which can embed source fragments, identifiers, literals, and
  file paths.
- **Exception type names** (e.g. `System.IO.IOException`) — **never exception
  messages or stack traces**.
- **Aggregate input profiles**: line-count bucket (small/medium/large/xlarge),
  estimated token count, and boolean feature flags (has-contracts, has-effects,
  has-modules). No source content.
- **Session metadata**: a random per-invocation operation ID (not tied to
  machine or user identity), OS platform, compiler version.

## What is never sent

Source code, file paths, file names, identifiers, diagnostic messages,
exception messages, stack traces, environment variables, or anything derived
from the content of your files beyond the aggregate profile above.

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
