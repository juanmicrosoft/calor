# Loop telemetry schema v2 (loop plan D4.1)

Per-iteration JSONL records for the agent edit–feedback loop, extending the
existing `journal.jsonl` stream that the `dotnet` PATH shim in `run-pair.sh`
emits. One record per harness-observed `build|test|run` invocation. The v1
record (`{ts, cmd, exit, edited, heldout_pass, heldout_fail}`) is a strict
subset; consumers key on `schema` to distinguish.

Machine-validatable schema: `loop-telemetry.schema.json` (same directory).
Conventions follow `docs/plans/agent-native-gates.md` §2: an **iteration** is a
build-or-test invocation following ≥1 workspace edit; iterations-to-green
censors at budget+1; censored fractions are reported per arm.

## Record fields

| Field | Type | Notes |
|:------|:-----|:------|
| `schema` | `"loop-telemetry/2"` | discriminator; absent on v1 records |
| `ts` | string (ISO-8601 UTC) | invocation start |
| `pair` | string | pair/task id (`pair.json` `id`) |
| `arm` | string | full arm label incl. variants (e.g. `calor`, `calor+exemplar`, `calor+mcp-file`) |
| `run` | int | run ordinal within the epoch |
| `iteration` | int \| null | 1-based ordinal **among edited invocations**; null when `edited` is false (unedited rebuilds are observations, not iterations, per gates §2) |
| `cmd` | string | `build` \| `test` \| `run` |
| `exit` | int | agent-visible exit code of the invocation |
| `edited` | bool | src-tree hash changed since previous invocation |
| `feedback_latency_ms` | int | wall time from invocation start to agent-visible completion (the loop's edit→feedback latency; M-L1's agent-run analogue) |
| `heldout_pass` / `heldout_fail` | int | silent held-out suite result (never surfaced to the agent) |
| `src_tree_hash` | string | content hash of the src tree at this invocation; doubles as the **snapshot reference** — when snapshotting is enabled the tree is archived under `snapshots/<hash>/` for reject-replay (D4.6) |
| `edit_mechanism` | `"raw"` \| `"mcp-file"` \| `"mcp-node"` \| `"unknown"` | how the edit since the last invocation was made. In the baseline harness (no MCP loop) all edits are `raw`; populated from the arm constraint when one is active, else `unknown` if attribution is impossible |
| `edit_target_ids` | string[] | declaration IDs whose content changed since the previous invocation (raw-file node attribution: per-file `calor ids index` diff + changed-file mapping). Empty when no `.calr` changed or IDs absent |
| `diagnostics` | array of `{code, declarationId?}` | compiler diagnostics of this invocation's build, from the envelope (`--format json`); truncated at 50 entries with `diagnostics_truncated: true` |
| `envelope_valid` | bool \| null | whether the build's JSON output validated against envelope schema v1.1; null when the arm doesn't produce envelopes (C# arm) |
| `apply_verdict` | string \| null | reserved for the WS2 transactional apply path (`applied` \| `rejected`); null in the baseline harness, which has no apply/reject mechanism |
| `rejected_edit` | object \| null | reserved (D4.6 replay): `{snapshotRef, payloadPath}` for a rejected edit archived under `rejects/`; null in the baseline harness |

## Field semantics that matter for the metrics

- **M-L3 (diagnostic actionability)** joins `diagnostics[].declarationId` of
  iteration *n* against `edit_target_ids` of iteration *n+1*. Raw-file edits
  are attributed via `edit_target_ids`, so the denominator does not narrow to
  MCP-mediated edits (plan review m5).
- **M-L2 (first-apply validity)** splits on `edit_mechanism`.
- **Latency** is agent-visible wall time — it includes the silent held-out run
  only if the held-out run delays the agent's prompt return (it does, in the
  current shim design; the shim records the split when it can).
- `apply_verdict` / `rejected_edit` being reserved-null in the baseline is
  intentional and honest: the baseline (WS1-only build, tag
  `loop-baseline-ws1`) has no apply path, and the schema must be frozen
  **before** the treatment build exists so both arms emit identical shapes.

## Compatibility

`extract_metrics` in `run-pair.sh` computes `iterations`, `iterationsToGreen`,
`censored`, and `escapedBugs` from the same fields as v1 (`edited`,
`heldout_fail`) — v2 is additive. Epoch `pins.json` gains
`telemetrySchema: "loop-telemetry/2"` so mixed-schema epochs are detectable.
