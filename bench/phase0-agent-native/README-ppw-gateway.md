# Request-reserving PP-W gateway (#1406)

This implementation replaces the unknown per-invocation CLI estimate with a
positive, request-level admission calculation. It does not promise that the
entire 444-slot pilot will fit the authorized ceiling. Historical spending
receipts and the estimate-only adapter remain separate.

**#1436 operational hold:** the actual `w-rows-pilot-gateway-002` continuation
at commit `0b87d52b92c07e0377f4948102f467b890107df5` halted with two unknown
request charges. Their full $51.04 reservation remains unresolved exposure,
not verified spending. The commands and registrations below are historical
authority, not permission to repeat the already applied zero-request recovery
or to restart collection with this changed source. No new execution profile or
financial disposition is registered by the diagnostic changes.

**Integration status:** the deterministic HTTP positive path and local kernel/SDK
canaries exist. Independent no-forward diagnostic-client transport and protected
control probes succeeded. The author-run shared registered-argument probe also
reached the gateway with two authenticated Opus 4.8 requests; their actual body
and capability sets pass the request-price contract. The server forwarded zero
requests. Actual visible and held-out controls passed under final containment.
The independent historical-traffic forecast and scoped AI methods review are
bound to the plan. The profile becomes operative on independently reviewed
merge; collection still requires the explicit operator invocation below.
The original additive 25-artifact execution
profile and 250-to-500-to-1000 financial lineage do not create a live ledger,
epoch or scientific outcome. Credential and model availability are not blockers.

### Independent registered-argument probe

Run the repository-owned no-forward launcher with the retained native client:

