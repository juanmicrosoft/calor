# C1 #1423 re-freeze verification pass (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — docs/plans/evidence/c1-1423/generate_candidate_manifest.py:64–66:** Tag lookup fails open. `git()` returns `None` for every nonzero exit, which releasability accepts as “absent.” Injecting exit 128 or a non-commit object produces no finding. **Fix:** establish tag absence separately; reject lookup and peeling errors. Add negative controls for both.

2. **MINOR — docs/plans/v0.24-c1-candidate.md:15:** Commit time is `12:51:38Z`; `12:51:39Z` is GitHub’s merge-event time. **Fix:** use `12:51:38Z`.

Otherwise, regeneration passes; candidate identities, hashes, version, advisory, recorded delta, decisions, and six CI environment observations check out. Candidate test assertions bind non-vacuously on inspection. Local .NET execution was blocked by the read-only sandbox’s temporary-directory restriction.

## Dispositions

Scope: the re-freeze delta only (`git diff ffdebaad HEAD` on the C1 files), after the maintainer
decisions of 2026-10-07 and the merge of release preparation #1509 (`696ab824`).

1. **Fixed.** The generator now decides tag existence explicitly: `git show-ref --verify` (0 present,
   1 absent, anything else an error) and `git ls-remote --tags origin`. Any lookup or peeling error
   fails with "cannot decide whether refs/tags/v0.24.0 exists"; an existing tag must point at the
   candidate. Checked by hand in the worktree (local refs only, removed afterwards): a `v0.24.0` tag
   at `5ebdbee2` fails with G009; a `v0.24.0` ref to a tree object fails with "cannot decide"; no
   tag passes. No Python controls were added to CI: the generator is not run in CI and the PR is
   at its line ceiling. `CandidateManifestTests` independently checks the version at the candidate.
2. **Fixed.** The record gives the commit time 12:51:38Z and notes GitHub's merge time 12:51:39Z.

The pass reported everything else accurate: candidate identities, hashes, version, advisory, the
recorded #1509 delta, the decisions, and all six CI environment observations.
