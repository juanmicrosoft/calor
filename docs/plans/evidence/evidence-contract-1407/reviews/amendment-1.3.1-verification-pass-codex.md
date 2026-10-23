# Amendment 1.3.1, revert-only changes for #1502 and #1503 — verification pass (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...8f0df1d1` (PR #1504). This was a verification-only pass on the round-1 fix, which made the negative controls check their own subjects. The prompt also asked the reviewer to re-check fidelity to the maintainer decision and the packet and text hashes.

## Codex output (verbatim)

VERDICT: APPROVE

No BLOCKING, MAJOR, MINOR, or NIT findings.

- The prior MINOR is resolved at `tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs:1655`: all 22 controls require a clean pristine contract and C011 on the expected subject. Subjects match the validator, including swapped PRs, moved #1497, copied #1498, and the duplicate subject without a PR suffix (`EvidenceContractValidator.cs:137`, `:232`). No new defect found.
- The amendment faithfully preserves the decision: exact base SHAs and hunks, one revert-only change per PR, assertion-only fixture corrections for #1503, one verification pass requiring APPROVE, stopping rule 1 otherwise, freeze before and after the pass, and value 5 for these two PRs only (`docs/plans/evidence/evidence-contract-1407/contract.json:337`, `:338`).
- All three packet hashes and all registered exception and charge-rule text hashes match. Existing 1.3.0 exceptions and base ceilings remain unchanged.

Tests could not execute: the read-only sandbox blocked MSBuild’s temporary-directory creation. Verification was by source inspection and read-only hash checks.

## Resolution

No change was requested. This verification pass is the final review. After it, no content changed; only this record was added.
