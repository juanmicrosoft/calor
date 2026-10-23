# C1 PR 2: the classifier against the new candidate `5e52d8ab`

Recorded on 2026-10-08 by the C1 agent: local run on osx-arm64 with SDK 10.0.401 and an
isolated `HOME`. The candidate is `5e52d8abb0881d0337e2ac12c6a031b2c2d2a14d`.

## 1. Before landing (this PR's branch, `origin/main` = the candidate)

`CandidateManifestTests.RangeFromTheCandidateIsNotInvalidated` with the default target, which is
the newest commit that changes a C1 file:

```
NOT-INVALIDATED; landing (not landed); 1 commit diffs
```

Every path that the PR commits change is a C1 file or the test-count bump. The classifier lists
each one as `exempt`, and none as `INVALID`. CI re-runs this check in the required
`tests (compiler)` job.

## 2. Simulated landing

Setup, in a scratch clone:

- C1 PR 2 is merged with `--no-ff` onto the candidate, giving `839fea7b`.
- `refs/remotes/origin/main` is set to that merge.
- The checkout of that merge acts as the trust anchor.

Run with `CALOR_C1_CLASSIFY_TARGET=839fea7b448cdc66d86499e90586ece6970e9080`. All 21
`CandidateManifestTests` pass:

```
NOT-INVALIDATED; landing 839fea7b448cdc66d86499e90586ece6970e9080; 4 commit diffs
```

The classifier finds the landing at the merge of C1 PR 2. That is the first first-parent commit
after the candidate. (The candidate already holds the PR 1 manifest, but the search starts after
the candidate.) So this PR's own commits are judged `BeforeLanding`, and each path they change is
exempt:

- `candidate-manifest.json`, `generate_candidate_manifest.py`, `inputs.json`, this document's
  directory, `docs/plans/v0.24-c1-candidate.md`, and `CandidateManifestTests.cs`;
- `eng/test-manifest.json`, as a test-count bump only (13036 -> 13037).

## 3. Negative check: a later production change

After the simulated landing, one more commit appends a comment to `src/Calor.Compiler/Program.cs`.
Run from the landing checkout with `CALOR_C1_CLASSIFY_TARGET` set to that commit:
`RangeFromTheCandidateIsNotInvalidated` fails, and the other 20 tests pass.

```
INVALID; landing 839fea7b448cdc66d86499e90586ece6970e9080; 5 commit diffs
ecbf0ca3 vs 839fea7b [AfterLanding] INVALID src/Calor.Compiler/Program.cs: compiler, runtime, SDK, or tasks source
```

The simulation used scratch refs only. Nothing was pushed, and the shared repository refs were not
changed.
