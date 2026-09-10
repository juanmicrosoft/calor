# Request-reserving PP-W gateway (#1406)

This implementation replaces the unknown per-invocation CLI estimate with a
positive, request-level admission calculation. It does not promise that the
entire 444-slot pilot will fit the authorized ceiling. Historical spending
receipts and the estimate-only adapter remain separate.

**Integration status:** the deterministic HTTP positive path and local kernel/SDK
canaries exist. Independent no-forward diagnostic-client transport and protected
control probes succeeded. Registered-argument/capability parity, the remaining
IPC/socket checks, complete operational source/profile/funding supersession, and
final review remain pending. Credential and model availability are not blockers.
The capability list is not yet claimed to admit the pinned client's full request
set, including its exact OAuth capability. No experimental invocation, live
ledger, or scientific outcome is created by these fixtures.

### Independent registered-argument probe

Run the repository-owned no-forward launcher with the retained native client:

```bash
python3 bench/phase0-agent-native/probe-ppw-gateway.py \
  --client <absolute-path-to-retained-2.1.266> \
  --scratch-root <existing-private-scratch-directory>
```

The probe and gateway-mode runner consume the same client-flag helper and
environment builder. It uses the current kernel policy, capability-prefixed
loopback URL and registered model, with no safe-mode, tools-none, bare,
settings-source or session-persistence override. It creates no spending ledger
or epoch. Its HTTP server unconditionally rejects requests and has no provider
forwarding implementation. Native stdout/stderr and request bodies stay in
memory; the JSON report contains only sanitized capability/shape data and local
control results. This tests native compatibility, not a complete isolation proof.

The real frozen-task null-agent exercise exposed VSTest's attempted loopback
listener. Gateway mode instead runs the same xUnit v2 discovery/execution engine
in-process, using the utility bundled by the already registered adapter 2.8.2
and the unchanged xUnit 2.9.2 test framework. Both policy arms pass the quota
task's four held-out cases under the unchanged network policy. The launcher
explicitly supplies the existing read-only NuGet cache; it does not install
packages or add a loopback exception.

`ppw-test-host.py --prepare` builds the host before isolation and produces its
runtime/dependency manifest. The spending plan pins that actual manifest.
Gateway-mode visible and hidden `dotnet test` calls use this host. It retains
xUnit theories, async execution, fixtures and configuration, but not VSTest's
separate testhost, data collectors, TRX output or crash recovery. Unsupported
test CLI options/loggers fail explicitly rather than being silently ignored.
Named results are emitted only after complete execution; discovery/cleanup
errors have no success-shaped summary. Historical non-gateway execution still
uses VSTest unchanged.

## One request must mean one model iteration

`ppw-gateway-budget.py::price_contract` binds the model, limits, prices, capability
list and primary source URLs. The supported model is `claude-opus-4-8`.
An independent metadata-only preflight on September 10, 2026 returned HTTP 200
from `GET /v1/models/claude-opus-4-8`, confirming the exact model ID,
`max_input_tokens: 1000000` and `max_tokens: 128000`.
The controller uses those exact limits, rather than binary approximations.
See the [Models API reference](https://platform.claude.com/docs/en/api/models/retrieve)
and the [Messages API definition](https://platform.claude.com/docs/en/api/messages/create)
of `max_tokens` as an absolute output maximum. The reported preflight made one
metadata GET and forwarded no upstream inference. No credentials or raw
authentication payloads are part of this record.

Before opening an upstream connection, the controller reserves the full context
at the highest admitted input-category rate, plus the request's `max_tokens` at
the highest admitted output rate. Rates cover fast mode, one-hour cache writes
and the 1.1x US-residency multiplier. Every attempt, including a client retry or
helper request, requires its own durable reservation.

The [service-tier reference](https://platform.claude.com/docs/en/api/service-tiers)
describes `auto` using existing Priority capacity when available. It does not
document a separate per-token Priority multiplier or a Messages operation that
purchases a capacity commitment. The controller applies the published token
categories rather than inventing a premium. A request explicitly selecting
`standard_only` must report standard-tier usage to reconcile.

This is a bound for the documented first-party per-request usage charges, not
an account-wide invoice limit. The adapter cannot establish private contractual
economics, allocate unrelated existing commitments, cover unrelated account
activity, or control taxes and fees absent from the published price contract.
Those are not silently priced as zero or inferred from CLI estimates.

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

The collector binds every scheduled slot into the ledger. A slot cannot complete
without reconciled gateway traffic and a non-interrupted client exit record.
Kernel evidence is written beside the private invocation context, not into
child-writable output. The collector checks its exact policy/client binding and
stores it in the protected ledger before publishing the output copy. Scope
completion requires the entire registered slot inventory.

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
and Launch Services. The Mach lookup allowlist is limited to `securityd.xpc`,
`SecurityServer`, `cfprefsd.agent`, `cfprefsd.daemon` and `logd` in the
`com.apple` namespace. No `trustd.agent` delegation is admitted.
The implemented kernel probes establish only their stated
controls; they are not a complete proof against every IPC/delegation route.
The independent pinned-client probe must establish compatibility and any necessary
allowances before operational registration. Saved-login execution must not use
`--bare`, replace credentials, or disable protocol features merely to pass a probe.

`test_ppw_gateway_collection.py` exercises all 444 scheduled slots through the
real collector, loopback gateway, request ledger and descriptive stage analyzer.
The provider, client/OS boundary, compiler product and authorization are explicit
synthetic doubles; this is not empirical collection or an independent kernel
proof. Its negative controls prevent budget-stopped, missing-request, interrupted
and unknown-charge collections from reaching stage analysis.
