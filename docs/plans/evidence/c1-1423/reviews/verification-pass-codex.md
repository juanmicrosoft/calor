# C1 #1423 verification pass (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidationTests.cs:204:** Round-2 item 1’s regression control passes without the fix. The fixture never creates a `.gitmodules` mapping, so `submodule.bench/corpus/MediatR.ignore=all` has no effect; `diff.ignoreSubmodules=all` alone does not hide this `diff-tree` change. **Fix:** commit the mapping in the candidate fixture, assert that the unprotected diff hides the gitlink change, and verify the control fails when `--ignore-submodules=none` is removed.

All other dispositions match HEAD; the escalated limitations remain honestly recorded.

## Dispositions

1. **Fixed (test only).** The fixture now commits a `.gitmodules` entry for `bench/corpus/MediatR`
   before the candidate, sets `submodule.bench/corpus/MediatR.ignore=all`, and asserts that a plain
   `git diff-tree` hides the gitlink bump before asserting that the classifier reports it.
   Discrimination checked locally: with `--ignore-submodules=none` removed from
   `CandidateInvalidation.Diff`, `AGitlinkBumpInvalidatesDespiteSubmoduleIgnoreConfig` fails; with
   it, the test passes. No production or classifier logic changed after the pass.

The pass reported every other disposition as accurately implemented and the escalated
limitations as honestly recorded. Under the §9 ceiling (three rounds and one verification pass)
no further Codex pass was run; the maintainer's review decides the remaining escalations
(environment binding, releasability).
