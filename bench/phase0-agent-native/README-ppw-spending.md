# PP-W pilot spending enforcement (#1378)

This document records the estimate-only adapter and its original authorization
boundary. The prospective request-level adapter is documented separately in
[Request-reserving PP-W gateway (#1406)](README-ppw-gateway.md); it does not rewrite
the historical receipts or imply that experimental collection has begun.

The user supplied a **$250 total ceiling** and accepted the registered stopping
rules and publication of negative/null results. The unresolved scope placeholder
is interpreted narrowly: **pilot only**, not stage 2. Formal approval and
feasibility belong to #1259. This implementation does not create or enlarge that
authorization.

## Current execution status: refused, not a claimed hard cap

The installed Claude Code 2.1.266 supports `--max-budget-usd` with `--print`.
The supported flag is wired into the PP-W invocation path. It is **not sufficient
to admit actual collection**:

- [Official cost documentation](https://code.claude.com/docs/en/costs) says the
  CLI computes costs locally from token counts, ordinarily at list prices.
  Those figures are estimates; authoritative billing is in the Claude Console.
  Subscription session costs are not subscription invoices. Managed pricing
  can also change what the CLI displays without changing provider charges.
- The [CLI reference](https://code.claude.com/docs/en/cli-reference) documents
  `--max-budget-usd`, but supplies no verified worst-case overshoot bound covering
  all in-flight requests, retries, descendants, accounting delays, and invoices.
- Vendor-hosted reports
  [#85400](https://github.com/anthropics/claude-code/issues/85400) and
  [#82104](https://github.com/anthropics/claude-code/issues/82104) describe
  subscription-estimate confusion and child activity after a parent stops.
  These are user reports about their stated environments, **not reproductions
  or proof of a defect in our installed version**.

Consequently the implemented capability assessment returns **unknown** for the
hard maximum liability of this control. The collector refuses before any
product/client command, output directory, or model invocation. A JSON assertion
such as `hardCap: true`, a hashed opaque document, a confirmation flag, or a
subscription cannot override that decision. No provider setting, invoice cap,
or maximum overshoot is invented.

Both list-price-equivalent **study cost and actual spending** are conservatively
within scope. Subscription access does not waive the ceiling.
`$250 / 444` is arithmetic, not a feasibility demonstration or permission to
truncate each run to an invented allowance.

An actual future bounded provider adapter requires a reviewed implementation,
trustworthy enforcement evidence, and an explicit prospective registration.
It must bound both cost bases over complete invocations, including remote work
that can outlive a local process. This PR does not supply such a provider.

## Supplied-control contract

The selected pilot stage references a `spendingPlan` using relative `path` and
SHA-256, like its other admission evidence. It also references an
`instrumentAmendment` by relative path and SHA-256. That amendment's complete
`replacementHarnessArtifacts` must match the current collector, shell runner,
spending helper, capture/inspection tools, templates and native inspector.
The amendment reference enters the protocol digest; every ticket claim rechecks
the source hashes before returning a client threshold. The plan contains:

- `schemaVersion: 1`, `kind: pp-w-pilot-spending-plan`, exact `stage` and `epochId`;
- `authorizationSha256`, the supplied `ceilingUsd`, and
  `costBasis: both-list-price-study-cost-and-actual-spend`;
- `protocolSha256`, computed by `ppw-spending.py::protocol_identity` over the
  unchanged task/source inventory, compiler, policies, N, model/client and
  method/model evidence references;
- `plannedInvocations`, equal to the complete registered inventory: currently
  74 × 3 tasks × 2 policies = **444**, not a budget-sized subset;
- one pinned absolute shared `ledgerPath`, used across output roots and workers.
  It must equal the supplied authorization's `spendingLedgerPath`; changing only
  a plan or output root cannot reset the authorization's accounting scope;
- `clientControl: {kind: claude-code-max-budget-usd, limitUsd: <supplied value>}`.
  The implementation chooses no experimental per-invocation allowance.

Admitting a bounded adapter additionally requires its verified maximum liability
for **every unchanged planned invocation** to fit the supplied aggregate ceiling.
The real Claude control fails earlier because that maximum is unknown. A
spending plan is not itself provider-enforcement evidence.

The new runner/control files change the instrument fingerprint. Existing
prospective harness and analysis pins must receive an explicit reviewed
supersession/amendment; they are not silently rewritten here. Task source,
models, N, ordinary iteration/time limits and scientific stopping rules remain
unchanged.

## Durable accounting

The SQLite ledger uses immediate transactions and full synchronous durability.
It binds the scope, plan, ceiling, full slot inventory and invocation identities.
The trusted deployment must protect and share the **same** pinned ledger file;
this local accounting mechanism does not prevent a privileged operator from
deleting files, cloning account credentials, or running unrelated clients.
It is not a substitute for the missing provider-side bound.
The ledger class itself is an internal accounting primitive, not an approval
authority: constructing an arbitrary admission dictionary in developer code
does not constitute registered collection.

Before an invocation:

1. One collector claims the scope. Another collector, including one using a
   different output root, cannot restart or reset the same scope.
2. The slot reserves its entire verified worst-case liability before execution.
3. A single-use ticket binds task, arm and run. `run-pair.sh` atomically claims
   it immediately before invoking the client and obtains the registered flag
   value from the ledger, not from editable ticket fields.

Reservations are **never refunded using estimated reported cost**. Completed,
failed, interrupted and in-flight attempts retain their full maximum liability.
Duplicate attempts/ticket claims and replacement retries are refused.
Append-only events retain lifecycle transitions.

A missing/invalid cost, an interrupted invocation, a contradicted bound, or a
client budget-exhaustion result stops subsequent invocations. A reported cost
above the reservation is retained, not clipped to make the ledger look safe.
Process loss leaves the in-flight reservation and claimed scope intact; there
is no automatic expiry, reset or unreviewed resume.

If collection stops, outputs retain `collection-outcome.json` with
`complete: false`, `verdict: null` and the spending snapshot. The epoch is not
promoted to `collected`, and no stage analysis is invoked. Partial budget-stop
data are **not** called underpowered, null, negative, positive or protocol-complete.

## Scope and validation

General historical benchmark calls retain their existing behavior.
`--ppw-spend-ticket` is restricted to a single non-null redesigned-policy run.
The low-level `run-pair.sh` primitive is not an authorization entry point;
registered collection must pass through `run-ppw-epoch.sh`/`run_epoch`.
Local process timeouts are explicitly not provider spending guarantees.

`test_ppw_spending.py` exercises exact accounting, all-slot fit, exhaustion,
missing/interrupted/in-flight costs, concurrent duplicate reservations/claims,
cross-output-root collector refusal, bound contradictions, scope binding and
real shell flag plumbing. Bounded adapters and receipts in these tests are
explicitly **synthetic test doubles**. The shell test invokes a deterministic
local stand-in, not an experimental model. Successful synthetic accounting is
not evidence that the real provider supplies a hard bound.
