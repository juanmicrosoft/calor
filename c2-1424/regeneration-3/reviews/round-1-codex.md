# C2 regeneration 3: review round 1 (Codex)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" <prompt> < <curated diff>`, run in the PR worktree on the pre-push commit 9ceadff0.

## Prompt

> You are reviewing C2 regeneration 3 (#1424) of the Calor 0.24 evidence contract, adversarially. Repository root is the current directory: branch milestone-0.24/c2-1424-regeneration-3, two commits on main b04e963f: b951d1c4 adds docs/plans/evidence/c2-1424/regeneration-2/ (211 files, the never-merged PR #1535's records, kept unchanged as history), and 9ceadff0 adds docs/plans/evidence/c2-1424/regeneration-3/ (193 files). Read-only review.
> 
> Context in the repo: docs/plans/v0.24-evidence-contract.md (contract 1.5.0: section 3 inventory, section 7 publication separation, section 8 rules incl. required subjects and the claim registry, section 9 capacity incl. the 1.5.0 exception (one more freeze and one more regeneration), regeneration-compute 1,500 per regeneration, the c2-candidate-determinism-protocol charge rule, section 10 C2 closure evidence); docs/plans/evidence/evidence-contract-1407/artifact-inventory.json; docs/plans/v0.24-c1-candidate.md (Supersession sections; candidate 04e61f18, C1 landing b04e963f), docs/plans/evidence/c1-1423/candidate-manifest.json and refreeze-3-classifier.md; docs/plans/evidence/g2-1421/ (protocol 1.5.0); scripts/determinism_protocol.py, determinism_runner.py, verify_release_adjudication.py (R2), benchmark_publication_gate.py (B2), reproducible_builds.py; tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs. Files over about 1 MB are on the orphan branch evidence/v0.24-archives (commit ca4e8df1), listed in regeneration-3/archives.json; you may not be able to read them.
> 
> Facts not yet in the commits (they will be added as new files in a follow-up commit): (a) the C1 classifier, run from a fresh clone at b04e963f with CALOR_C1_CLASSIFY_TARGET=9ceadff0, returned NOT-INVALIDATED (landing b04e963f); (b) an R2 dry run in a scratch clone: a SYNTHETIC terminal record (inventory artifacts BOUNDED or HISTORICAL-ONLY, gate rows BOUNDED, the 28 claims BOUNDED, publication block = regeneration-3/publication-candidates.json, evidence manifests = the C1 manifest and regeneration-3/raw-artifact-freeze.json) committed locally on top of 9ceadff0 with refs/remotes/origin/main moved to it locally; verify_release_adjudication.py with --expect-head --version 0.24.0 --release-notes --nuget-dir --metadata-dir --website-dir (clone A's outputs) PASSED, and --benchmark-worktree after B2's gate wrote the headline PASSED; the same record with gate:#1135 and gate:#1424 BLOCKED failed with G008 only.
> 
> Review for: (1) missing artifacts or rows (every inventory artifact exactly once; the determinism gate; claim registry vs the candidate's CHANGELOG [0.24.0] section; section 10 closure evidence; C1 knownOpenItems owned by C2); (2) laundered failures or overstated results (the determinism result vs determinism/result.json and protocol 1.5.0's rules; Tier 2's FAIL; skipped jobs; the planned Homebrew host for coverage); (3) off-candidate runs (head SHAs in regeneration-3/ci/run-*.json; local provenance); (4) publication leaks or anything the classifier would not exempt; (5) factual errors you can check (counts, hashes, minutes, dates, file names, claim line references, the regeneration-2 files being byte-identical to PR #1535's head ec82c313 if reachable). Output 'VERDICT: APPROVE' or 'VERDICT: REQUEST-CHANGES', then findings tagged BLOCKING/MAJOR/MINOR/NIT with file/field and fix. Be concrete and brief.

## Response

VERDICT: APPROVE

No BLOCKING or MAJOR findings. Verified 34 inventory rows exactly once, 28 claims, candidate SHAs, 583 runner-minutes, freeze/archive hashes, and regeneration-2’s 211 files against `ec82c313`. Recomputing the decider from all 150 archived attempts reproduces `result.json` exactly. Supplied classifier/R2 follow-up facts accepted.

- **MINOR — `regeneration-3/results.json:57–59`, `ci-test-reports`:** `VERDICTS-AGREE` and “every count” overstate agreement. Enforcement CI has 694 passed / 1 skipped; local coverage has 695 passed / 0 skipped. Remove that status and record the differing skip explicitly.

- **MINOR — `regeneration-3/results.json:235`; `regeneration.md:71`:** The B2 limitation is incorrect. Comparison uses the headline at candidate `HEAD`; later `main` publication can cause refusal, not changed output bytes. An identical headline already merged is allowed. Replace the stated limitation with that behavior.

- **MINOR — `regeneration-3/regeneration.md:9–10`:** “Every publication candidate” matched Linux CI exceeds the retained evidence. Packages, metadata, and website matched CI; headline/stamp and rendered notes matched local clones. Name those scopes separately.

Make corrections without introducing modified downstream-record commits into the classified range; refresh the freeze hashes.

## Disposition

APPROVE with three MINOR findings, all fixed in 0ceff9d49f9cf5fdd241a45d25444df9d4803cb1 (the rebuilt records commit): ci-test-reports is PASS-ONLY and records the Enforcement skip difference; the benchmark limitation states that B2 compares with the headline at the candidate and refuses (B2-07) instead of changing bytes; regeneration.md scopes which surfaces matched across clones and Linux CI and which across clones.
