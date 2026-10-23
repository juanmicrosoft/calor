# C2 regeneration 2: review round 1 (Codex)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" <prompt> < <curated diff>`, run in the PR worktree. Round 1 reviewed 7a546fda; round 2 reviewed 1a306b52 (the rebuilt single records commit). Both pre-push.

## Prompt

> You are reviewing C2 regeneration 2 (#1424) of the Calor 0.24 evidence contract, adversarially. Repository root is the current directory: branch milestone-0.24/c2-1424-regeneration-2, one commit (7a546fda) on main 7eca4868 adding only docs/plans/evidence/c2-1424/regeneration-2/ (196 files). Read-only review.
> 
> Context in the repo: docs/plans/v0.24-evidence-contract.md (contract 1.4.0: section 3 inventory, section 7 publication separation, section 8 rules incl. required subjects and the claim registry, section 9 regeneration-compute 1,500 per regeneration, at most 2, and the c2-candidate-determinism-protocol charge rule, section 10 C2 closure evidence); docs/plans/evidence/evidence-contract-1407/artifact-inventory.json; docs/plans/v0.24-c1-candidate.md (Supersession; candidate 5e52d8ab, C1 landing 7eca4868) and docs/plans/evidence/c1-1423/candidate-manifest.json (knownOpenItems); docs/plans/evidence/g2-1421/ (protocol 1.4.0, rules incl. harnessCutInconsistency, retryPolicy); scripts/determinism_protocol.py, determinism_runner.py, verify_release_adjudication.py (R2), benchmark_publication_gate.py (B2), reproducible_builds.py; tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs. Regeneration 1's records (history) are in docs/plans/evidence/c2-1424/. Files over about 1 MB are on the orphan branch evidence/v0.24-archives (commit e1c65e27), listed in archives.json; you may not be able to read them.
> 
> Facts not yet in the commit (they will be added as new files in a follow-up commit): (a) the C1 classifier, run from a fresh clone at 7eca4868 with CALOR_C1_CLASSIFY_TARGET=7a546fda, returned NOT-INVALIDATED; (b) an R2 dry run in a scratch clone: a SYNTHETIC terminal record (all inventory artifacts BOUNDED or HISTORICAL-ONLY, gate rows BOUNDED, the 28 registered claims BOUNDED, publication block = publication-candidates.json) committed locally on top of 7a546fda with refs/remotes/origin/main moved to it locally; verify_release_adjudication.py with --expect-head --version 0.24.0 --release-notes --nuget-dir --metadata-dir --website-dir (clone A's outputs) PASSED, and --benchmark-worktree after B2's gate wrote the headline PASSED; a second synthetic record with gate:#1135 and gate:#1424 BLOCKED FAILED with G008 as expected. An earlier draft of this commit had copied regeneration 1's headline hashes; the dry run caught it (G011) and the records were corrected (ledger.json deviations).
> 
> Review for: (1) missing artifacts or rows (every inventory artifact exactly once; gates; claim registry vs the candidate's CHANGELOG [0.24.0] section; section 10 closure evidence; C1 knownOpenItems owned by C2); (2) laundered failures (the determinism FAILED row; Tier 2's FAIL; the coverage and Z3-download second attempts; skipped jobs) - check the determinism cause analysis against result.json and the protocol's rules; (3) off-candidate runs (head SHAs in ci/run-*.json; local provenance); (4) publication leaks or anything under c2-1424 the classifier would not exempt; (5) overclaiming or factual errors you can check (counts, hashes, minutes, dates, file names, the claim line references). Output 'VERDICT: APPROVE' or 'VERDICT: REQUEST-CHANGES', then findings tagged BLOCKING/MAJOR/MINOR/NIT with file/field and fix. Be concrete and brief.

## Response

VERDICT: REQUEST-CHANGES

- **MAJOR — `ci/run-*.json`, `jobs[].environmentObserved.imageVersion`; `ledger.json`, `ci[].imagesObserved`:** All 50 logged jobs record the **Runner Image Provisioner** version as the image version. For example, Windows x64 records `20260901.588`; its actual image is `20260925.250.1`. Parse the **Runner Image** block and correct the summaries. This affects C1’s `TOOLCHAIN-AND-IMAGES` evidence.

- **MINOR — `results.json`, determinism gate `cause`; `regeneration.md:70`:** The 1,174 INCOMPLETE cases were not all unobserved. The cut invocation retained all 1,170 cells and three artifact hashes; invalid contributions cannot establish agreement. The remaining case is the absent `oracle-isolated` invocation. Correct the explanation while retaining FAILED, `complete: false`, and all four OPEN rows.

- **MINOR — `determinism/attempt-timing.json`, Linux x64 `sumSeconds`:** These sums omit `compiler-verifier`. Totals across all profiles are **2,377 seconds** for j1 and **1,541 seconds** for j2, versus 1,118 and 751 recorded. Include that profile or explicitly label the field as a two-profile subtotal.

- **NIT — `claim-registry.json`, `rule` and `details.*.lines`:** The rule cites contract 1.3.2; this regeneration uses 1.4.0. Line references use rendered-notes coordinates although `source.path` names CHANGELOG.md, whose section starts at line 7. Declare section-relative coordinates or reference the rendered notes.

All 34 inventory artifacts appear exactly once. Local and archived hashes verify; replaying the decider reproduces `result.json` exactly. No off-candidate CI run or publication leak was found. The announced classifier/R2 follow-up is acknowledged.

Apply corrections before merge through an amended/squashed addition commit, and refresh the freeze hashes; later modifications to these records would fail the classifier’s additions-only exemption.
