# R0 review — round 3 — Codex

| Field | Value |
|---|---|
| Gate | R0 (#1371) and amendment 001 |
| Round | 3 |
| Artifacts | as round 1 |
| Reviewed commit | `b6980126f8773c9b4790101a36e80d8217c06ff1` |
| Date (UTC) | 2026-10-01 |
| Reviewer | OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral` |
| Model (as shown) | gpt-6.1-sol (provider openai) |
| Tokens (as shown) | 43,433 |
| Input | As round 2. The prompt additionally restated the protocol's BLOCKING definition and asked the reviewer to report any re-raised rejected BLOCKING objection. |

## Reviewer output (verbatim)

Round-2 dispositions:

1. **Resolved.** Completion now means merged R5 disposition, and both deadline cases are defined.
2. **Partially resolved.** Individual expiry and withdrawal checks are specified, but their coverage remains ambiguous for R5’s upstream authorities. See objection 1.
3. **Resolved within the stated publication boundary.** Reconstruction anonymity is explicitly disclaimed; the extractor’s published outputs are limited to aggregates and a digest.
4. **Resolved as a proposed authorization.** Concrete hours caps require maintainer adoption by merge. Their accounting remains incomplete; see objection 2.
5. **Resolved.** The administrative-closeout exception permits preservation of `UNADJUDICATED` after expiry or prerequisite failure.
6. **Resolved.** Discretionary outbound contacts are removed; independently required legal notices are separated from inquiry authority.

**Rejected BLOCKING objections re-raised: none.** Objection 1 continues an accepted but incompletely resolved objection.

1. **BLOCKING — R5 can revalidate R4 without revalidating the authorities underlying it.**

   **Location:** `r0-authorization.md:318–323`; `amendment-001-public-proxy.md:55–59,72–76`; `gate-state.json`, R5 `prerequisites`.

   The procedure checks “each prerequisite in the amended graph,” while R5’s listed prerequisite is only R4. A literal implementation therefore checks R0 and R4, but does not examine R1, R2A′, R3, or R2B′ for their own expiries, withdrawals, and evidence validity. For example, an expired R2A′ authority with stale `MET` values downstream can pass R5’s checks. This leaves #1370’s requirement to revalidate **all prerequisite authorities and expiries** incompletely specified.

   **Change:** Explicitly require the transitive prerequisite closure at every checkpoint. Name R5’s required checks as R0, R1, R2A′, R3, R2B′, and R4. Record each authority’s validity and relied-on evidence version; stop and propagate invalidation when any check fails.

2. **MAJOR — The hours ledger reports zero despite recorded agent work, without defining an accounting start.**

   **Location:** `spend-ledger.md:13–14,20–27`; `r0-authorization.md:88,324`.

   Two completed review invocations are recorded, but the hours table is empty and its cumulative agent total is asserted as zero. Drafting and revision sessions are also absent. The cap applies “across all 0.23 gates,” yet the record does not say whether pre-merge preparation counts. Consequently, subsequent cap revalidation cannot establish remaining capacity from this ledger.

   **Change:** Define the accounting start and whether preparation, review, concurrent sessions, and waiting time count. Record elapsed time for completed sessions, using labeled conservative estimates where necessary. If pre-merge work is excluded, state that explicitly and report its duration as unknown rather than implying no work occurred.

## Dispositions

Fixing commit for all ACCEPTED items: `8d4571d55fe8f724246f5622b087ff01eba3798b`.

| # | Severity | Disposition | Change or reason |
|---|---|---|---|
| 1 | BLOCKING | ACCEPTED | Revalidation now covers every gate in the transitive prerequisite closure of the acting gate, with R5's full list named (R0 Section 10; amendment 001 Section 5). |
| 2 | MAJOR | ACCEPTED | R0 Section 5 defines the accounting start (2026-10-01 approval), inclusion of pre-merge drafting/review/revision, wall-clock and concurrency rules, and labeled conservative estimates. The ledger records this session as an estimated 3.0 agent hours; maintainer hours are reported as not metered rather than zero. |
