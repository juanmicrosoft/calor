# R0 review — round 1 — Codex

| Field | Value |
|---|---|
| Gate | R0 (#1371) and amendment 001 |
| Round | 1 |
| Artifacts | `r0-authorization.md`, `amendment-001-public-proxy.md`, `review-protocol.md`, `spend-ledger.md`, `gate-state.json`, `tests/Calor.Compiler.Tests/Plans/SafeDelegationV023BoundaryTests.cs` |
| Reviewed commit | `02c409eaecc4bd72a07dfde48fb7974e6b75acc1` |
| Date (UTC) | 2026-10-01 |
| Reviewer | OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral` |
| Model (as shown) | gpt-6.1-sol (provider openai) |
| Tokens (as shown) | 35,454 |
| Input | Protocol reviewer prompt, the maintainer's 2026-10-01 decisions, and `gh issue view` text of #1370 (with comments), #1371 (comments only, by mistake; see objection 10), #1372 (comments), #1373, #1377 |

## Reviewer output (verbatim)

1. **BLOCKING — Expiry does not invalidate completed dependent gates.**  
   **Location:** `r0-authorization.md:265–269`; `amendment-001-public-proxy.md:80–81`.  
   R0 expires only if R1–R4 remain undispositioned, and expiry invalidates only in-progress gates. This contradicts #1370’s lifecycle correction: expiry must change every affected completed `MET` dependent to `INVALIDATED`. Finishing R1–R4 before the deadline currently permits R0 to remain active indefinitely.  
   **Resolve:** Make authority expire unconditionally at the deadline; invalidate affected completed and in-progress outputs, prohibit reuse, and close unstarted descendants `NOT_REACHED`.

2. **BLOCKING — Revalidation covers R0 rather than every prerequisite authority.**  
   **Location:** `r0-authorization.md:261–278`; `amendment-001-public-proxy.md:62–81`.  
   Neither operational rule requires revalidating R1, R2A′, R3, and R2B′ before dependent work and closure. Source exclusion or permission withdrawal stops analysis but does not explicitly revoke R2A′ or invalidate its completed descendants. Required invalidation records—time, affected output version/hash, work performed, partial-evidence disposition—and mandatory R5 lifecycle disclosure are also missing.  
   **Resolve:** Define the complete prerequisite revalidation and propagation procedure, including permitted external notifications, source-authority withdrawal, required invalidation fields, and R5 disclosure.

3. **BLOCKING — Published provenance defeats the maintainer’s contributor anonymity requirement.**  
   **Location:** `r0-authorization.md:133–144,150,153`.  
   Per-item PR numbers and commit SHAs identify individual contributors through direct lookup; the record explicitly accepts this. Five-contributor aggregate suppression does not protect separately published per-item eligibility records. The contact-data row additionally permits retaining a requester’s public handle.  
   **Resolve:** Publish aggregate provenance that does not associate individual public objects with analyzed decisions or contributor clusters. Remove the handle-retention exception and specify how reproducibility works within that boundary.

4. **BLOCKING — The prerequisite machine check trusts editable prerequisites.**  
   **Location:** `SafeDelegationV023BoundaryTests.cs:194–221,223–241`.  
   Set R3 to `MET` and replace its `prerequisites` with `[]`: validation passes despite R1 and R2A′ remaining open. Removing R5—or all current gates—also passes because required gate membership is never checked. This defeats Section 9’s claimed enforcement.  
   **Resolve:** Validate exact required gate IDs and prerequisite sets against a fixed graph, reject missing or unexpected gates, and add negative cases for deleted gates and cleared prerequisites.

5. **BLOCKING — Markdown enforcement is trivially evaded.**  
   **Location:** `SafeDelegationV023BoundaryTests.cs:45–51,69–75,254–264`; `r0-authorization.md:251–256`.  
   These declarations pass:
   - `Task execution is authorized.`
   - `Classification: FEASIBLE AS PROPOSED`
   - `Participant enrollment:` followed by `started` on another line.

   Records placed beneath `reviews/` bypass scanning entirely. Section 9 claims broader enforcement than these narrowly formatted, single-line expressions provide.  
   **Resolve:** Require a validated structured declaration for authoritative state, reject conflicting or unstructured state declarations, separate transcript content from dispositions, and describe the actual limits of prose scanning.

6. **BLOCKING — The review protocol doubles the authorized round allowance.**  
   **Location:** `review-protocol.md:98–102`; `r0-authorization.md`, Section 5.  
   Copilot receives five rounds in addition to five Codex rounds, permitting ten adversarial rounds per artifact. The maintainer authorized a maximum of five, without a provider exception.  
   **Resolve:** Count all adversarial review rounds against one artifact-level allowance; define whether simultaneous reviewers constitute one round and validate that accounting.

7. **MAJOR — Retention exceptions can preserve prohibited personal or confidential material indefinitely.**  
   **Location:** `r0-authorization.md:155,168,173–185`.  
   Logs are never deleted, and git history remains public by default even after personal or restricted data is discovered. An instruction against future analysis does not isolate publicly accessible sensitive material. The seven-day decision about a history purge has no execution deadline.  
   **Resolve:** Make sensitive-data removal override ordinary audit retention; preserve sanitized disposition evidence, specify purge/access-revocation deadlines, and define isolation and eventual disposal for any justified retention exception.

8. **MAJOR — Deletion deadlines govern proposed PRs rather than completed removal.**  
   **Location:** `r0-authorization.md:166–169,197–204`.  
   Opening a removal PR within seven days or 24 hours does not ensure deletion by that deadline. Local caches are permitted only while a gate is active, but gate closure itself has no explicit deletion deadline. “No access occurred” after refusal assumes away preparatory or previously authorized access.  
   **Resolve:** Require completed removal and verification by the deadline, add gate-close cache disposal, and inventory actual access and retained material after refusal or withdrawal.

9. **MAJOR — Source selection can precede the rules governing it.**  
   **Location:** `amendment-001-public-proxy.md:55,85–104,181–188`.  
   R2A′ runs parallel to R1 even though R1 freezes its objective source-selection rules. Freezing before task counts does not prevent selecting repositories using prior knowledge of Calor compatibility. Symmetric exclusions can still produce a Calor-favorable domain through wholesale removal of unsupported work.  
   **Resolve:** Freeze governing selection rules before selecting the pool; document prior source familiarity, enumerate the candidate frame and exclusions, and report coverage of excluded public .NET work.

10. **MAJOR — Full #1371 acceptance coverage cannot be audited from the supplied governing text.**  
    **Location:** `r0-authorization.md:122`; `SafeDelegationV023BoundaryTests.cs:8–10`; review input.  
    The supplied text contains #1371’s privacy amendment but omits its original required-record list and acceptance criteria. GitHub retrieval failed through both web access and `gh`. Consequently, the asserted acceptance-criterion-3 coverage and completeness against every original required-record item remain unverifiable.  
    **Resolve:** Include a versioned copy of the complete governing issue text and a requirement-to-record mapping before closing R0.

## Dispositions

Fixing commit for all ACCEPTED items: `d838e0c5201eb1f199a1b20fcee3b07a7455ff5c`.

| # | Severity | Disposition | Change or reason |
|---|---|---|---|
| 1 | BLOCKING | ACCEPTED in part | R0 authority now ends at the earlier of the deadline and #1370 close. If the deadline arrives while any of R1-R5 is undispositioned, R0 becomes `EXPIRED` and affected gates, including completed `MET` gates, become `INVALIDATED` (R0 Section 10). Rejected part: unconditional expiry after #1370 has closed would invalidate a completed inquiry, which #1371 does not require ("expires before downstream gates complete"); a completed inquiry keeps `MET` as a completed authorization. |
| 2 | BLOCKING | ACCEPTED | R0 Section 10 now requires revalidation of R0 and every prerequisite in the amended graph, defines prerequisite withdrawal below R0 (including R2A′ source exclusion), the invalidation record fields, the no-reuse rule, and R5 disclosure. Amendment 001 Section 5 points to it. |
| 3 | BLOCKING | ACCEPTED | R0 7.1: committed artifacts no longer cite individual PRs, issues, or commits next to decisions; provenance is repository + pinned revision + pinned extractor/rules + SHA-256 digest of the uncommitted per-item list. Handle retention removed from 7.2. |
| 4 | BLOCKING | ACCEPTED | The test now holds a fixed copy of the amended graph, rejects missing, extra, or re-wired gates, checks `MET` against the fixed graph, and adds negative tests for cleared prerequisites and a deleted gate. |
| 5 | BLOCKING | ACCEPTED in part | gate-state.json is declared the only authoritative state (R0 Section 9). The Markdown scan now joins wrapped paragraph lines, catches `Classification:` and table-cell forms, catches `<activity> is/was/has been <state>` sentences unless negated, and scans files under `reviews/` unless they are correctly named transcripts. All three evasions cited are negative tests. Rejected part: full natural-language detection is not feasible; R0 Section 9 now states the heuristic limits instead of claiming broader enforcement. |
| 6 | BLOCKING | ACCEPTED | One 5-round allowance per artifact across all reviewers; a round is one artifact version; Copilot is requested only on a round's commit (protocol "Round accounting"; R0 Section 5). |
| 7 | MAJOR | ACCEPTED | Sensitive material overrides audit retention: history rewrite or hosting-service removal completed within 14 days; sanitized disposition entries; logs redacted (R0 7.2, 7.4, 7.6). |
| 8 | MAJOR | ACCEPTED | Deadlines are for completed and verified removal; gate-closure cache deletion added; refusal/withdrawal requires an access inventory (R0 7.3). |
| 9 | MAJOR | ACCEPTED | R2A′ is authority only and selects nothing; R1 freezes the candidate frame and selection rules and declares prior familiarity (including the `bench/corpus/` MediatR, Serilog, FluentValidation submodules); R2B′ selects under frozen rules and reports the excluded share of the eligible frame; R5 statements limited to included cells (amendment 001 Sections 5, 7). |
| 10 | MAJOR | ACCEPTED | Round 2 input includes the full #1371 body and acceptance criteria. R0 Section 12 adds a requirement-to-record coverage map. |
