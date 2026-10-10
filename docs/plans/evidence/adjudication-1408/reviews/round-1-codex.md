# Codex adversarial review, round 1 (#1408 A1)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run from the A1 worktree. Reviewed commit: `1d409c0d79b07c2fde72e5755b15553e022ae5d4`.

## Prompt

You are an adversarial reviewer for the Calor repository (current directory; a git worktree). Review round 1 of the 0.24 A1 terminal adjudication record for issue #1408. Read-only: do not modify files.

What was added (branch head vs origin/main 12200dd7): docs/plans/evidence/adjudication-1408/ — terminal-record.json (the record), generate_terminal_record.py.txt (its generator; evidence hashes from git blobs at 12200dd7), capacity-audit.md (terminal condition 9), README.md (maintainer sign-off packet), r2-dry-run/ (R2 release-gate and terminal-validator runs).

Governing documents: docs/plans/v0.24-evidence-contract.md (§3 inventory, §7 release path, §8 terminal success predicate and required subjects, §9 independence deviation and capacity ceilings with exceptions and amendment log) and docs/plans/evidence/evidence-contract-1407/contract.json + artifact-inventory.json; the R2 gate scripts/verify_release_adjudication.py and docs/plans/evidence/r2-1410/release-gate.md; the validator tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs ValidateTerminalRecord; C1 docs/plans/v0.24-c1-candidate.md and docs/plans/evidence/c1-1423/candidate-manifest.json; C2 regeneration 3 docs/plans/evidence/c2-1424/regeneration-3/ (results.json, claim-registry.json, raw-artifact-freeze.json, ledger.json, regeneration.md).

The record proposes MILESTONE-FAILED because terminal condition 9 ("No §9 ceiling was exceeded without a merged amendment raising it") does not hold: review-rounds-per-pr exceeded without exception on #1508 and #1479 (and others), per the committed review records cited in capacity-audit.md.