```bash
python3 bench/phase0-agent-native/probe-ppw-gateway.py \
  --client <absolute-path-to-retained-2.1.266> \
  --shell <absolute-path-to-registered-GNU-Bash> \
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

The #1432 startup regression uses the actual native client and frozen first-task
materialization with a deterministic in-process provider. It exercises a real
Bash edit, build, visible test, and private held-out grading. No provider inference
occurs. This exposed two additional startup gaps: native client and zsh temporary
files must stay inside the workspace, and login-shell startup must not reorder
`PATH` ahead of the test shim. The launcher now selects the already registered
GNU Bash through `CLAUDE_CODE_SHELL` and supplies a source-pinned `BASH_ENV` that
restores the instrument's tool path. These settings are process-local; no global
shell configuration changes or new network allowances are involved.

The SDK's exact `anthropic-dangerous-direct-browser-access: true` header is also
admitted unchanged. It is the SDK's browser-access opt-in, not a model or pricing
control ([pinned SDK source](https://github.com/anthropics/anthropic-sdk-typescript/blob/135f71e9297683e14614d4307081c0273ed0a09c/src/client.ts#L1577-L1580)).
Other values, duplicate headers, unknown provider headers, and capability
headers marked hop-by-hop remain rejected before reservation. Fixed diagnostic
codes preserve the original refusal without recording header values or prompts.

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

### Prospective failure diagnostics (#1436)

The original final HTTP 402 reports a later stopped-scope refusal. It does not
identify the cause of either unknown charge: that source discarded the original
transport and receipt-validation exceptions. Partial native response metadata
contains `inference_geo: "not_available"`, and a synthetic completed-receipt
control reproduces `unknown provider geography`. This is a compatibility lead,
not a recovered raw provider receipt or proof of both original failure causes.
The response-geography allowlist and all prices remain unchanged. No CLI usage
or prospective diagnostic is substituted for missing historical provider evidence.

Future gateway failures retain a `pp-w-provider-failure-v1` diagnostic inside
the protected `unknown-charge-retained` event, bound to its existing request ID.
The record contains a fixed refusal/exception category, processing phase,
numeric HTTP status if observed, a categorical content type and SSE lifecycle
booleans. These booleans describe the observer's prefix, not proven complete
inference. The phase records the operation attempted; it does not prove that
bytes reached the provider. No credentials, capability tokens, raw headers,
exception text, request bodies, model text or tool output enter this record.

The first known HTTP, encoding, parser or usage failure atomically retains the
full reservation and halts new admission, before draining any remaining bytes.
Later errors or a secondary 402 cannot replace that cause. Cleanup failure also
retains the reservation instead of skipping settlement. A client disconnect
has its own fixed protected stop code; even when complete provider usage can be
reconciled, the interrupted scope stays halted. Legacy unknown-charge events
without diagnostics remain unchanged, and no existing unknown request can be
reconciled by adding a diagnostic.

Both scheduled CLI attempts remain consumed. A1 is the previously accounted
invalid attempt. Per the [independent methods inspection](https://github.com/juanmicrosoft/calor/issues/1436#issuecomment-5631059582),
B1 is raw-invalid/censored but operationally unresolved, not an admitted ordinary
invalid terminal. Exactly 442 identities are untouched. This change does not
classify financial failures as ordinary attrition, replace attempts, change
denominators, authorize prefix adjudication or create a continuation path.
Any future financial/methodological disposition and source-profile supersession
require separate review and explicit operator action under the same $1,000 cap.

The collector binds every scheduled slot into the ledger. A **valid completion**
requires reconciled gateway traffic and a non-interrupted client exit record.
The #1434 terminal-attempt amendment separately accounts for registered invalid
attempts, including invalid attempts with no provider request. A protected
`attempt-start.json`, actual client exit, classified runner reason, reason-file
hashes, sealed-source inventory and `invalid-terminal.json` must agree. The ledger
records `slot-terminal-invalid`, never `slot-complete`, for these attempts.
Missing output, malformed agent results, missing transcripts and non-interrupted
client crashes with no observed work can qualify. API/price/policy/integrity
failures, interrupted clients and any unreconciled liability still halt.
An invalid marker alone cannot advance the schedule.
Even a pre-reservation ledger refusal durably halts with `REQUEST_RESERVATION`;
it cannot be relabeled as a zero-request invalid terminal.

Both terminal kinds consume the next registered identity exactly once. Invalid
attempts are not replaced or counted as zero-valued outcomes. Gateway invalid
records leave unavailable usage/grading fields null; historical non-gateway
placeholder records remain unchanged. A nonzero, non-interrupted exit with
observed work retains the existing eligibility rule.
Kernel evidence is written beside the private invocation context, not into
child-writable output. The collector checks its exact policy/client binding and
stores it in the protected ledger before publishing the output copy. Scope
completion requires the entire registered terminal-slot inventory.
Operational completion does not imply usable data: even a completely invalid
schedule has zero eligible observations and yields `UNIDENTIFIED`, not a null
effect or success. No new consecutive-invalid threshold is introduced; that
would require a separately reviewed change to the frozen stopping rules.

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
using the real frozen task inventory and preserved 25-file execution map.
All observations and the provider/compiler/OS boundaries are explicit synthetic
fixtures. Both frozen estimands remain exactly one half in that fixture; the
reported decision remains `SYNTHETIC_ONLY`.
Its recovery case also executes all 443 continuation slots under the new 30-file
profile and sends the resulting archive to the registered adjudicator. The
original invalid launch remains separate: 443 request-bearing completed slots,
444 accounted slots, and no replacement. The fixture models post-apply accounting
in a temporary ledger; separate recovery tests exercise atomic apply itself.
An additional mixed fixture includes zero-request and reconciled-request invalid
terminals, a zero-eligible cell, and read-only analysis after archive relocation.
These are prescribed engineering cases, not pilot observations.

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

## Historical #1432 failure and applied #1434 continuation

The commands in this historical section describe the already-applied transition.
They must not be reused for the later two-request halt; see the distinct #1436
amendment below.

The first `w-rows-pilot-gateway-001` launch reached the retained client but
forwarded zero provider requests. It is still one attempted slot under the
registered counting rule ([methods clarification](https://github.com/juanmicrosoft/calor/issues/1432#issuecomment-5627948180)). Recovery therefore preserves
`C-001-quota-adapter/calor-permissive/1` as an invalid censored attempt and
continues only the remaining 443 slots. It does not replenish the sample,
increase the $1,000 total allowance, erase the original three-event ledger
prefix, or rewrite the failed archive.

The #1433 recovery proposal was never applied or collected. Its complete
`*-1432.json` records, original wire/startup evidence and analysis manifest remain
preserved. #1434 explicitly supersedes that proposal, retaining the same
prospective `w-rows-pilot-gateway-002`, original ledger and 443 remaining identities.
It does not create a third segment or a new sample.

The collector accepts only the separately reviewed
`gateway-execution-profile-1434.json`. That file
is intentionally not generated until
`gateway-evidence/gateway-native-wire-1434-evidence.json` exists and passes both
the price and shared production wire contracts. The source-bound
`gateway-native-startup-1434-evidence.json` must separately prove the scripted
native build and tests passed under the new trusted attempt lifecycle.
Registration generation is a pre-review
engineering step, not an operator action after recovery:

```bash
python3 bench/phase0-agent-native/ppw-gateway-register-terminal.py --write
```

During pre-merge review, `--write --refresh-unexecuted-proposal` explicitly
regenerates only the current #1434 proposal after source/evidence corrections.
It requires the exact original stopped-ledger bytes, unchanged failed archive,
no target epoch, and unchanged prior proposal documents. It cannot refresh after
recovery, reset accounting, or overwrite the immutable #1433 predecessors.
Changed proposal bytes still require final independent review and green CI.

The required evidence uses kind
`pp-w-engineering-no-forward-native-probe`, has `success: true`,
`upstreamRequests: 0`, `experimentalObservations: 0`, the retained client hash
and flags, and at least one observation. Every observation must set
`messagesPath`, `credentialHeaderPresent`, `priceContractAccepted`, and
`wireContractAccepted` to `true`, with `unclassifiedBetaCount: 0`.
`sourceHashes` must contain exactly:

- `probe-ppw-gateway.py`
- `ppw-gateway-client.py`
- `run-pair.sh`
- `ppw-gateway-budget.py`
- `ppw-budget-gateway.py`

The generator reads the fixed ledger in SQLite `mode=ro`, hashes the failed
archive opaquely, and creates no epoch, backup, recovery event, or provider
request. The public recovery command has no caller-selected profile, approval,
ledger, archive, or backup options:

```bash
python3 bench/phase0-agent-native/ppw-gateway-recover.py inspect
python3 bench/phase0-agent-native/ppw-gateway-recover.py apply \
  --confirmed-proof-sha256 <independently-reviewed-inspect-proof-sha256>
