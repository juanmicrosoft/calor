# Codex review, round 1 of 3 (PR #1615)

**Reviewer:** OpenAI Codex, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. **Independence:** reduced.
No reviewer independent of the maintainer reviewed this work.
**Reviewed:** the PR at `a8932d3ed` (measurement commit `b31bd3253` plus the record commit). Prompt: `round-1-prompt.md`.
**Verdict:** CHANGES-REQUESTED (BLOCKING 0, MAJOR 1, MINOR 3, NIT 0).

## Findings (verbatim)

[MAJOR] `docs/plans/evidence/v0.25-f1-943/ready/d1d7afde/ready-record.json:93` — All six READY records cite CI run **39323630837** “on the candidate.” That run tested **48cae911**, with 14,606 compiler tests; these records report 14,696 tests for **d1d7afde**. — The READY provenance points to the superseded candidate. — Fix `tools/make_ready.py.txt:518` to cite **39516188077**, verify its `headSha`, and regenerate all six records and their freeze hashes.

[MINOR] `docs/plans/evidence/v0.25-f2-906/ready/d1d7afde/cases/F2-INTERP-09.json:13` — The judgement says no amendment records the count changes and “the case is not passed,” while `result` is `passed` and every `registerAfterPreserved` value is true. T1 register 1.0.3 supplies the amendment. — The retained judgement contradicts the supported result. — Make the producer’s judgement conditional and cite the re-registration when all counts match.

[MINOR] `docs/plans/evidence/v0.25-regeneration/d1d7afde/publication-candidates.json:14` — The note claims packages, metadata **and website** used SDK 10.0.401/runtime 10.0.12 across local and CI builds. Both CI website manifests instead record **10.0.402/10.0.13**; `local/compare-a-ci-website.md` explicitly reports this difference. `regeneration.md:35` repeats the overclaim. — Byte identity holds, but the stated toolchain provenance is false. — Restrict the .NET pin claim to packages and metadata, and describe the website’s recorded toolchain separately.

[MINOR] `docs/plans/evidence/v0.25-regeneration/d1d7afde/t1-aggregate.json:574` — F4’s aggregate reports “no result record” for F4-ITER-04 and leaves `readyResultRecord` null, although its retained record correctly says `skipped`. The frozen aggregator searches only for `passed`. — This presents an approved scope 1.5.0 disposition as missing evidence. — Add an explicit annotation explaining the aggregator limitation and linking the skipped record; preserve the frozen producer.

Counts: BLOCKING 0, MAJOR 1, MINOR 3, NIT 0.
VERDICT: CHANGES-REQUESTED

## Disposition

Every file must be added once and never modified, because the V025 classifier diffs every commit. The fixes
therefore rebuilt the PR's commits from the same raw measurements and force-pushed them. No measurement was re-run.

1. **MAJOR, test-run citation: fixed.** The producer had a hard-coded run id left over from the 48cae911 re-proof.
   Every READY record's `tests` rows now cite test.yml run 39516188077, whose `headSha` is d1d7afde
   (`ci/run-39516188077.json`). The totals were already that run's (Compiler 14,696).
2. **MINOR, F2-INTERP-09 judgement: fixed.** The judgement is now conditional. When every count matches, it cites
   T1 register 1.0.3's re-registration (D-F2-INTERP-09-RECANDIDATE, scope 1.5.0), and the measurement note describes
   the re-registration.
3. **MINOR, website toolchain: fixed.** `publication-candidates.json`, `results.json` and `regeneration.md` now claim
   SDK 10.0.401 / runtime 10.0.12 only for packages and metadata. They say the website is built by Node and runs no
   .NET, and that the CI website job records the runner image's .NET (10.0.402 / runtime 10.0.13).
4. **MINOR, T1 aggregate and F4-ITER-04: fixed.** The aggregator is a frozen producer and its output stays
   unchanged. `t1-aggregate-annotation.json` lists each case the aggregator cannot count, here F4-ITER-04, with its
   retained record and result (`skipped`) and the reason: the aggregator accepts only `passed`.
   `regeneration.md` points to it.
