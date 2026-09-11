# Current-authority boundary: scoped read-only design assessment

## Scope and disposition

This responds to the user's 2026-09-11 12:36 EDT design/review question. It is
an engineering assessment, not an independent review, new authorization, or
operator-readiness certificate. Broader research implementation and operation
remain paused.

Examined source identities:

- Historical executed instrument:
  `0b87d52b92c07e0377f4948102f467b890107df5`.
- Inactive disposition proposal, draft PR #1438:
  `3d8dc1080b576c29d9bada6c8372193e90c23cbf`.
- Proposed record-set identity:
  `be4835997e360d15470efab4211e4fa0b2719093700f76e4ff4ce09d3099e720`.

No actual ledger/archive was opened or changed for this assessment. No process
census, native probe, test, financial operation or collector was run. No source
was edited. Existing source and test definitions were read.

**Conclusion: (a) is not established; (b) is the right prospective claim, but
the current source/evidence does not yet justify asserting it universally. HOLD
remains.** This is not evidence that a stale descendant currently exists, that a
capability leaked, or that a historical archive was modified.

## Two different claims

**(a) Every historical descendant exited.** The retained evidence does not prove
this. Historical termination targets process groups; neither the existing
same-group tests nor the current argv/cwd/text census establishes ancestry or
termination of a child that left its original group. No historical PID tree or
final actual `gateway-isolation.json` should be invented.

**(b) Any survivor lacks present authority to initiate new spending or mutate
adopted evidence.** This can, in principle, be proved through fencing and
confinement without proving (a). A process merely remaining alive is not an
additional billable request. Conversely, numerical retention of $51.04 is not
a proof of operational closure.

## What the exact source supports

All paths below are relative to `bench/phase0-agent-native/`.

| Boundary | Source-supported behavior | Qualification |
| --- | --- | --- |
| Old financial owner | New `ppw-gateway-disposition.py:978-1043` applies under `BEGIN IMMEDIATE`, changes the binding and sets `owner=NULL`; the next authorized start creates a fresh owner. | This is an actual owner fence, not just an epoch-label change. It has not been applied to the real ledger. |
| Legacy owner checks | Historical `ppw-gateway-budget.py:839-914` compares the passed owner with the current database owner before reserve, settle and stop. Reserve also requires `collecting`. | Calls using the old owner fail after revocation, even through the old implementation. Already reserved upstream work remains part of the retained old maxima. |
| New proof/owner binding | New `ppw-gateway-budget.py:1032-1176` replays disposition history, checks the exact startup proof, and binds the new owner to the reviewed binding/proof. | New-code protections are not automatically retrofitted into old loaded code. |
| Old listener/handlers | Historical `ppw-budget-gateway.py:74-76,280-329`: non-daemon handlers; close marks inactive, cancels the observer, shuts down and joins the server, closes the socket and then closes the observer. | This is a source/lifecycle inference, not a retained independent final socket-close receipt. An exception in the earlier cancellation step can interrupt this sequence. `allow_reuse_address=False` does not mean a port is never reused later. |
| Old runner control flow | Historical `ppw-instrument.py:782-858` owns the gateway in a `with` block; exiting its body closes it before later evidence/admission checks. Context and transient isolation files are unlinked in `finally`. | This supports closure on ordinary exception unwinding, under the identified trusted launcher/runtime assumptions. Forced process death is a different mechanism, and completion of every cleanup step is not directly archived. |
| Process-group cleanup | Historical `run-pair.sh:1118-1138,1294-1324` terminates/checks the original group before sealing; `ppw-gateway-client.py:543-599` similarly controls generated-code groups. | A detached stdio child is not necessarily a child that called `setsid`/changed group. The code does not prove arbitrary historical descendant termination. |
| Model confinement | Historical and proposed `ppw-gateway-client.py:283-339` deny general network and file writes, allow writes only inside the assigned workspace, allow only the selected IPv4 loopback port, restrict signal/process-info/task-port access to the same sandbox, and restrict IPC. | Fork/session changes must not be mistaken for sandbox removal. Reliance on inherited kernel enforcement must be explicit and independently exercised for the cross-scope case. |
| Control-context secrecy | Historical `ppw-instrument.py:787-829` and proposed `:907-949` create a random-named, exclusive mode-0600 context under the protected ledger directory. The model receives its client capability, not the trusted observer-control environment. | Mode 0600 alone is not a same-UID security boundary; the inherited Seatbelt read denial of protected state is essential. |
| Fresh endpoint capabilities | Both gateway versions create independent random 32-byte client and observer capability strings. Routes compare them before dispatch. Each listener binds to `127.0.0.1:0`. | Fresh capability values are not proof that an old process cannot learn a new value. Fresh bind is not guaranteed distinct historical port selection. |
| Archived evidence | Old model writes are confined outside the authoritative archive. `RunObserver.seal` at historical `ppw-run-observer.py:189-238` freezes/verifies source and copies authoritative fragments; disposition apply rechecks exact archive inventories. | Hash checks establish identity/detect drift; they are not OS-wide immutability against unrestricted trusted same-UID processes. Trusted output/observer writers and pipe lifetimes must be included in the closure argument. |

The new and old client sandbox source and `run-pair.sh` are unchanged between
the two examined commits. The gateway, budget and collector differ.

The parent's reported collector exit is a separate operator observation.
Under the trusted process/runtime assumptions, a genuinely exited process
cannot retain its listener or non-daemon handler threads; the kernel closes
its descriptors. That inference does not establish that every descendant
exited or that every user-space cleanup statement completed.

## Concrete open proof obligations