```

`inspect` is read-only. `apply` separately requires the exact registered proof
hash, creates a mode-0600 failed-ledger backup under the protected Git-common
directory, changes only the proven zero-request scope to the reviewed target
binding, and appends event 4. It never starts the collector. A later collector
start appends event 5 and archives the proof, opaque inventory, and original
three-event operational snapshot into the new epoch.
The preserved first result is an explicitly identified wrapper, linked to the
original raw-result, invocation, pins, registration and source hashes. It is not
presented as a newly run or newly inspected observation.

Progress distinguishes `accountedSlots`, `validCompletedSlots`,
`invalidTerminalSlots` and `requestBearingSlots`. Accounted slots include the
original invalid launch and subsequent valid or invalid terminals. Request
counts are financial traffic, not an attempted/unstarted-trial counter.

`registrations/ppw-rows-stage1/gateway-execution-profile.json` resolves the
preserved #1431 registration without rewriting it. The new collector archives the
resolved registration and every selected operational proof. The original
eleven-file pins, inactive fifteen-file projection and dated assessment remain
unchanged. Complete empirical archives have a real admission path; incomplete
accounting, mismatched proof files or any missing slot are refused.

The spending plan preserves the immutable proposed forecast, its generator and
the independent AI methods review under `gateway-forecast/`. Only the plan's
projection changes `proposed` to `registered`; the entire reviewed estimate,
method and limitations otherwise match exactly. Admission and archived analysis
require the exact three reviewed artifact hashes, not a nonblank method and a
small number. The collector copies these proofs into each archive, and analysis
does not execute the generator or fall back to canonical files when a proof is
missing. The preceding pending plan is preserved as an explicit supersession.

The historical 18-run traffic scenario scales to $900.36 for 444 slots, leaving
$99.64 under the $1,000 shared experiment ceiling. Its 1.1 pricing sensitivity
is $990.40, and 25% more traffic costs $1,125.45. These are conditional planning
scenarios, not a new-task mean, probability or completion guarantee. Missing
speed/geography evidence uses the implemented conservative 2x/1.1x settlement
multipliers; outstanding reservations and unknown liabilities further reduce
available headroom. No cheaper traffic is assumed for the redesigned tasks.

After the new wire/startup evidence and generated metadata pass independent
review and final-head CI, merge the PR. Only then may the operator separately
inspect and apply the recovery. The subsequent collector
invocation is:

```bash
CLAUDE_MODEL=claude-opus-4-8 python3 bench/phase0-agent-native/ppw-instrument.py run \
  --registration bench/phase0-agent-native/registrations/ppw-rows-stage1/gateway-execution-profile-1434.json \
  --tasks-root bench/phase0-agent-native/tasks/ppw-redesign \
  --compiler-root <exact-frozen-v0.18.0-checkout> \
  --epoch-id w-rows-pilot-gateway-002 --stage pilot \
  --confirm-paid-epoch
