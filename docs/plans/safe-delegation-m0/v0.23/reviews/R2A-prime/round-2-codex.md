# R2A′ adversarial review — round 2 (Codex)

**Date:** 2026-10-01. **Reviewer:** Codex CLI 0.159.2 (`codex exec -s read-only`),
model `gpt-6.1-sol`, hostile-reviewer prompt plus a check of every round-1
disposition.
**Reviewed commit:** `aa2638f9`.
**Verdict from reviewer:** 0 blocking / 5 major / 1 minor.
**Stopping rule:** zero blocking objections, so the review loop stops after
this round (cap was 5). The majors and the minor were still addressed below.

## Reviewer's check of round-1 dispositions

| R1 # | Reviewer status | Note |
|---|---|---|
| 1 | Resolved | `NOT_REACHED` pending R0 with activation and voiding rules |
| 2 | Partially resolved | Image digest, resource limits, retry classification unfrozen → R2-2 |
| 3 | Partially resolved | Global rules can still be tailored → R2-1 |
| 4 | Resolved | Collection boundary disclosed; no task-content inspection evidenced |
| 5 | Resolved | License-file identities pinned; translation obligations deferred appropriately |
| 6 | Partially resolved | AUP research permission does not cover acknowledged personal data → R2-5 |
| 7 | Partially resolved | Continuation output could not seed another continuation → R2-3 |

## New or remaining objections

| # | Severity | Objection | Disposition |
|---|---|---|---|
| 1 | major | A global rule can still encode a sample-specific exclusion (e.g. "exclude Unity" removes a known primary). Require independent review of each rule against the disclosed sample; prohibit or separately report exclusions based on Calor support. | **Accepted in part.** §3.1 now requires review of each removal rule by the R1 methods reviewer, shown the repositories it would remove, before application. Calor-support exclusions cannot be prohibited because R3's support intersection is part of the registered design (#1374); instead they are defined as a Calor coverage cost and must be reported per repository, separately from comparative results. |
| 2 | major | C12 still permits outcome-changing choices: digest recorded only at run time, no resource limits, undefined retry "patterns", no precedence for mixed errors; do not assert the restriction cannot favor Calor. | **Accepted.** §3.4 now pins the SDK image digest (`sha256:35d40304…a7d29`, retrieved 2026-10-01), fixes 4 CPU / 16 GB / 64 GB limits, lists the exact infrastructure regexes, and gives compiler/MSBuild/SDK error lines precedence. The bias statement now says survival may correlate with Calor suitability and must be reported as a possible selection bias. |
| 3 | major | Continuation chaining broken: `continue_walk()` output lacks `candidate_walk_order`, so a second continuation fails or restarts at rank 132. | **Accepted.** Every snapshot and continuation now carries `candidate_walk_order`, its SHA-256, `base_snapshot`, and `next_rank`; continuation validates the order hash and freeze commit. Verified offline with a stubbed evaluator: continuation 1 walked ranks 132–134, continuation 2 walked 135–137, no overlap. |
| 4 | major | API failures masquerade as license exclusions: `license_identity()` mapped every error to "no license", and the JSON cannot show whether any of the 50 C6 failures were retrieval errors. | **Accepted.** `license_identity()` now returns `not_found` only for HTTP 404 and aborts on any other error; `license_lookup_status` is recorded. Audit of the existing snapshot (§4): 47 of 50 C6 failures have a repository-level license outside the allowlist, 2 have no license at either level, and 1 (`CesiumGS/cesium-unity`) is a genuine repo-level/pinned-SHA detection mismatch. None is a retrieval error, so the selection is unchanged. |
| 5 | major | Personal-data basis unsupported: the AUP research permission covers non-personal data, yet owner logins are acknowledged personal data and §5.3 allowed later contributor pseudonyms. | **Accepted.** §5.1 states a separate basis per data class: research permission for non-personal data; for repository identifiers containing a login, use limited to the purpose the owner authorized by publishing the project under that name (AUP §8). Contributor-level data now has no basis under R2A′; §5.3 permits it only if R0/R2B′ first establishes one. |
| 6 | minor | Summary records misdate license evidence (enumeration time used for later license lookups). | **Accepted.** Summary records now carry `enumeration_retrieval_utc` and `license_identity_retrieval_utc` separately. |

**Outcome:** 0 blocking; all 5 majors and 1 minor addressed. No further
rounds run (2 of 5 used).
