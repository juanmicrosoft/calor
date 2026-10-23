# Amendment 1.5.0 prep (PR #1537), review round 3 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `44f9e7c0`, with read-only repository access. Under the contract
§9 independence deviation, this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: APPROVE

- **MINOR — docs/plans/evidence/g2-1421/README.md:399:** The controls summary says a run lasting as long as its process timeout cannot be a harness cut. The new rule correctly ignores duration, and the boundary control accepts a deadline cut whose termination runs late. **Fix:** replace this claim with "an invocation given the process-timeout bound is not a harness cut"; re-seal the packet.

- **MINOR — docs/plans/v0.24-evidence-contract.md:761:** "Every case the attempt touched read DISAGREE" misstates regeneration 2. Only 470 cases disagreed; its 1,170 observed cells and three artifact hashes matched and remained INCOMPLETE. **Fix:** say "the 470 cases whose values were filled read DISAGREE"; re-seal the contract.

No BLOCKING or MAJOR findings. Main's D001–D016 validator passes; seals and exception hashes match. Replay exactly reproduces the published result and the expected INCOMPLETE result. Budget is 3,180 ≤ 3,200; execution counting and PR #1537 merge binding pass. All changed lines total 735, below the limit.

46 read-only controls passed, with mutation and supplemental runner checks. Filesystem-writing and .NET tests were not run under the read-only constraint.

## Dispositions

Both MINORs are fixed, using the reviewer's wording.

- The README now says an invocation given the process-timeout bound is not a harness cut.
- The contract's §9 text now says "the 470 cases whose values were filled read `DISAGREE`", and
  adds that the cut attempt's observed cells and artifact hashes matched the other attempts.

Both packets are re-sealed. The protocol controls pass (63 tests), and the protocol validates
against `origin/main`.
