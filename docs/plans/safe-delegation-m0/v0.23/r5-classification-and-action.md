# Safe delegation M0 v0.23 — R5 close-out: classification status and maintainer action

**Recorded:** 2026-10-01. **Issue:** #1377. **Epic:** #1370.
**Decision authority:** @juanmicrosoft (R5 classifier and action owner,
[r0-authorization.md](r0-authorization.md) Section 2), close-out decision of
2026-10-01, relayed to the implementing agent that day.
**Effective:** on the maintainer's merge of the 0.23 close-out PR.
**Machine-readable state:** [gate-state.json](gate-state.json) (authoritative;
a Markdown statement that contradicts it has no effect).

## 1. Result

| Field | Value |
|---|---|
| R5 gate value | `UNAVAILABLE` |
| Scientific classification | **None produced.** Process status `UNADJUDICATED` |
| Maintainer action (separate field) | `DEFER` |
| Original-domain M0 status ([../decision.md](../decision.md)) | `UNADJUDICATED`, unchanged |
| Date of reason | 2026-10-01 |

**No formal classification was produced.** None of the four registered
classifications applies to this record. Amendment 001 Section 6 requires any
classification produced under it to carry the label "AI-adjudicated,
public-proxy domain". That rule had nothing to attach to here. Any later
restatement of this result must still describe it as a public-proxy,
AI-reviewed process outcome, not as independent or human review.

## 2. Dated reason (2026-10-01)

Formal adjudication could not occur, for two reasons.

