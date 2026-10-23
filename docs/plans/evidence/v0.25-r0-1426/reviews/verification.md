# Codex verification-only pass (#1426 R0)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`). Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `bf4b1274` (round-3 fixes merged with `origin/main`), asked only to verify the round-3 dispositions.
- **Verdict:** NOT CLEAN. No new BLOCKING defect. Items 1-4 VERIFIED; item 5 not completely verified; one documentation miscount.

## Findings (verbatim)

1. **VERIFIED.** READY reads JSON and requires matching candidate, case ID, current requirement digest, `passed`, and every registered cell (`InteropScopeValidator.cs:337–342`). Off-candidate and historical-evidence controls are present (`InteropScopeTests.cs:190–191`); the former receives a real mismatched synthetic record (`:219–227`).

2. **VERIFIED.** MET requires two distinct registered interaction families and an existing website page (`InteropScopeValidator.cs:260–264`). Both identities participate in the requirement digest (`:398–400`). The relabeling control updates its digest to isolate S009 (`InteropScopeTests.cs:195–196`).

3. **VERIFIED.** Leading `/`, backslashes, and `..` path segments are rejected before file resolution (`InteropScopeValidator.cs:56–57`). Controls cover examples, reviews, and results (`InteropScopeTests.cs:192–194`).

4. **VERIFIED.** Library/SDK obligations are separate (`scope.json:1049–1056`); CLI/MCP migration modes and unavailable combinations are explicit (`:1166–1179`); migration provenance is separate (`:1196–1197`). **Documentation discrepancy:** REPORT-12 contains **14 cells**, whereas the plan (`v0.25-interop-scope-and-baseline.md:197`) and disposition (`round-3.md:57`) say 12.

5. **NOT VERIFIED completely.** READY uses the current version (`InteropScopeTests.cs:355`), the amendment positive reads the current expected value (`:73–79`), and the missing fixed initial-state packet is honestly documented (`round-3.md:58`). However, the new result helper emits **no `cells`** (`InteropScopeTests.cs:224–227`). A legitimate amendment adding an F4 cell therefore breaks the positive READY control because the validator requires that cell (`InteropScopeValidator.cs:341`). Current F4 rows have no cells, masking this gap.

**New BLOCKING defects:** none found. The helper gap and cell-count contradiction prevent an entirely clean verification.

All 49 requirement digests, fixture hashes, baseline/producer/generated seals, frozen pin, baseline derivations, and observations match. Test execution was blocked by the sandbox’s socket restriction; no test-pass claim is made.

NOT CLEAN

## Follow-up (one commit after this pass, not re-reviewed)

- F6-REPORT-12 has 14 cells, not 12: the plan now says 14. `round-3.md` keeps its original text as a record.
- The synthetic READY result records in the tests now carry `passed` for every registered cell, so a legitimate amendment that adds a cell to an F4 case does not break the positive READY control.

Both changes are one line each and were not reviewed again; the review budget (3 rounds plus one verification pass) is spent. Test execution inside the Codex sandbox was blocked; the author ran `tests/Calor.Compiler.Tests` locally: 12,475 total, 0 failed, 9 skipped (corpus and network tests skip on a bare local clone; CI expects 3 with submodules).
