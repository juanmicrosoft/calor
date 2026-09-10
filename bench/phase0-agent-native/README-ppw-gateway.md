# Request-reserving PP-W gateway (#1406)

This implementation replaces the unknown per-invocation CLI estimate with a
positive, request-level admission calculation. It does not promise that the
entire 444-slot pilot will fit the authorized ceiling. Historical spending
receipts and the estimate-only adapter remain separate.

**Integration status:** the deterministic HTTP positive path and local kernel/SDK
canaries exist. The independent retained-client transport/IPC probe, complete
operational source/profile/funding supersession, and final review remain pending.
The capability list is not yet claimed to admit the pinned client's full request
set, including its exact OAuth capability. No experimental invocation, live
ledger, or scientific outcome is created by these fixtures.

## One request must mean one model iteration

`ppw-gateway-budget.py::price_contract` binds the model, limits, prices, capability
list and primary source URLs. The supported model is `claude-opus-4-8`. The larger
binary interpretations of the documented 1M context and 128K synchronous output
limits are used: 1,048,576 and 131,072 tokens.

Before opening an upstream connection, the controller reserves the full context
at the highest admitted input-category rate, plus the request's `max_tokens` at
the highest admitted output rate. Rates cover fast mode, one-hour cache writes
and the 1.1x US-residency multiplier. Every attempt, including a client retry or
helper request, requires its own durable reservation.

That formula is **not** a bound for arbitrary Messages API features:

- [Server-side compaction](https://platform.claude.com/docs/en/build-with-claude/compaction)
  can generate a paid summary and then continue within one HTTP request.
  `compact_20260112` and its beta capability are rejected before forwarding,
  including `pause_after_compaction` requests.
- [Server-side fallback](https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback)
  can run additional models within one HTTP request. The `fallbacks` field and
  all fallback capabilities are rejected, even when a caller names the same
  model again.
- Server tools, server tool callers, unregistered models, unknown top-level or
  admitted control-object fields, unsupported API versions and unknown beta
  capabilities are rejected. There is no silent model remapping, parameter
  stripping or assumption that an unpriced server operation is free.

The admitted context edits clear old thinking/tool content before inference;
they do not run a summarizing model. Adding another operation or beta requires
reviewing its financial and method implications and changing the source-bound
price contract. An unexpected response iteration, compaction/fallback content
block, changed model, incomplete usage or unrecognized usage component retains
the entire reservation and stops admission. This response check is a failure
detector, **not** a substitute for pre-forward request rejection.

## Transport and accounting

`ppw-budget-gateway.py` exposes one capability-protected loopback endpoint per
slot. The production upstream is fixed HTTPS `api.anthropic.com:443`; there are
no alternate upstream settings, redirect following, automatic proxy retries or
environment-proxy routing. Test connection factories point only to deterministic
local servers.

Accepted body bytes, `anthropic-version`, `anthropic-beta` and authentication
headers are forwarded unchanged. Stream bytes and pings are relayed immediately.
The [general gateway compatibility guide](https://code.claude.com/docs/en/llm-gateway-protocol)
recommends open capability lists for evolving clients. This frozen financial
adapter deliberately rejects unsupported requests instead of silently stripping
capabilities; it is not a general-purpose drop-in gateway for arbitrary releases.
Credentials and request content remain in memory and are not written to the
ledger or diagnostic logs.

SQLite immediate transactions reserve before connection/forwarding. Only complete
provider category counts permit a refund, with conservative prices when a billing
modifier is absent. CLI cost estimates never reconcile liability. Missing,
truncated, interrupted or ambiguous charges remain reserved; no expiry or
automatic restart resets them.

Budget exhaustion produces `INCOMPLETE_BUDGET`, not a reduced-N experiment, a
scientific null or a stage-2 decision. A complete schedule is still required for
adjudication. Financial authorization, operational admission and scientific
results remain distinct.

## Isolation boundary

The launcher isolates the entire runner, not only the client: generated-code
builds and held-out execution are descendants too. The retained client and
existing Bash 5 shell are pinned by bytes; no global tool or billing settings
are changed. Gateway state remains outside writable work/output roots.

The current policy restricts networking, protected state access, outside signals,
process metadata and task ports, Mach lookup/registration, POSIX IPC, Apple Events
and Launch Services. The implemented kernel probes establish only their stated
controls; they are not a complete proof against every IPC/delegation route.
The independent pinned-client probe must establish compatibility and any necessary
allowances before operational registration. Saved-login execution must not use
`--bare`, replace credentials, or disable protocol features merely to pass a probe.
