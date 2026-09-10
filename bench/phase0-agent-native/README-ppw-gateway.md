# Request-reserving PP-W gateway (#1406)

This implementation replaces the unknown per-invocation CLI estimate with a
positive, request-level admission calculation. It does not promise that the
entire 444-slot pilot will fit the authorized ceiling. Historical spending
receipts and the estimate-only adapter remain separate.

**Integration status:** the deterministic HTTP positive path and local kernel/SDK
canaries exist. Independent no-forward diagnostic-client transport and protected
control probes succeeded. The author-run shared registered-argument probe also
reached the gateway with two authenticated Opus 4.8 requests; their actual body
and capability sets pass the request-price contract. The server forwarded zero
requests. Remaining independent IPC/socket review, the parent-owned full-pilot
forecast and final review are still pending. The additive 25-artifact execution
profile and 250-to-500-to-1000 financial lineage do not create a live ledger,
epoch or scientific outcome. Credential and model availability are not blockers.

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

The trusted observer records framework counts from one nonce-bound xUnit receipt,
not arbitrary `Passed:` text printed by candidate code. Missing, duplicate or
inconsistent receipts fail closed. This framing prevents ordinary console output
from becoming a result; it is not a claim of isolation against hostile CLR
reflection inside the test-host process.

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

The native client advertises `advisor-tool-2026-03-01` even when its tool list
contains no advisor. That header may pass, but the actual `advisor_20260301`
tool definition is rejected. The provider's [advisor documentation](https://platform.claude.com/docs/en/agents-and-tools/tool-use/advisor-tool)
requires that explicit tool definition for the extra server-side model pass.
URL-backed image/document sources are also rejected: the provider must not
become an arbitrary remote-fetch relay around client network isolation.

## Transport and accounting

`ppw-budget-gateway.py` exposes one capability-protected loopback endpoint per
slot. The production upstream is fixed HTTPS `api.anthropic.com:443`; there are
no alternate upstream settings, redirect following, automatic proxy retries or
environment-proxy routing. Test connection factories point only to deterministic
local servers.

Accepted body bytes, `anthropic-version`, `anthropic-beta` and authentication
headers are forwarded unchanged. Stream bytes and pings are relayed immediately.
The gateway requests identity content encoding so usage can be observed without
altering the relayed entity bytes; it never negotiates an unimplemented decoder.
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

The trusted runner owns authoritative output. It launches the native client and
its tools in a child policy that permits writes only to their workspace and
networking only to their model gateway capability. A separate typed observation
capability can request observations but cannot register, seal or finalize a run.
The retained client and existing Bash 5 shell are pinned by bytes; no global tool
or billing settings are changed.

Generated source builds and hidden-test execution use private snapshots with no
network and no writes to the original agent workspace or authoritative output.
Candidate builds finish before hidden tests are copied. Hidden-test compilation
references the prebuilt candidate DLL, not candidate-controlled MSBuild targets;
test sources stay read-only. A child can read only the private artifacts required
for its phase, not unrelated observation directories. Source sealing revokes new
model requests, terminates remaining process-group descendants and archives a
stable declared-source copy before final grading.

Trusted preflight also discovers every registered worktree for the harness and
compiler repositories, their common Git storage and configured alternate object
stores. Child policies deny hidden task/evidence categories in those worktrees
and the object stores themselves, preventing `git show` or another checkout from
bypassing a task-directory denial. Discovery errors fail closed. This covers
known registered storage at discovery time, not arbitrary unregistered clones
or files introduced elsewhere afterward.