```

This command is documentation, not evidence that collection ran. The fixed
Git-common-directory ledger applies across worktrees/output roots and cannot be
reset by selecting another path. The authorization is $1,000 total for the
experiment, not an additional grant or a stage-2 allocation.

## #1436 permanent historical liability amendment

**Prospective engineering; not permission to apply or collect.** The operator
applied #1434 once and launched `w-rows-pilot-gateway-002` using merged source
`0b87d52b92c07e0377f4948102f467b890107df5`. That actual segment halted at
`C-001-quota-adapter/calor-strict/1` with two unknown reservations. The original
001 and actual 002 archives are immutable attempted-run evidence, not unexecuted
proposals.

The [explicit user grant](https://github.com/juanmicrosoft/calor/issues/1436#issuecomment-5633881736)
at `2026-09-11T07:39:10.192-04:00` authorizes one narrowly scoped amendment:
permanently retain both full upper bounds within the same $1,000 ceiling, keep
actual cost unknown, preserve both consumed attempts, and resume only the 442
untouched slots after independent approval. It does not authorize this engineering
agent to apply the transition, start collection, make a paid probe, or release.

| Historical local request | Permanent retained exposure |
| --- | ---: |
| `8d7fa7bf4a31f28ba7f7e8f6802781e8b6d8238fb38d3219` | $25.52 |
| `a24d9933f2d62c0c904eefdb81d9b592b4e36d30a7e577b3` | $25.52 |

The total **$51.04 remains permanently unavailable**, including after successful
operational completion. It is neither verified spend nor a fabricated maximum
debit. The historical rows remain `unknown`, with null usage, and original events
1 through 9 remain unchanged. No provider receipt, usage reconciliation, refund,
request deduplication, or financial reset is invented. Two native assistant
events sharing one provider locator do not merge these two local reservations.
The independent historical bound review does not establish actual charges or
approve the new implementation. This amends the predecessor's **all-reconciled
financial completion/admission rule for exactly these two rows**. It does not
change the scientific rules.

The distinct `historical-liability-disposition` event 10 binds the grant,
the exact stopped ledger (`464acefd37b96620aeab69c025cbb11703ca038d91eaa0d3d514af3d3c8a8990`),
the old backup (`0f32e092fbd760f7273405b4feda7e107b1a5630c9d98668b90d84a323048295`),
both archive inventories and attempt records, executed source identities,
prospective source/price/profile identities, and a fresh quiescence inspection.
Its separately named protected backup preserves the pre-disposition ledger.
The atomic operation appends the disposition and changes the scope binding to
`w-rows-pilot-gateway-003`; it does not start collection. A later, separate
collector start appends event 11.

This exception recognizes only the two exact historical liabilities. Every live
unknown, interrupted request, unpriced operation, policy/isolation refusal,
integrity failure, or unexplained liability still halts. A malformed, replayed,
rebound, or concurrent transition fails closed. The old zero-request recovery
has already been used and is inapplicable. Quiescence evidence must be stated
truthfully: a current process census is not a historical all-descendant receipt.
Missing historical process evidence must not be fabricated.

The current gate uses untruncated process arguments and same-user working-directory
and mapped-executable metadata. A native process whose kernel start predates the
immutable first collector start, and whose cwd/mapped files do not match protected
experiment paths, is not a descendant of these later per-attempt client launches.
This narrowly identifies pre-existing unrelated native sessions without killing
them; ambiguous or later native processes still block the operation. It relies on
the stated trusted-host/source and clock assumptions, not an invented historical
PID receipt. Every operator apply repeats the checks.

Collector startup compares the ledger's disposition with the exact registered
proof. Its owner handle binds that proof and scope, so later rehashing does not
silently change the running collector's authority. This is an integrity binding,
not a provider receipt or a cryptographic signature from an independent reviewer.

### Attempt and analysis lineage

The actual predecessor archives contain source identities in their original pins,
but no copies of the 25/30 pinned harness files. Their provenance therefore uses
the explicit `original-pins-only` kind, the unchanged source hashes and original
Git commits. It does not claim that source files were retrospectively archived.
The B1 raw reason remains the original `agent output matches error marker:
"api error"` classifier, not a new diagnosis of the provider's original failure.

Original A1 remains historically accounted invalid. Actual B1 keeps the frozen
runner's `invalid: true`, `censored: true`, null unavailable grading fields, and
API-error-marker reason. It is not recoded as a no-journal crash or an ordinary
zero-request invalid terminal. Only the explicit protected historical disposition
accounts B1. Its two unknown requests remain financially visible.

003 preserves contained copies of 001 and 002 evidence and creates two explicitly
identified historical wrappers. It never re-stamps the original records with
new source, inspection, usage, or lifecycle claims. Portable analysis verifies
the copies and their hashes without requiring the original physical worktree.
The old analysis registration is preserved by explicit supersession.
Proposal generation leaves its active bytes untouched; only explicit prospective
activation replaces the active wrapper and preserves the original file verbatim.

Only the original suffix beginning `C-002-shipping-quote/calor-permissive/1` may
run: **442 new identities plus two preserved identities equals 444**, with
three tasks, two arms, and 74 runs per task/arm. Run ordinal, task order, and A/B
order remain unchanged. There is no replacement, retry allocation, replenished
cohort, or reduced-442 forecast invented to claim affordability.

#### Current-budget planning (conditional, not observations)

The original forecast, proposal and independent forecast review remain immutable.
The plan's `forecast` field remains historical evidence, explicitly marked
`immutable-historical-reference-not-current-headroom`. Its old $99.64 headroom
does **not** account for the permanent encumbrance. A separate
`currentBudgetPlanning` projection conservatively uses the unchanged full-444
reference for remaining work:

| Conditional planning quantity | USD |
| --- | ---: |
| Same experiment cap | 1,000.00 |
| Permanent unknown encumbrance | 51.04 |
| Initially available for new requests | 948.96 |
| Unchanged full-444 historical reference | 900.36 |
| Combined planning reference | 951.40 |
| Current planning headroom | **48.60** |
| Pricing sensitivity plus encumbrance | 1,041.44 |
| Traffic stress plus encumbrance | 1,176.49 |

These are integer-microdollar calculations, not usage observations, a newly
approved forecast, a new task mean, or a completion guarantee. They assume neither
cheaper redesigned tasks nor a cheaper 442-slot forecast. Admission and portable
archive analysis both reject missing or changed projections.

Terminal accounting is not model success or scientific eligibility. Progress
separates accounted, valid-completed, invalid-terminal, request-bearing, and
remaining-nonterminal counts. A nonterminal count alone does not establish that
a slot is untouched. Final reports distinguish permanent historical unknown
exposure from live unknowns and continue to report actual historical cost as null.
They must never describe this as zero unknown usage or invoice-precise accounting.

`ppw-gateway-progress.py` opens only SQLite `mode=ro` with `query_only=ON` and
reports no task outcomes or credentials. It separates ordinary invalid terminals
from B1's historical raw-invalid disposition. Its remaining count is explicitly
nonterminal, not an assertion that every remaining identity is untouched:

```bash
python3 bench/phase0-agent-native/ppw-gateway-progress.py
```

It does not apply, reconcile, resume, adjudicate, or replenish anything. Native
engineering readiness reuses the immutable prebuilt runtime locators with
`PPW_INSPECTOR_READONLY=1`; it does not rebuild the historical caches.

The frozen 74-per-cell planned counts, eligibility denominators, weights,
unknown handling, and stopping rules are unchanged. Legitimate all-invalid
accounting may complete operationally while both estimands are `UNIDENTIFIED`,
with null estimates/half-widths and intervals `[0,1]`. It is not a scientific
null, zero-rate finding, planned precision, or stage-2 permission. No arbitrary
consecutive-invalid threshold is added, and known broken runtime readiness
remains a reason to hold rather than deliberately consume untouched identities.

### Prospective transport compatibility and operator gates

The original transport/receipt cause remains unproven. The preserved partial
native metadata demonstrated the `not_available` geography compatibility lead;
the final HTTP 402 was the stopped gateway's secondary refusal. #1437's protected,
privacy-minimized first-failure diagnostics remain in force.

The new price profile treats only the exact `not_available` response sentinel
like absent geography: retain the value and use the existing conservative US
1.1 multiplier. It does not infer global residency or a cheaper rate. The
[published residency contract](https://platform.claude.com/docs/en/manage-claude/data-residency)
documents global/US and the 1.1 maximum across token categories; it does not
document the sentinel's semantic origin. Arbitrary unfamiliar geography strings
remain refused. Historical price profiles retain their original stricter response
policy and are not retrospectively reinterpreted.

The [provider SDK's terminal delta schema](https://github.com/anthropics/anthropic-sdk-python/blob/main/src/anthropic/types/raw_message_delta_event.py)
declares nullable `container` and `stop_details`. Only absent/null values are
admitted; non-null server execution/refusal detail and unknown controls still
fail closed. Fresh guarded, no-forward native startup evidence must exercise
these exact response shapes, the retained executable, real native Bash build/test
tools, and unchanged containment. Synthetic readiness is not an empirical trial,
a paid availability probe, or a complete historical provider receipt.

#### Proposal → independent exact-subject reviews → metadata activation

`--contract` describes every input, phase, output and hash subject without reading
the ledger or archives. Proposal generation requires fresh source-bound wire and
native evidence plus typed indexes resolving **unchanged raw review artifacts**:

- `gateway-liability-bound-review-1436.json` points to the genuine historical
  audit at SHA-256 `a387cc8f7376eb48295f63ac87d3c38d85985ac2fc565c96c9982a4f86f4d1a5`.
  Its `APPROVE` is historical-bound only: no new implementation, human review or
  operator-execution approval. The missing actual final isolation artifact and
  trusted-host/provider-contract assumptions remain in that raw audit.
- `gateway-liability-methods-review-1436.json` points to the genuine draft review
  at SHA-256 `6d7f03c266ec74f37f4b44aefe5d4ab511774d10e7d0b771169dabc290948c04`.
  Its verdict remains `REQUEST_CHANGES`; it is description-only history, not
  final-record, new-financial-implementation or source approval.

Neither history index manufactures final approval. `--write` creates an honest
**unapproved proposal** for reviewers, not collection authority:

```bash
python3 bench/phase0-agent-native/ppw-gateway-register-disposition.py --contract
python3 bench/phase0-agent-native/ppw-gateway-register-disposition.py --write
```

The immutable proposal record set pins authorization, plan, profile, proof,
prices, inventories, stopped snapshot, evidence, historical lineage, forecast,
stage/model/source-inspection records and raw review history. It also pins the
exact source/test/document map. `sourceArtifactsSha256` hashes that map;
`recordSetSha256` hashes the record-set object excluding its own hash field.
Canonical encoding is sorted-key JSON with indent 2 and a final newline.
Raw reviews are always hashed as exact original bytes, never reserialized.

The record set excludes activation, final reviews/indexes and the active analysis
wrapper, avoiding a hash cycle. The immutable profile names the fixed
`gateway-disposition-activation-1436.json` basename, never a future hash. Its
bytes and proposal evidence do not change during activation. Refresh is allowed
only with `--write --refresh-unexecuted-proposal`, before activation/collection,
against the exact old ledger, backup and archive inventories. It never rewrites
the old active analysis or its preserved copy; changed proposal identities
invalidate any earlier final reviews.

Independent reviewers must produce three raw final reviews with exact subjects:
`registered-methods-final-records`, `new-financial-implementation`, and
`source-implementation`. For each, fixed files in `gateway-evidence/` are
`gateway-<subject>-1436-review.json` and `gateway-<subject>-1436-index.json`.
The strict raw schema is `pp-w-independent-disposition-final-review`:
`schemaVersion`, `kind`, `subject`, `verdict` (`APPROVE` or `REQUEST_CHANGES`),
`reviewer`, `binding`, `scope`, nonempty `findings`, and nonempty `limitations`.
The contract prints the exact nested fields. The binding names record-set and
source-map hashes, original ledger, grant, permanent retention, same cap, target
epoch, null historical actual cost and future-halt policy. The index has exactly
`schemaVersion`, `kind` (`pp-w-disposition-review-index`), `subject`, `artifact`
(`path`/byte `sha256`), and verbatim `verdict`. Additional fields, generic scope
strings, wrong subjects/hashes, description-only approvals and pending verdicts
cannot satisfy final approval.

Only after those three genuine exact-subject `APPROVE` records:

```bash
python3 bench/phase0-agent-native/ppw-gateway-register-disposition.py --activate \
  --confirmed-record-set-sha256 <exact-reviewed-recordSetSha256>