1. **No cleared methods review.** R1 (#1372) is `UNAVAILABLE`. Its rules
   went through the 5 adversarial review rounds that
   [review-protocol.md](review-protocol.md) allows. Round 5 ended with 2
   unresolved BLOCKING objections, and the fixes in `a929aa74` were never
   reviewed ([r1/clearance-record.md](r1/clearance-record.md)). Amendment 001
   Section 5 and R1's own ordered procedure (rules section 7.2, rule 1) both
   make a formal classification impossible without a cleared R1.
2. **Unmeasurable gates in the public-proxy domain.** R1's draft rules
   (section 7.3) record, before any inspection, that the inputs for G0
   (workload horizon), G1.1 and G1.2 (cost), G5 (usable adoption), and G6.2
   (independent handoff receipt) are `NOT_MEASURABLE_IN_DOMAIN`. Amendment 001
   Section 5 closes R4 `UNAVAILABLE` when required inputs are missing. So even
   a cleared R1 v1 would have led R4 to `UNAVAILABLE` and R5 to
   `UNADJUDICATED`, unless an R0 amendment changed R4's closure criterion. No
   such amendment was adopted. This finding comes from an uncleared record and
   is disclosed as an expectation, not a cleared result.

R2A′ (#1373) is also `UNAVAILABLE`, under the early close-out rule of
[amendment 002](amendment-002-early-closeout.md), and R3, R2B′, and R4 are
`NOT_REACHED` (Section 3). Either blocking condition alone prevents a formal
classification.

**What this result is not.** It is not a finding that the safe-delegation
study is infeasible, that adopter demand is zero, that public task supply is
absent, or that the study would succeed. No missing evidence is converted
into any of these. The inquiry never measured supply, never sized a study,
and never contacted anyone.

## 3. Gate values

Values take effect on merge of the close-out PR. R0 takes effect on merge of
#1467.

| Gate | Issue | Value | Record | Reason |
|---|---|---|---|---|
| R0 | #1371 | `MET` (completed; no active authority remains once R5 is dispositioned) | [r0-authorization.md](r0-authorization.md) | Bounded public-data inquiry authorized 2026-10-01 |
| R1 | #1372 | `UNAVAILABLE` | [r1/clearance-record.md](r1/clearance-record.md) | 2 BLOCKING objections unresolved after round 5 of 5 |
| R2A′ | #1373 | `UNAVAILABLE` | [r2a-prime-public-proxy.md](r2a-prime-public-proxy.md) Section 2.2 | [Amendment 002](amendment-002-early-closeout.md) Section 2 item 2: no record conforming to the amended definition was obtained before the early close-out (2 of 5 rounds used) |
| R3 | #1374 | `NOT_REACHED` | [r3-r2b-r4-not-reached.md](r3-r2b-r4-not-reached.md) | R1, R2A′ not `MET` |
| R2B′ | #1375 | `NOT_REACHED` | [r3-r2b-r4-not-reached.md](r3-r2b-r4-not-reached.md) | R1, R2A′, R3 not `MET` |
| R4 | #1376 | `NOT_REACHED` | [r3-r2b-r4-not-reached.md](r3-r2b-r4-not-reached.md) | R1, R2A′, R3, R2B′ not `MET` |
| R5 | #1377 | `UNAVAILABLE` | This record | Section 2 |

Original-definition history (amendment 001 Section 2), unchanged:
R1-original `UNAVAILABLE`, R2A-original `UNAVAILABLE`
([r2a-adopter-unavailable.md](r2a-adopter-unavailable.md)), R3-original,
R2B-original, and R4-original `NOT_REACHED`.

## 4. How this close-out was run

R4 is `NOT_REACHED`, so this is the **R5 administrative closeout** of R0
Section 10. That procedure may only record terminal gate values,
invalidations, and disposition actions; publish the dated reason; preserve
`UNADJUDICATED`; and record a maintainer action. It may not inspect, access,
or extract evidence, size anything, or classify formally. This close-out did
none of those things: it read only the committed 0.23 records and the
#1370-#1377 issue text.

The maintainer ended the inquiry early on 2026-10-01.
[Amendment 002](amendment-002-early-closeout.md) records that decision and
the rule that closes the gates still open at that time.

R0 revalidation at close-out (2026-10-01): R0 is `MET` on merge of #1467;
the inquiry deadline (2026-10-29) has not passed; no revocation, withdrawal,
exclusion request, or license change is recorded; cash is USD 0.00 of 200.00
and hours are 14.5 of 60 (agent) and 5.0 of 20 (maintainer) after R5 review round 2, both
conservative estimates that include this close-out session
([spend-ledger.md](spend-ledger.md)). Every other gate in R5's prerequisite closure is not
`MET`, which is why the administrative closeout applies.

**Machine check.** `SafeDelegationV023BoundaryTests` gains three terminal
invariants with this close-out: a gate whose ancestor is `UNAVAILABLE`,
`NOT_AUTHORIZED`, or `NOT_REACHED` must be `NOT_REACHED` or open (R5
excepted); every gate with a non-`MET` value carries a `reason` and a
`decided` date; and a non-`MET` R5 must preserve `UNADJUDICATED` and record
a maintainer action, with value `UNAVAILABLE` or `EXPIRED` (administrative
closeout) or `INVALIDATED` (a later expiry, revocation, or withdrawal under
R0 Section 10). The close-out adds
the fields `decided`, `reason`, `maintainerActionNote`, and
`originalDomainStatus` to gate entries in gate-state.json. R1's live-state
rule (governance section 5.1) would treat new fields as a change to reviewed
authority; that rule only gates an R1 `MET`, which can no longer occur.

## 5. Assumptions

- The maintainer's 2026-10-01 decisions (R0 terms, amendment 001, the R1
  close-out, acceptance of R1's protocol deviations, and this R5 action)
  were relayed to the implementing agents in their sessions. They become the
  repository record only when the maintainer merges the PRs that carry them.
- AI review is a quality control, not independent review. The maintainer is
  the only human in the inquiry (R0 Section 2).
- Proposer usage and agent hours that the tools did not report are entered
  as labeled conservative estimates (R0 Section 5).

## 6. Resources used against caps

| Resource | Used | Cap | Source |
|---|---|---|---|
| Marginal cash | USD 0.00 (all AI use ran on existing subscriptions) | USD 200.00 | [spend-ledger.md](spend-ledger.md) |
| Review rounds, R0 | 4 | 5 | `reviews/R0/` |
| Review rounds, R1 | 5, plus a competence probe that is not an artifact round | 5 | `reviews/R1/` |
| Review rounds, R2A′ | 2 | 5 | `reviews/R2A-prime/` |
| Review rounds, R3, R2B′, R4 | 0 | 5 each | Not started |
| Review rounds, R5 (this record) | 2 (Section 13) | 5 | `reviews/R5/` |
| Codex tokens | 1,658,905 (R0 157,470 + R1 507,104 + R2A′ 865,142 + R5 129,189) | Not capped | Ledger rows 1-4, 6-11, 13-14, 16, 19 |
| Agent session hours | See ledger "Cumulative" line | 60 | Ledger hours table (estimates) |
| Maintainer hours | See ledger "Cumulative" line | 20 | Ledger hours table (estimates pending the maintainer's report) |
| Inquiry deadline | Closed 2026-10-01 | 2026-10-29 | R0 Section 5 |
| Compensation to any person | USD 0 | USD 0 | R0 Section 8 |

No prospective study envelope was proposed or approved; R4 never ran.

## 7. Evidence revisions

| Record | Commit SHA | Status |
|---|---|---|
| R0 records (authorization, amendment 001, protocol, ledger, gate-state, boundary test) | `b0c054377c9d414bb3702b8d57cb35f955cd523f` (#1467) | `MET` on merge |
| R2A′ criteria freeze | `15fba333792c8c1949ee38a3ee35f3b44a6e82ca` | Frozen before enumeration |
| R2A′ enumeration snapshot | `fb2a610f09c05bef46e07fb64c74ff7ee99464fc` | Non-binding; retrieved before R0 was `MET`; used by no gate |
| R2A′ last reviewed version | `7a8bb7d6d16ad7c7b87c7ba37eae278f346975c4` | Does not meet the amended definition |
| R2A′ stacked on R0 | `858a1094a78754cdc550ba63f054983036736cee` (#1465) | Merge; no earlier SHA rewritten |
| R1 last reviewed version (round 5) | `a8be69412e4ee825635f82f818eb6826459cfc7a` | 2 BLOCKING unresolved |
| R1 unreviewed fixes | `a929aa7449629b6db0d067556a0a51aab4860653` | Never reviewed |
| R1 round-5 record | `23b2f9ab33c47729283dbb0ca08993426716039f` | — |
| R1 stacked on R2A′ | `2c4302d41caf6898ed5ba2068cd0beb81e9867fd` (#1468) | Merge; no earlier SHA rewritten |

No output was invalidated, so no evidence is excluded from classification on
that ground. No classification was made from any evidence.

## 8. Sign-off, independence, and conflicts

- **Sign-off.** The maintainer decides and merges. There is no independent
  signature or countersignature. #1377's `MET` path needs one; this record
  takes the `UNAVAILABLE` path instead.
- **AI review.** Codex CLI (OpenAI) was the required cross-family reviewer;
  Claude Code (Anthropic) was the proposer. Neither is an independent human
  reviewer.
- **Conflicts.** The maintainer authors Calor, chose the public proxy, and
  holds the decision, budget, and R5 roles together (R0 Section 8). The
  proposer's model family wrote much of Calor.
- **Shared blind-spot risk (disclosed per R0 Section 8).** Proposer and
  reviewer models are trained on overlapping public text and may share blind
  spots. Cross-vendor review reduces this risk; it does not remove it.

## 9. Expiry, revocation, invalidation, and disposition disclosure

| Item | Status |
|---|---|
| Expired gates | None. The deadline (2026-10-29) did not pass before close-out |
| Revoked authority | None |
| `INVALIDATED` gates or outputs | None |
| `NOT_REACHED` gates | R3, R2B′, R4 (and R3-original, R2B-original, R4-original in history) |
| Exclusion requests, license changes, non-public data found | None recorded |
| Human contacts | None (`boundary.humanContact` is `false`) |
| Restricted or non-public data accessed | None. Restricted evidence manifest: **empty**, because no restricted store was approved or used (R0 Section 6) |
| Committed records | Retained as the public governance record (R0 Section 7.2) |
| Local caches | R0 Section 7.3 requires deletion within 7 days of each gate's closure and of milestone close. R1, R3, R2B′, R4, and R5 accessed no repository data. The R2A′ enumeration states that it wrote no raw responses to disk. Any remaining local clone, scratch extract, or Codex session store entry that holds repository data must be deleted and the deletion logged in R0 Section 13 by the maintainer within 7 days of the close-out merge. Persisted Codex sessions from R1 rounds 2-5 hold only review transcripts of committed records, which R0 Section 7.2 retains as logs |

## 10. Maintainer action: `DEFER`

M0 is parked. A future milestone may restart it if any of these becomes
available:

- an adopter organization willing to give conditional support (original R2A);
- private task data from such an adopter (original R2B);
- a human methods reviewer (original R1).

Reusable, **non-binding** inputs kept on file:

- [r1-decision-rules-v1.md](r1-decision-rules-v1.md), its JSON mirror, and
  `r1-safety-nmin.py`, including the unreviewed `a929aa74` fixes. They never
  cleared review and are not frozen rules.
- The R2A′ repository frame ([r2a-prime-repos.json](r2a-prime-repos.json)
  and [tools/r2a_prime_enumerate.py](tools/r2a_prime_enumerate.py)). It does
  not meet the amended R2A′ definition and was retrieved before R0 was `MET`.

A restart needs its own authorization. It must review these inputs afresh
before relying on them; nothing in 0.23 binds it. `DEFER` does not activate
implementation, recruitment, spending, or any study.

## 11. Parked issues

This record reopens nothing.

- **#1284-#1309** (closed NOT_PLANNED): none may seek reopening on the basis
  of 0.23, because 0.23 produced no formal classification. They stay closed.
- **#1254** (0.21 PP-W-rows effect-row proposal) and **#1259**: they may
  still seek a separate, issue-specific approval decision on the route that
  [../decision.md](../decision.md) ("Separate PP-W-rows proposal") already
  sets out, with every prerequisite listed there preserved: buildability
  gate, redesigned off-ramps, bounded spend ceiling, null-result acceptance,
  task and oracle prerequisites, and independent review. Being eligible to
  ask is not a reopening or an authorization. 0.23 adds nothing to that
  route: no 0.23 approval, funds, tasks, or evidence transfer to them, and
  `boundary.reuseBy1254Or1259` stays `false`.
- **#1371-#1377**: their terminal values are recorded here and in the linked
  records. The maintainer closes the issues. A future milestone opens new
  issues rather than reopening these.

## 12. Interpretation limits

- Every 0.23 result is a public-proxy, AI-reviewed process result. It says
  nothing about organizational adoption, adopter demand, private workloads,
  or human reviewer capacity (amendment 001 Section 6).
- "None obtained within the authorized search and deadline" is the only
  claim made about absent parties. With zero permitted contacts, the search
  for human parties was empty by authorization (R0 Section 11).
- The R1 unmeasurability finding is pre-registered reasoning in an uncleared
  record, not a measured fact.
- No package, tag, release, deployment, participant action, or study
  activation follows from this record. Correctness ownership and ordinary
  release boundaries are unchanged.

## 13. Review of this record

R0 Section 10 sends the administrative closeout record through
[review-protocol.md](review-protocol.md) when rounds and cash remain. Both
remained, so the close-out record (this file, the records it links for R1,
R2A′, R3, R2B′, and R4, amendment 002, gate-state.json, the ledger, and the
boundary test) was reviewed by Codex.

| Round | Reviewed commit | BLOCKING | MAJOR | MINOR | Outcome |
|---|---|---:|---:|---:|---|
| 1 | `ae0c70a1` | 2 | 3 | 0 | All accepted; fixes in `546871b0` ([round-1-codex.md](reviews/R5/round-1-codex.md)) |

A clean review round would not make R5 `MET`: R5 is `UNAVAILABLE` whatever
the review outcome. If BLOCKING objections remained after round 5, this
record would list them, and the maintainer could still decline to merge.