### 1. Old authority reacquisition is distinct from stale-owner rejection

The legacy `RequestLedger.start()` checks only `state == ready` and then writes a
new owner; it does not understand the new disposition proof. The new apply
currently leaves `state='ready'`.

This does **not** mean a sandboxed model can invoke that method against the
protected database: it cannot, under the registered kernel restrictions.
Nor does it prove an actual trusted old collector can return to startup: the
identified old collector calls start once before its loop, and it already
passed that point.

However, a general claim that the database itself fences *all old startup
code* would be false. The boundary must either explicitly establish trusted
old-launcher closure/non-reentry, or introduce and review a legacy-inaccessible
startup fence. A distinct prospective pre-start state is one possible design
to assess, not an implemented or approved change.

An unrestricted malicious host process with direct database write access is
outside the historical audit's trusted-host/non-substitution assumptions.
Do not silently use that exclusion to dismiss an actually surviving,
authorized old launcher.

### 2. Port reuse plus future capability visibility needs an explicit control

The old sandbox permits its particular loopback port, not a particular server
instance. The prospective gateway still binds an ephemeral port with no
retired-port inventory.

The client policy starts from `allow default` and denies reads only at the
listed roots. `discover_sensitive_roots` at historical
`ppw-gateway-client.py:165-198` captures selected sensitive bench directories
in worktrees existing at that time, plus Git storage. Its
`SENSITIVE_BENCH_NAMES` does not include `.ppw-instrument-work`.
The proposed collector puts new workspaces at
`REPO/.ppw-instrument-work/<new epoch>` (`ppw-instrument.py:773`).
An old policy does not acquire new denials when a future worktree, workspace
or observer directory is created.

Thus the current source does not establish that a surviving old sandbox is
unable to read every future model workspace. The future client necessarily
knows its own client capability. If that capability becomes visible through
an old-readable file or other permitted channel, forced reuse of the old
permitted port would turn it into authority at the fresh gateway. This is a
conditional capability path, not an observed leak or a demonstrated historical
exploit. A random nonce alone is not sufficient proof against that case.

Potential approaches for independent assessment include enforcing transport
disjointness from every old permitted endpoint, or placing all future
capability-bearing surfaces under boundaries already denied to the old
policies. Neither approach is implemented or approved by this note. The actual
old endpoint/policy evidence must not be reconstructed by guesswork.

### 3. Trusted sinks and surviving descriptors belong in the evidence boundary

The historical runner deliberately uses trusted `cat`, `tee` and `jq` sinks
outside the model sandbox (`run-pair.sh:1294-1300`) rather than giving the model
direct archive output handles. Normal pipeline completion and original-group
cleanup support their closure.

An escaped child's inherited pipes, open workspace descriptors, the trusted
sinks' group membership and the observer's generated-process callbacks still
need an explicit boundary argument. A census of names/cwd/text alone is not
that argument. Do not claim that unchanged artifact hashes by themselves
prevent a later write.

## Adversarial synthetic controls needed before accepting (b)

These are proposed controls, **not results**. No new tests were written or run
as part of this read-only answer.

1. Keep a deliberately `setsid`/group-escaped child alive after its original
   parent and normal pipeline exit. Show inherited confinement still rejects
   writes/aliases into old authoritative archives, the canonical financial
   state and fresh future evidence.
2. Exercise old client and observer tokens against a fresh gateway, including
   deliberately reused old port numbers. Count connection-factory calls and
   synthetic ledger events; refusal must precede new reservation/forwarding.
3. Exercise future-capability confidentiality, not just random-token mismatch:
   intentionally place a fresh client's capability on representative
   model-writable/logging surfaces and verify that an old sandbox cannot
   obtain/use it. If the chosen design instead enforces transport
   disjointness, verify that disjointness even with a known fresh token.
4. Invoke reserve/settle/stop with the exact stale owner through both old and
   new loaded ledger implementations. Separately test old startup code at the
   post-transition boundary and document whether rejection is a code fence
   or depends on the proved trusted-launcher lifecycle.
5. Force an old admitted upstream completion to arrive after the transition.
   Preserve the two old rows/maxima without refunds; reject new stale-owner
   actions and independently demonstrate that any *new* unknown still halts.
6. Hold inherited stdout/stderr pipes and file descriptors across group escape.
   Verify closure of trusted archive sinks and inability to mutate adopted
   artifacts after the identity check.
7. Attempt cross-sandbox signal, task-port/process inspection and allowed IPC
   paths against a fresh launcher/client. Fail closed if any required kernel
   observation is unavailable.

Existing tests cover same-group cleanup, detached stdio children that remain
in the original group, ordinary protected-file and network isolation, and
proof/history tampering. They are not substituted for these cross-generation
controls.

## Review and authorization boundary

Route this note to the independent historical financial auditor
`0b9ec5f7-d216-4282-9c6e-f3ca2d77e2b5` and the methods/registration reviewer
`6695b79d-aa59-4574-9e06-cef6bdf595af` for the boundary portion of their eventual
separate reviews. Their historical or draft approvals must not be upgraded.
Any resulting implementation change requires a new exact source/record set
and appropriate source/financial/methods review.

The historical bound artifact
`a387cc8f7376eb48295f63ac87d3c38d85985ac2fc565c96c9982a4f86f4d1a5`
remains unchanged and historical-only. Actual historical cost is unknown.
The existing $51.04 retention is not a new allowance, proof of operational
closure, or permission to execute the proposed permanent-retention amendment.

No unrelated process should be killed. No historical isolation/PID evidence
should be backfilled. No guard should be bypassed. No activation, apply,
recovery or collection is authorized by this note.