```

Activation writes **prospective metadata only**. It pins exact final indexes and
raw reviews, then explicitly supersedes the old analysis registration. It never
applies the ledger transition, creates an epoch or launches a collector.
Every live resolver, funding/admission path and canonical operator inspect/apply
requires this activation. Lower-level financial-core inspection remains available
to the proposal builder read-only. The collector archives every critical record,
typed index, raw review, activation and reviewed source file; analysis refuses
missing archival proofs instead of falling back to canonical physical files.

Content hashes in committed reviews avoid a self-referential final Git commit
hash. A final-head GitHub review, CI/merge and separate parent/operator decision
remain external manual gates, explicitly **not machine-attested** by activation.
No final approval is supplied by this implementation.

The distinct operator interface has no caller-selected ledger or archive:

```bash
python3 bench/phase0-agent-native/ppw-gateway-dispose.py inspect
python3 bench/phase0-agent-native/ppw-gateway-dispose.py apply \
  --confirmed-proof-sha256 <final-independently-reviewed-disposition-proof-sha256>
```

These commands are documentation, not an executed transition. Only the parent
operator may apply the exact final reviewed proof once, after independent bound,
methods and final-source approval, green CI, merge, and a fresh readiness check.
The final handoff must name the exact merge, registered profile, proof, immutable
predecessors, and 442-only collector command. No automatic stage-2 transition or
fresh budget follows.
