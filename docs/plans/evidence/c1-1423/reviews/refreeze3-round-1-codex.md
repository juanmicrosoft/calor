# C1 #1423 PR 3 (third freeze, PR #1539), review round 1 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `72ec0690`, with read-only repository access. `origin/main` was at
the candidate `04e61f18`, and #1535 and `evidence/g3-1135-exec-2` were fetched. Under the contract
§9 independence deviation, this is an adversarial tool review, not an independent review.

An earlier run of this round was stopped before it produced a result. Its prompt wrongly called
this C1's "second and last" PR. That run is not counted, and nothing from it was used.

## Reviewer output (verbatim)

VERDICT: APPROVE

- **MINOR — docs/plans/evidence/c1-1423/inputs.json:603:** `issueStateAtFreeze` values still come from the October 8 second-freeze observation, while this freeze is October 9. Neither verifier checks issue states. Refresh the snapshot and timestamp, or explicitly label the generated states as carried forward; regenerate the manifest.
- **MINOR — docs/plans/evidence/c1-1423/inputs.json:424; docs/plans/v0.24-c1-candidate.md:230:** Byte reproducibility is still assigned to "C2 regeneration 2," whose results this PR invalidates for the new candidate. Change both references to regeneration 3 and regenerate the manifest.

Offline regeneration matches byte-for-byte; the supersession tamper is rejected and prior history is preserved verbatim. R0 placement, intervening merges, classifier exemptions before and at an immediate landing, and capacity conditions check out. Total diff: 451 changed lines, including evidence.

Remote tags and API observations remain unverified offline. .NET tests were not run under the read-only constraint.

## Dispositions

Both MINORs are fixed, and the manifest is regenerated (`--check` reports `OK`).

1. **Issue states.** The C1 agent had re-run `gh issue list` at this freeze (2026-10-09T15:33Z), and
   every state was unchanged. `observedVia` now records that re-observation.
2. **Byte reproducibility.** The `R2-BYTE-REPRODUCIBILITY` open item and the candidate document now
   assign the byte comparison to C2 regeneration 3.
