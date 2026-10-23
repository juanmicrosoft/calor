# C1 PR 3: the classifier against the new candidate `04e61f18`

Recorded on 2026-10-09 by the C1 agent: local run on osx-arm64 with SDK 10.0.401 and an isolated
`HOME`. The candidate is `04e61f188e85a710a91242e1ddbf35fcd50e329e`, the merge of #1537. The checks
ran on this PR's branch at `a9844591`.

## 1. Before landing (this PR's branch, `origin/main` = the candidate)

`CandidateManifestTests.RangeFromTheCandidateIsNotInvalidated` with the default target:

```
NOT-INVALIDATED; landing (not landed); 2 commit diffs
```

## 2. Simulated landing

Setup, in a scratch clone:

- C1 PR 3 is merged with `--no-ff` onto the candidate, giving `5518093a`.
- `refs/remotes/origin/main` is set to that merge.
- The checkout of that merge acts as the trust anchor.

Run with `CALOR_C1_CLASSIFY_TARGET` set to the merge. All 21 `CandidateManifestTests` pass:

```
NOT-INVALIDATED; landing 5518093ab0a4fcf612d86089054ba4d6ed1c7cd0; 4 commit diffs
```

The classifier finds the landing at the merge of C1 PR 3, the first first-parent commit after the
candidate. The landings of C1 PRs 1 and 2 (`fd34f41f`, `7eca4868`) come before this candidate and
are not considered. Every path this PR changes is a C1 file, judged `BeforeLanding` and exempt:

- `candidate-manifest.json`, `generate_candidate_manifest.py`, and `inputs.json`;
- this record;
- `docs/plans/v0.24-c1-candidate.md`;
- `CandidateManifestTests.cs`.

This PR adds no test, so `eng/test-manifest.json` does not change.

## 3. Negative check: a later production change

After the simulated landing, one more commit appends a comment to `src/Calor.Compiler/Program.cs`.
Run from the landing checkout with `CALOR_C1_CLASSIFY_TARGET` set to that commit:
`RangeFromTheCandidateIsNotInvalidated` fails, and the other 20 tests pass.

```
INVALID; landing 5518093ab0a4fcf612d86089054ba4d6ed1c7cd0; 5 commit diffs
... [AfterLanding] INVALID src/Calor.Compiler/Program.cs: compiler, runtime, SDK, or tasks source
```

The simulation used scratch refs only. Nothing was pushed, and the shared repository refs were not
changed.
