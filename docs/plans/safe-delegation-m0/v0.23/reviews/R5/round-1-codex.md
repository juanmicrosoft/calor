# R5 close-out adversarial review, round 1 (Codex)

| Field | Value |
|---|---|
| Gate | R5 administrative closeout (#1377), with the terminal records for R1, R2A′, R3, R2B′, R4 |
| Round | 1 |
| Artifact paths | `r5-classification-and-action.md`, `r3-r2b-r4-not-reached.md`, `amendment-002-early-closeout.md` (from round 2), `r1/clearance-record.md`, `r2a-prime-public-proxy.md` Sections 2-2.2 and 7, `gate-state.json`, `spend-ledger.md`, `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs` |
| Reviewed commit | `ae0c70a151ee9e5fc2f51c198646d66dd55bd741` |
| Date (UTC) | 2026-10-01 |
| Tool | Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -C <worktree> -o <scratch>/round-1.txt` (prompt on stdin) |
| Model | `gpt-6.1-sol` (session header; reasoning effort `none`, the CLI default) |
| Session id | `01a0f8fa-29c5-7940-9b9c-78be499a3b86` (ephemeral) |
| Tokens | 71,988 |
| Prompt | Names the artifact paths, the SHA, the governing records and issues, the maintainer decisions as given inputs, and quotes the protocol's reviewer instruction verbatim |
| Outcome | 2 BLOCKING, 3 MAJOR, 0 MINOR; all accepted |

## Reviewer output (verbatim)

1. **BLOCKING — R2A′ UNAVAILABLE has no governing trigger.**  
   **Location:** `r2a-prime-public-proxy.md:123–134`; `gate-state.json:55–59`; `r5-classification-and-action.md:68`.  
   Amendment 001 §5 permits UNAVAILABLE when no permitting public source can be identified or blocking objections remain after five rounds. The artifact explicitly acknowledges that neither occurred, then substitutes “closest fit.” R0 §10 authorizes recording terminal values; it does not authorize inventing closure criteria. MET is also unsupported because the artifact violates the authority-only definition and lacks conforming review. Once R0 is MET, no alternative terminal value is better supported by the given decisions: NOT_AUTHORIZED requires declining the source, and the other states require events not established here.  
   **Resolution:** Adopt a numbered, versioned amendment defining early administrative termination with review rounds remaining, then apply it consistently. Until then, leave R2A′ open and disclose the unresolved state-assignment gap. R1 independently supports the downstream NOT_REACHED values and R5 UNADJUDICATED outcome.

2. **BLOCKING — The cap revalidation omits closeout effort already performed.**  
   **Location:** `spend-ledger.md:30–59`; `r5-classification-and-action.md:89–92, 128–129`.  
   The ledger contains no R5 proposer session, no closeout maintainer effort, and no separately identified integration sessions. Nevertheless, R5 asserts that cumulative hours are under their caps. R0 §5 counts drafting, revision, waiting, and concurrent sessions; unknown durations require conservative estimates. The reviewed artifact necessarily involved closeout drafting before this review began.  
   **Resolution:** Append the missing session and hours entries, including conservative estimates where necessary, reconcile cumulative totals, and update the cap revalidation. Record this review invocation after completion.

3. **MAJOR — Current and historical gate declarations remain contradictory.**  
   **Location:** `r2a-prime-public-proxy.md:35–60, 102–104`; `r1/clearance-record.md:28–39`.  
   R2A′ still declares its **current** value NOT_REACHED, promises MET on activation, and says its JSON entry remains null. R1 likewise says its entry “stays null in this PR” and requests a maintainer decision that the appended section records as completed. JSON precedence prevents these statements from changing authority, but does not make the public record internally consistent.  
   **Resolution:** Explicitly mark those passages as historical, superseded proposal text, with links to the terminal sections. Remove the unconditional MET activation promise.

4. **MAJOR — Parked-issue disclosure does not answer the reopening question for #1254 and #1259.**  
   **Location:** `r5-classification-and-action.md:203–213`.  
   “Unaffected” and “no approval” describe present effects, but do not state whether these issues may seek an issue-specific reopening decision, as #1377 requires. The governing `../decision.md:110–116` explicitly preserves a separate approval route with prerequisites.  
   **Resolution:** State whether each may seek a separate reopening decision, preserve the existing prerequisites, and distinguish eligibility to request a decision from actual reopening or authorization.

5. **MAJOR — The new validator rejects a lifecycle state the governing rules require.**  
   **Location:** `SafeDelegationV023BoundaryTests.cs:440–445`; `r5-classification-and-action.md:99–100`.  
   The validator allows only UNAVAILABLE or EXPIRED for every non-MET R5. R0 §10 requires affected gates that started or finished to become INVALIDATED after prerequisite expiry, revocation, or withdrawal; this includes an already completed R5. The new check conflates an administrative closeout’s terminal path with all subsequent lifecycle states.  
   **Resolution:** Distinguish administrative closeout from lifecycle invalidation. Permit INVALIDATED R5 with the required dated cause, evidence disposition, UNADJUDICATED status, and separate maintainer action; add a mutation test covering that transition.

## Dispositions

| # | Severity | Disposition | Fix |
|---|---|---|---|
| 1 | BLOCKING | `ACCEPTED`. The reviewer is right that amendment 001 has no trigger for this case and that "closest fit" is not a rule. Leaving R2A′ open would conflict with amendment 001 Section 4 (R5 runs only after every earlier gate is dispositioned). Following the proposed resolution, [amendment 002](../../amendment-002-early-closeout.md) records the maintainer's 2026-10-01 early close-out and adds the missing trigger; R2A′ `UNAVAILABLE` now cites it, and the amendment takes effect only on the maintainer's merge | `546871b0` |
| 2 | BLOCKING | `ACCEPTED`. Ledger rows 16-18 and hours rows added for R5 round 1, the close-out agent session, the orchestrating session, and maintainer close-out effort; cumulative totals reconciled (agent 14.25 of 60, maintainer 5.0 of 20, estimates); the R5 revalidation now states the figures | `546871b0` |
| 3 | MAJOR | `ACCEPTED`. Superseded current-value text in `r2a-prime-public-proxy.md` Section 2 and in `r1/clearance-record.md` is marked as history with links to the terminal sections; the activation promise is marked as no longer applying | `546871b0` |
| 4 | MAJOR | `ACCEPTED`. Section 11 of the R5 record now states that #1254 and #1259 may seek a separate issue-specific approval decision on the route `../decision.md` sets out, with its prerequisites preserved, and that eligibility to ask is not a reopening or authorization | `546871b0` |
| 5 | MAJOR | `ACCEPTED`. The validator now allows `INVALIDATED` for a non-`MET` R5 alongside `UNAVAILABLE` and `EXPIRED`, still requiring `UNADJUDICATED`, a dated reason, and a maintainer action; mutation test `Validator_AcceptsInvalidatedR5AfterLaterRevocation` added | `546871b0` |
