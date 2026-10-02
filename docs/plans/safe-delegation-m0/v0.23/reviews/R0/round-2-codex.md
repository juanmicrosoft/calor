# R0 review — round 2 — Codex

| Field | Value |
|---|---|
| Gate | R0 (#1371) and amendment 001 |
| Round | 2 |
| Artifacts | as round 1 |
| Reviewed commit | `8d49ce2c018613a433678f05561ca7d391879bda` |
| Date (UTC) | 2026-10-01 |
| Reviewer | OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral` |
| Model (as shown) | gpt-6.1-sol (provider openai) |
| Tokens (as shown) | 41,126 |
| Input | Protocol reviewer prompt, the maintainer's 2026-10-01 decisions, round-1 record, and `gh issue view` text of #1371 (body and comments), #1370 (body and comments), #1372 (comments), #1373, #1377 |

## Reviewer output (verbatim)

Round 1 dispositions:

1. **Not resolved.** Preserving historical authorization after a completed inquiry is defensible, but the revised rule leaves an uncovered deadline case: all gates dispositioned while #1370 remains open. See objection 1 below.
2. **Partially resolved.** Propagation and invalidation evidence are now explicit. Revalidation still checks prerequisite values rather than each prerequisite’s authority and expiry. See objection 2.
3. **Not resolved.** Removing published per-item identifiers does not establish anonymity when the record promises anyone can reconstruct the identifying decision list. See objection 3.
4. **Resolved.** The fixed graph and required gate membership address the cited bypasses.
5. **Resolved within the revised claim.** Making JSON authoritative and explicitly limiting prose scanning to a heuristic justifies the partial rejection of full natural-language detection.
6. **Resolved.** The protocol defines one shared allowance and explains same-version reviews.
7. **Resolved for the cited defects.** Sensitive-data removal overrides audit retention and has an execution deadline.
8. **Resolved.** Removal must be completed and verified; gate closure and actual access inventories are covered.
9. **Resolved at this gate.** Selection follows frozen rules, prior familiarity is disclosed, and excluded coverage must be reported.
10. **Resolved for review coverage.** The full governing text is supplied and mapped. The mapping exposes an unmet planning-hours requirement; see objection 4.

1. **BLOCKING — Deadline transition has an uncovered case.**  
   **Location:** `r0-authorization.md:309–318`.  
   Authority ends at the deadline, but R0 becomes `EXPIRED` only when a gate remains undispositioned. If R1–R5 are dispositioned and #1370 is still open, neither branch applies. R0 remains current `MET` despite authority having ended, with no defined disposition of completed descendants.  
   **Change:** Define the deadline transition for every case. Distinguish completed historical authorization from active authority explicitly, and specify whether inquiry completion means R5 disposition or epic closure. Apply the governing invalidation rules whenever authority expires before that defined completion.

2. **BLOCKING — Revalidation does not verify every prerequisite’s authority and expiry.**  
   **Location:** `r0-authorization.md:302–307`; `amendment-001-public-proxy.md:72–73`.  
   The acting agent checks that prerequisite gate values are `MET`, but checks expiry and revocation explicitly only for R0. A stale `MET` can therefore pass after a source permission or another prerequisite authority expires. This falls short of #1370’s requirement to revalidate **all prerequisite authorities and expiries**.  
   **Change:** Require examination and recording of each prerequisite’s authority, expiry, withdrawal status, and evidence validity—not just its current value. Define how stale state is corrected before work resumes. Make the amendment’s abbreviated rule point explicitly to this complete procedure.

3. **BLOCKING — Reproducibility can reconstruct contributor-linked decisions.**  
   **Location:** `r0-authorization.md:133–151`.  
   The record prohibits associating individual objects with eligibility decisions, then promises anyone can reproduce the full per-item decision list from public inputs. PRs and commits in that reconstructed list identify their contributors. Five-contributor suppression of published aggregates does not address this reconstruction path.  
   **Change:** Define whether the anonymity requirement includes reproducible inference. Provide a reproducibility procedure whose exposed outputs remain aggregated and do not reconstruct contributor-linked decisions, or obtain an explicit amendment permitting that disclosure and state its limits. Do not claim the existing digest procedure resolves contributor identification.

4. **BLOCKING — The required planning-hours cap remains unset.**  
   **Location:** `r0-authorization.md:88,369`.  
   #1371 requires a planning-hours cap. “No person-hour cap was set” records its absence; it does not satisfy the requirement. Subscription-backed AI work is also unbounded by the cash cap, so the other limits do not substitute for an effort cap.  
   **Change:** Obtain and record a concrete planning-hours cap with its measurement scope, or explicitly amend #1371 to waive that requirement. Until then, mark this prerequisite unmet rather than mapping it as satisfied.

5. **BLOCKING — General rules prohibit the required R5 closeout.**  
   **Location:** `r0-authorization.md:302–307,314–318,335–347`; `amendment-001-public-proxy.md:65–66,70–78,168–171`.  
   R5 must run after failed or expired prerequisites, but the general revalidation rule stops any gate unless R0 and every prerequisite are `MET` and the deadline has not passed. The amendment also requires every gate to finish review and merge under those conditions. These rules prevent the post-expiry administrative closeout they expressly require.  
   **Change:** Define a narrow R5 administrative-closeout exception covering non-MET prerequisites and elapsed deadlines. State its permitted records, review procedure, terminal values, and prohibition on new evidence inspection or substantive sizing.

6. **MAJOR — Contact exceptions expand the maintainer’s stated authorization.**  
   **Location:** `r0-authorization.md:50–66,177,212–213`; `gate-state.json`, `boundary.humanContact`.  
   The maintainer specified permitted contacts **NONE**. The record adds permission to reply to exclusion requests and send notices, while the authoritative boundary still declares human contact false. Processing an inbound request does not require an outbound reply. A legal obligation also should not be represented as permission granted by this inquiry.  
   **Change:** Remove discretionary outbound-contact permission unless separately authorized. Distinguish any independently required legal notice from inquiry authority, and make the structured boundary and notification rules express that distinction consistently.

## Dispositions

Fixing commit for all ACCEPTED items: `69de2d70708b431b196d501407928874136be4e8`.

| # | Severity | Disposition | Change or reason |
|---|---|---|---|
| 1 | BLOCKING | ACCEPTED | R0 Section 10 defines completion as R5 dispositioned and merged, and tabulates both deadline cases: completion first (R0 stays `MET` as a completed authorization with no active authority) and deadline first (R0 `EXPIRED`, invalidation including completed gates, then R5 administrative closeout). |
| 2 | BLOCKING | ACCEPTED | Revalidation now checks, for each prerequisite, its value, its own expiry, recorded withdrawals/exclusions/invalidations, and the relied-on output version, and records the gate-state.json SHA read. Stale `MET` stops work until the maintainer merges the invalidation. Amendment 001 Section 5 points to the full procedure. |
| 3 | BLOCKING | ACCEPTED (scope statement) | R0 7.1 states that the anonymity rule governs what the inquiry publishes, that the pinned extractor writes only aggregates and the digest, and that the inquiry does not claim anonymity against third-party re-derivation from public data, with the reason the residual is accepted. This takes the "define and state limits" option the objection offers; the record no longer claims the digest resolves identification. |
| 4 | BLOCKING | ACCEPTED | Section 5 sets planning-hours caps of 20 maintainer hours and 60 agent wall-clock session hours. They are explicitly marked as proposed in this record and adopted only by the maintainer's merge. An hours table is added to the spend ledger; gate-state.json and the test enforce the caps. |
| 5 | BLOCKING | ACCEPTED | R0 Section 10 adds the R5 administrative closeout exception: exempt from the revalidation stop, records only, no evidence access, sizing, or formal classification; terminal value `UNAVAILABLE` or `EXPIRED`. |
| 6 | MAJOR | ACCEPTED | Section 3 now has no exceptions. Exclusion dispositions are published on #1370 with no reply. Legally required notices are handled outside the inquiry, which stops; `boundary.humanContact` records inquiry contacts only. |
