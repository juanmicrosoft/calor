# Codex review, round 2 of 3 (PR #1581)

**Reviewer:** OpenAI Codex, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. **Independence:** reduced.
**Reviewed:** the rebuilt PR at `0f83a3e5a` (measurement commit `4dae3be25`, round-1 record `e53cfc481`, record commit
`0f83a3e5a`). Prompt: `round-2-prompt.md`.
**Verdict:** CHANGES-REQUESTED (BLOCKING 2, MAJOR 0, MINOR 0, NIT 0). Codex confirmed the F2 disposition, the
review-record fix, `ready_problems` matching the disclosed gaps, F4, the R2 dry-run logs, the classifier paths, and
the frozen and archived hashes.

## Findings (verbatim)

- [BLOCKING] docs/plans/evidence/v0.25-regeneration/results.json:139 — `provenance-candidate-manifest` remains PASSED without retained classifier evidence. At reviewed HEAD `0f83a3e5a`, neither `classifier/` nor `classifier.txt` exists; the available manifest run matched zero tests. Round-1 disposition 4 therefore remains unsubstantiated — The PASSED row and claimed 107 passing tests lack evidence — Add the promised fresh landing-clone target, classifier verdict, logs and TRX before retaining PASSED.

- [BLOCKING] docs/plans/evidence/v0.25-f6-1144/ready/cases/F6-REPORT-05.json:22 — The aggregation cell claims the project reports `failedFiles`, but `MigrateOutput` emits `totalFiles`, `successCount` and `failureCount`. The cited test checks only per-file paths; `W1-F6-project/mcp-migrate` retains no project totals — This cell does not establish its own stated aggregation behavior, leaving F6’s passed case and READY claim unsupported — Retain a candidate measurement checking the actual aggregate fields against per-file outcomes, correct `F6_CELLS`, and regenerate the records; otherwise block the case.

F2’s disposition is correct, and the missing-review disposition is fixed. `ready_problems` matches the disclosed approval and failed-case gaps. F4 remains correctly failed; the R2 dry-run logs match their stated outcomes. All rebuilt commits add only allowed files, and the frozen and archived hashes match.

Counts: BLOCKING 2, MAJOR 0, MINOR 0, NIT 0.
VERDICT: CHANGES-REQUESTED

## Disposition

The commits were rebuilt again. Both fixes are additions; no earlier measurement changed.

1. **Classifier evidence (BLOCKING): fixed.** `classifier/` now holds the fresh landing-clone classifier run against
   the record commit: the explicit target, `git log` of the range, the range test log with its verdict and TRX, and
   the V025CandidateManifestTests and ReleaseCandidateInvalidationTests log and TRX. A record cannot classify the
   commit that contains it, so it lands in a last commit that adds only `classifier/` files. In round 2 it was still
   planned, not yet committed.
2. **F6-REPORT-05 aggregation (BLOCKING): fixed with a candidate measurement.** The cell misnamed the field
   (`failedFiles` belongs to calor_batch). `tools/f6_aggregates.py.txt` runs, on the candidate (measurement clone, the
   same Release build), MCP `calor_migrate` (phase convert) and `calor_batch` (convert, dryRun) through
   `calor mcp --stdio` on the F6 tests' two projects. It checks the project totals against the per-file entries:
   `measure/f6-aggregates/f6-aggregates.json`, all consistent. calor_migrate: totalFiles 2, failureCount 2 = 2
   refused files (mixed project); totalFiles 2, successCount 1, failureCount 0 with statuses success and partial
   (clean project). calor_batch: the summary's totalFiles, successfulFiles, partialFiles and failedFiles agree with
   files[]. The F6-REPORT-05 `aggregation` and `protocol-error` cells and the F6-REPORT-06 `per-file-and-aggregate`
   cell now cite it.
