# C1 #1423 review round 3 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — docs/plans/evidence/c1-1423/candidate-manifest.json:386:** Environment identities are now recorded, but whether drift invalidates evidence remains undecided. Round-2 item 2 is still unresolved. **Fix short of an amendment:** C1 can freeze the observed SDK/runtime/image tuples as acceptance requirements and provide a tested comparison that rejects mismatches or missing identities. Hosted-image provisioning need not be pinnable to reject drift.

2. **MAJOR — docs/plans/evidence/c1-1423/candidate-manifest.json:149:** This candidate cannot pass R2’s release gate: its version is `0.22.0`, but existing tag `v0.22.0` points to `72a0a855`, not `5ebdbee2`. `verify_release_adjudication.py:344` unconditionally rejects that mismatch with `G009`. A later version bump invalidates this freeze. **Fix:** merge release preparation, including the new version and required notes, before freezing a replacement candidate; add a conflicting-tag control to C1 acceptance.

## Dispositions

1. **Not adopted; escalated to the maintainer.** Freezing the observed tuples with exact-equality
   acceptance would fail every C2 job once hosted images roll (they update weekly and already differ
   by label within one run), and choosing any tolerance is a rule change, which needs a #1407
   amendment that C1 cannot make (§9). The record keeps the observed tuples, requires C2 to compare
   against them, and states that C1's acceptance depends on the maintainer's decision.
2. **Confirmed and recorded as a release blocker; not fixable inside C1.** `scripts/verify_release_adjudication.py`
   fails `G009` when tag `v<version>` points elsewhere; `v0.22.0` points at `72a0a855` and the
   candidate declares 0.22.0. The manifest gains a `releasability` block (version and tag commit
   read from git by the generator) and open item `CANDIDATE-NOT-RELEASABLE`; the record says the
   candidate can carry evidence but cannot be published, and that release preparation plus a
   re-freeze (C1's second PR) or an amendment must be decided before C2 starts. The candidate was
   fixed by the task as origin/main at the start of C1; this PR does not merge release preparation
   (no release work is authorized here). No conflicting-tag control was added to the C# tests,
   because CI checkouts do not reliably fetch tags; the generator records the observation.

This was review round 3, the last round under the §9 ceiling. A verification-only pass follows.
