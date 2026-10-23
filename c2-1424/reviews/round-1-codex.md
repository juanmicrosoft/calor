# C2 review round 1 (Codex)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" <prompt> < <curated diff of the C2 record files>` on commit 5b428a79 (pre-push; not retained after amendment). First attempt hit the Codex usage limit; re-run after the reset.

## Prompt

> You are reviewing gate C2 (#1424) of the Calor 0.24 evidence contract: "Regenerate all evidence on the frozen candidate". Repository root is the current directory (branch milestone-0.24/c2-1424-regeneration, one commit on top of main fd34f41f). Read-only review.
> 
> Context you can read in the repo: docs/plans/v0.24-evidence-contract.md (contract 1.3.2: section 3 inventory, section 7 release path / publicationSeparation, section 8 rules, section 9 capacity incl. regeneration-compute 1,500 runner-minutes and the c2-candidate-determinism-protocol chargeRule, section 10 C2 closure evidence), docs/plans/evidence/evidence-contract-1407/artifact-inventory.json, docs/plans/v0.24-c1-candidate.md and docs/plans/evidence/c1-1423/candidate-manifest.json (candidate 696ab82470626164979a07792ed74932ab88d9e6; knownOpenItems), docs/plans/evidence/g2-1421/ (protocol.json, cases.json, README), scripts/determinism_protocol.py, scripts/determinism_runner.py, scripts/verify_release_adjudication.py, scripts/benchmark_publication_gate.py, tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs. All C2 output is under docs/plans/evidence/c2-1424/ (188 files; large archives are data). The diff on stdin covers the C2 record files (regeneration.md, plan.json, ledger.json, results.json, claim-registry.json, determinism-registry-drift.json, publication/wording-scan.json, fresh-clone/comparison.json); read any other file under docs/plans/evidence/c2-1424/ you need (ci/run-*.json, raw-artifact-freeze.json, publication/candidate-packages.json, regenerated/**).
> 
> Review the C2 record for:
> 1. Missing artifacts: every artifact in artifact-inventory.json has exactly one row in results.json with an honest status; nothing required by the contract (section 8 claim registry, section 10 closure evidence, C1 knownOpenItems owned by C2) is silently omitted.
> 2. Laundered failures: any failure, infrastructure failure, skip, refusal, or non-reproducibility reported as success; any re-run that is actually run-until-green; denominators changed.
> 3. Off-candidate runs: every CI run and local result must come from 696ab824; check head SHAs in ci/run-*.json and the local provenance claims.
> 4. Publication leaks: anything published, tagged, released, deployed, or a publication PR opened; anything under c2-1424 that would invalidate the candidate under CandidateInvalidation.cs.
> 5. Overclaiming: any statement stronger than the evidence (e.g. "matched", "reproduced", "VALIDATED", "deterministic") or a factual error you can check against the files (counts, hashes, minutes, dates, file names).
> Also check the specific reasoning of the BLOCKED rows (determinism registry drift and the decider; R2 G012 wording; B2-08; performance; packages/website reproducibility) against the scripts.
> 
> Output: a verdict line "VERDICT: APPROVE" or "VERDICT: REQUEST-CHANGES", then findings, each tagged BLOCKING, MAJOR, MINOR, or NIT, with file and line or field, what is wrong, and the fix. Be concrete and brief.

## Response

VERDICT: REQUEST-CHANGES

- **MAJOR — `docs/plans/evidence/c2-1424/regeneration.md:105`; `plan.json`, `alsoRequired[classifier]`:** The claimed `classifier.txt` is absent, and no classifier execution or verdict is recorded. C1 requires this check from the trusted landing commit. Run it against the reviewed C2 commit from `fd34f41f`, retain the output, and reference it in the ledger and freeze manifest.

- **MAJOR — `results.json`, `statusVocabulary.REGENERATED-MATCHED` and artifact statuses:** The definition promises byte equality, but `ci-test-reports` explicitly has differing bytes; `roundtrip-reports` has only one successful regeneration; and `sdk-consumer-check` records job success without a byte comparison. `tier2-corpus-verification` reports producer failure but lacks `REGENERATED-FAILING`. Use separate statuses for regeneration, verdict agreement, byte agreement, and failure; preserve the failed and skipped attempts.

- **MAJOR — `excluded-coverage-xml.json:2`; `raw-artifact-freeze.json`, `publicationCandidates.nuget-packages.bytesRetained`:** Raw coverage reports and package bytes remain only in expiring Actions artifacts or scratch storage. Hashes cannot substitute for durable artifacts. The inventory explicitly requires a durable candidate-package store and includes coverage in release-quality reports; §10 requires durable attempts. Retain these bytes unpublished, compressed and deduplicated if needed, and include their locations and hashes in the freeze.

- **MINOR — `regeneration.md:94–95`; `results.json`, `candidate-packages.blocked`:** Retaining publication bytes is presented as an alternative remedy for the hash gate. It fixes retention, but the frozen publication workflows rebuild packages and the website before comparing hashes; they do not consume the retained bytes. State that this remedy leaves reproducibility blocked unless the build or publication consumer changes, requiring a new candidate.

- **MINOR — `ci/run-37644886479.json`, macOS jobs’ `environmentObserved`; `ledger.json`, corresponding `dotnetObserved`:** Runtime `10.0.12` is incorrectly listed as an SDK, while the runtime list is empty. The retained logs distinguish installation of runtime `10.0.12` from SDK `10.0.401`. Correct all three macOS consumer records and the ledger summary.

- **MINOR — `ledger.json:25`; `regeneration.md:22–26`:** The determinism failure is attributed to the stated 15:14–15:32 outage, but its run record was already completed by 15:12:01. The files establish that no attempt started, not that this outage caused it. Retain supporting incident/annotation evidence or label the scheduling failure’s cause unconfirmed. The “not an execution” conclusion remains supported.

## Disposition

All six findings addressed: (1) classifier run from fd34f41f recorded in classifier.txt; (2) results.json status vocabulary split into REGENERATED / BYTES-MATCH / VERDICTS-AGREE / SINGLE-RUN / PASS-ONLY / REGENERATED-FAILING / NOT-REPRODUCIBLE; (3) raw coverage XML and both .nupkg files committed (gzip-wrapped); (4) regeneration.md and results.json state that retention does not unblock publication; (5) SDK/runtime extraction fixed from download URLs; (6) determinism-run cause labeled unconfirmed, incident and annotation records committed.
