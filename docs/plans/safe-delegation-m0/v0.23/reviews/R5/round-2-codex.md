# R5 close-out adversarial review, round 2 (Codex)

| Field | Value |
|---|---|
| Gate | R5 administrative closeout (#1377), with the terminal records for R1, R2A′, R3, R2B′, R4 |
| Round | 2 |
| Artifact paths | `r5-classification-and-action.md`, `r3-r2b-r4-not-reached.md`, `amendment-002-early-closeout.md` (from round 2), `r1/clearance-record.md`, `r2a-prime-public-proxy.md` Sections 2-2.2 and 7, `gate-state.json`, `spend-ledger.md`, `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs` |
| Reviewed commit | `081e1b30727e4efbd05d79aa18f1047940b08c0f` |
| Date (UTC) | 2026-10-01 |
| Tool | Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -C <worktree> -o <scratch>/round-2.txt` (prompt on stdin) |
| Model | `gpt-6.1-sol` (session header; reasoning effort `none`, the CLI default) |
| Session id | `01a0f8fe-5568-7fa3-b6de-4a381e8c562e` (ephemeral) |
| Tokens | 57,201 |
| Prompt | Names the artifact paths, the SHA, the governing records and issues, the maintainer decisions as given inputs, and quotes the protocol's reviewer instruction verbatim |
| Outcome | 0 BLOCKING, 2 MAJOR, 1 MINOR; all accepted; review stopped |

## Reviewer output (verbatim)

Round-1 dispositions:

1. **Resolved, conditional on amendment 002’s maintainer merge.** Its new early-closeout trigger supports R2A′ `UNAVAILABLE`. Amendment 001 alone still supplies no applicable terminal value. No alternative is better supported: `MET` lacks a conforming reviewed record; `NOT_REACHED` requires non-`MET` R0; `NOT_AUTHORIZED` requires declining the source; no expiry, revocation, or invalidation event is established.
2. **Resolved for the reviewed snapshot.** The ledger now includes closeout, orchestration, maintainer effort, and round-1 review hours. This invocation must be appended after completion.
3. **Resolved.** The conflicting R1 and R2A′ declarations are explicitly historical.
4. **Resolved.** Section 11 preserves the issue-specific approval route and its prerequisites.
5. **Resolved as to the rejected lifecycle value.** `INVALIDATED` R5 is now permitted. The new mutation test has a separate defect below.

New objections follow. Paths are relative to `docs/plans/safe-delegation-m0/v0.23/` unless specified otherwise.

1. **MAJOR — The early-closeout rule leaves another open-state case undefined.**  
   **Location:** `amendment-002-early-closeout.md:30–40`.  
   The rule promises to close each `null` gate, but supplies no outcome when prerequisites are `MET` and a conforming record has cleared review without yet receiving maintainer merge. Item 2 excludes that case; item 3 applies only to gates already holding a value. Review clearance cannot automatically confer `MET`, because amendment 001 separately requires maintainer merge. This does not alter today’s R2A′ result, but makes the broadly applicable amendment incomplete.  
   **Change:** Narrow the amendment to open gates lacking a cleared conforming record, or explicitly define the remaining case while preserving separate maintainer approval and merge requirements.

2. **MAJOR — The lifecycle mutation test accepts incomplete propagation and stale invalidation metadata.**  
   **Location:** Repository-root `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs:309–319`.  
   The test revokes R0 but changes only R5 to `INVALIDATED`. R1 and R2A′ both started or finished, so R0 §10 requires their invalidation too. R5 also retains its original October 1 decision date and adjudication-unavailability reason rather than recording the October 15 revocation cause. The test therefore asserts acceptance of a record that does not represent the required lifecycle transition.  
   **Change:** Construct a conforming revocation fixture: invalidate every affected started or finished gate, retain `NOT_REACHED` for unstarted descendants, and record dated causes and output/evidence dispositions. Keep a separate test isolating whether `INVALIDATED` is an allowed R5 value.

3. **MINOR — “No count … exists” contradicts the retained enumeration evidence.**  
   **Location:** `amendment-002-early-closeout.md:55–60`.  
   Section 4 says no count exists to disposition, although R2A′ Section 4 contains candidate, eligibility, exclusion, and activity counts. Their lack of downstream use does not erase their existence. Amendment 001 §8 requires amendments to identify affected evidence and its disposition.  
   **Change:** State that no downstream supply estimate, sizing, or classification exists; explicitly identify the enumeration counts as retained, non-binding historical material excluded from 0.23 gate evidence.

## Dispositions

Round-1 objections: the reviewer reports all five resolved (objection 1 conditional on the maintainer's merge of amendment 002, which is how every 0.23 rule takes effect).

| # | Severity | Disposition | Fix |
|---|---|---|---|
| 1 | MAJOR | `ACCEPTED`. Amendment 002 Section 2 now applies only to open gates without a cleared conforming record, and states that an open gate with a cleared but unmerged record keeps the ordinary rule (`MET` only on maintainer merge). Item 3 became a scope sentence; the Section 3 table cites amendment 001's own trigger for R1 | `90021eb7` |
| 2 | MAJOR | `ACCEPTED`. `Validator_AcceptsConformingRevocationAfterCloseout` now builds a conforming fixture (R0 `REVOKED`; R1, R2A′, R5 `INVALIDATED` with dated causes and evidence disposition; R3, R2B′, R4 `NOT_REACHED`). `Validator_AllowsInvalidatedAsR5Value` isolates the value check | `90021eb7` |
| 3 | MINOR | `ACCEPTED`. Amendment 002 Section 4 now names the retained R2A′ enumeration counts and excludes them from the evidence of every 0.23 gate | `90021eb7` |

Stopping rule: this round raised no BLOCKING objection, new or re-raised, and every MAJOR objection has a disposition, so review stops after round 2 of 5 (review-protocol.md "Severity and stopping").