The current policy restricts networking, protected state access, outside signals,
process metadata and task ports, Mach lookup/registration, POSIX IPC, Apple Events
and Launch Services. The Mach lookup allowlist is limited to `securityd.xpc`,
`SecurityServer`, `cfprefsd.agent`, `cfprefsd.daemon` and `logd` in the
`com.apple` namespace. No `trustd.agent` delegation is admitted.
The gateway allowance is IPv4-only: a distinct IPv6 listener on the same port is
not an alternate route. Preflight tests this with a real IPv6 listener, as well
as a Unix socket, a delegated write to a disposable preferences sentinel, and
hardlink writes to readable outside source. Only newly created sentinels are
used; no user preferences or global settings are modified.
Python, its TLS/SQLite native extensions, .NET executable/runtime versions,
the operating-system release, Bash and the prebuilt test host are also bound
to the execution plan and checked around each invocation.
The source inspector is rebuilt from trusted sources before accepting a populated
cache, and its executable plus all DLL/JSON runtime dependencies are byte-bound.
New inspection certificates preserve the original source-analysis results while
explicitly superseding their execution-runtime identity; original certificates
are not rewritten.
Read-only archive analysis validates those recorded identities without requiring
the collecting machine's runtime paths to exist. Live admission and execution
still validate every local runtime byte.
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

`test_ppw_gateway_registered_collection.py` additionally sends that actual
collector's complete 444-slot output directly to the registered adjudicator
using the real frozen task inventory and current 25-file execution map.
All observations and the provider/compiler/OS boundaries are explicit synthetic
fixtures. Both frozen estimands remain exactly one half in that fixture; the
reported decision remains `SYNTHETIC_ONLY`.

`test_ppw_gateway_frozen_controls.py` additionally exercises the real retained
compiler, source-fragment assembly, visible shim and private observer against all
18 null-agent controls. Six honest cases pass four held-out tests; six permissive
effect controls build and fail held-out tests; six strict effect controls fail
compilation. Every case reaches registration, both build observations, sealing, source
inspection and final grading without a model or provider request. The expanded
fixture also invokes the actual materialized `Smoke.csproj` through the
production `dotnet` shim inside the final client policy, not a stand-in shim.
All 18 cases execute their 8/8/10 visible xUnit cases and record a third, `test`
observation; correct numeric controls pass and incorrect numeric controls fail.
Neither held-out test output nor the private framework receipt appears in
visible output.

The unchanged smoke project references the last successfully built `Src.dll`.
After a strict compilation failure, visible tests can therefore execute that
older starter assembly. The regression records this byte identity explicitly;
it does not mistake those tests for a successful new build. Authoritative final
grading still builds a fresh private snapshot and preserves the strict
nonbuilding outcome.

The opt-in
`PPW_RUN_FROZEN_GATEWAY_CONTROLS=1` run requires macOS and the retained product;
other environments skip it explicitly. Earlier whole-run evidence is preserved
in separate `pre-boundary-1406` records, not presented as proof of the new policy.

## Operational entrypoint

`registrations/ppw-rows-stage1/gateway-execution-profile.json` resolves the
preserved registration without rewriting it. The new collector archives the
resolved registration and every selected operational proof. The original
eleven-file pins, inactive fifteen-file projection and dated assessment remain
unchanged. Complete empirical archives have a real admission path; incomplete
accounting, mismatched proof files or any missing slot are refused.

The checked-in spending plan currently names the outstanding parent-owned
forecast explicitly. Admission refuses that pending forecast before creating
an epoch or ledger. This is not an unknown price bound: the per-request bound is
implemented and positive. The forecast must describe all 444 slots, its
no-charge method and point cost estimate; it must not claim guaranteed completion.

After the real forecast and final independent review are registered and merged,
the invocation is:

```bash
CLAUDE_MODEL=claude-opus-4-8 python3 bench/phase0-agent-native/ppw-instrument.py run \
  --registration bench/phase0-agent-native/registrations/ppw-rows-stage1/gateway-execution-profile.json \
  --tasks-root bench/phase0-agent-native/tasks/ppw-redesign \
  --compiler-root <exact-frozen-v0.18.0-checkout> \
  --epoch-id w-rows-pilot-gateway-001 --stage pilot \
  --confirm-paid-epoch
```

This command is documentation, not evidence that collection ran. The fixed
Git-common-directory ledger applies across worktrees/output roots and cannot be
reset by selecting another path. The authorization is $1,000 total for the
experiment, not an additional grant or a stage-2 allocation.