Find defects. In particular:
1. Is the condition-9 finding correct? Check the cited review records and the contract's amendment text (1.3.0, 1.3.1, 1.3.2, 1.5.0) and contract.json exceptions. Is there any amendment, exception, or contract text that covers #1508 or #1479 that the audit missed? Is the counting rule applied consistently with how the contract's own amendments counted? Is any cited record misread?
2. Is MILESTONE-FAILED the outcome the contract requires given the evidence, or is anything else (e.g. condition 1, 7) misstated?
3. Does every required subject appear exactly once (every inventory artifact, gate:#<issue> for every contract child other than 1408, every registered claim), with allowed outcomes, HISTORICAL-ONLY only for historical-only artifacts, independence = reduced-maintainer-adjudicated, the published limitation verbatim?
4. Are the per-row evidence pointers real, with correct sha256 (recompute a sample against `git show 12200dd7cf84cffd33a7bbe133b65b3cd03c3c2e:<path> | shasum -a 256`), and do row limitations, defect adjudications, and claim adjudications match the evidence? Flag any claim row whose BOUNDED adjudication the evidence does not support (it should then be BLOCKED).
5. Any statement in README.md or capacity-audit.md that is false, overstated, or unsupported; any use of the wording "independently adjudicated" or "independently verified" outside the contract's verbatim limitation.
6. Anything in the record that would let a future publication proceed improperly.

Output: a list of findings, each labeled BLOCKING, MAJOR, MINOR, or NIT, with file:line or JSON path and a concrete fix. End with exactly one line "VERDICT: APPROVE" or "VERDICT: REQUEST-CHANGES".

## Final answer (verbatim)

Condition 9 is correctly found false: the cited #1508/#1479 records support the counts, and no amendment or exception covers them. `MILESTONE-FAILED` is required. All 75 subjects appear exactly once; outcomes and independence limitations are structurally correct. All 139 distinct evidence pointers matched their hashes at `12200dd7`. No extra independence wording was found.

Paths below are relative to `docs/plans/evidence/adjudication-1408/`.

1. **BLOCKING — README.md:37–42, 126–144; terminal-record.json `signOffDecisions[AD-4].ifRejected`.** The packet offers publication after a maintainer ruling that contradicts the amendments’ review-round accounting. §8 expressly prevents #1408 from weakening a condition. The counterfactual passes despite retaining `failedConditions: [9]` and condition 9 false; that demonstrates the gate’s limited checks, not contractual eligibility. **Fix:** remove this proposed success/publication route and describe the counterfactual solely as a gate control.

2. **MAJOR — terminal-record.json `adjudications[subject="gate:#1421"]`, `adjudications[subject="gate:#1423"]`, `terminalPredicate[condition=3]`.** The audit recognizes unamended overruns but leaves every gate `BOUNDED`. Contract §8 stopping rule 1 requires an amendment before further work or recording the gate `BLOCKED`. Neither decisive overrun received an amendment. **Fix:** record the affected gates as `BLOCKED`, apply the same rule to the other established overruns, and update condition 3, `failedConditions`, counts, generator, and README. Retain `MILESTONE-FAILED`.

3. **MAJOR — terminal-record.json:168–169, 274–275; README.md:54, 63.** AD-1 substitutes checking the release PR’s products for the requirement that every §7 surface consume the identity. R2 §6 documents this unresolved conflict; it supplies no contractual exemption. An A1 sign-off cannot amend the predicate after the freeze. Also, rejecting AD-1 does not prohibit merging a truthful failed record, contrary to `ifRejected`. **Fix:** record condition 7 as unmet under the frozen text and remove the proposed waiver and failed-record merge prohibition.

4. **MAJOR — terminal-record.json `adjudications[subject="claim:reproducible-release-evidence-and-gate"]`.** The frozen claim says a release can be published **only** through the gate. Its cited evidence, R2 §5, explicitly identifies dispatchable older workflows with access to the repository-scoped NuGet credential. The row acknowledges that enforcement is limited to workflows on `main`; this does not support the claim’s exclusive publication guarantee. **Fix:** adjudicate the frozen claim `BLOCKED` unless evidence establishes that the bypass authority was removed. Update the related release-path defect adjudication and predicate.

5. **MAJOR — terminal-record.json:286–287; `adjudications[subject="claim:no-benchmark-results-published"]`.** Leaving the benchmark PR unmerged does not prevent publication of its results. §7 identifies the bot PR as the benchmark publication surface. The workflow pushes the headline to a public branch and opens that PR; both expose the benchmark result. AD-3 also has no machine-enforced restriction in the publication hashes. **Fix:** mark this claim `BLOCKED` for the proposed publication path; remove the assertion that withholding the merge preserves it. Keep the failed record’s prohibition on publication.

6. **MINOR — README.md:154; terminal-record.json:1921.** `reviews/classifier.txt` does not exist at branch head. The cited C2 classifier record covers target `0ceff9d4`, not this A1 branch, so it cannot substantiate “A1 ran it on its own branch.” **Fix:** retain the actual A1 classifier output with its anchor and target identity, or remove the unsupported completed-check statement.

7. **MINOR — README.md:150–151; r2-dry-run/negative-control-blocked-row.txt.** Changing only one row of the committed failed record cannot produce `T001`: `ValidateTerminalRecord` returns before T001/T002 checks unless the outcome is `MILESTONE-SUCCEEDED`. The negative control necessarily used another outcome or input. **Fix:** identify the success variant used and retain its exact mutation/input; distinguish schema validation of the failed record from the success-only checks.

VERDICT: REQUEST-CHANGES

## Disposition

- 1 BLOCKING (success route offered): accepted. The success route was removed; the counterfactual is described as a gate control only.
- 2 MAJOR (overrun gates must be BLOCKED under stopping rule 1): accepted. Seven gates are BLOCKED.
- 3 MAJOR (AD-1 cannot waive condition 7): accepted. Condition 7 is recorded as not met.
- 4 MAJOR (the exclusive-gate claim is contradicted by R2 §5): accepted. claim:reproducible-release-evidence-and-gate is BLOCKED.
- 5 MAJOR (the benchmark PR exposes a result): accepted. claim:no-benchmark-results-published is BLOCKED.
- 6 MINOR (no A1 classifier output): accepted. reviews/classifier.txt was added.
- 7 MINOR (the negative-control description): accepted. The README says the control used the earlier MILESTONE-SUCCEEDED draft.

Because subjects must now be BLOCKED, A1 stopped here as instructed: no further rounds, no push, no PR.
