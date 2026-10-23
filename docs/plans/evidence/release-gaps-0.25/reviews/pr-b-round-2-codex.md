# PR B (#1546, #1547), Codex round 2

Reviewing the round 1 head. Raw final message: `pr-b-round-2-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (1 MAJOR). The round 1 fixes were not disputed.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | `benchmark.yml` checks out the candidate before its later gate runs, so those runs use the candidate's copy of the gate. A candidate older than this PR has a gate without `G015`, which would publish without the record's approval. | Fixed. The first gate step, still on the `main` checkout, copies `main`'s `verify_release_adjudication.py` to `$RUNNER_TEMP/main-gate/`. Every gate run after the candidate checkout uses that copy with `--repo .`. `BenchmarkPublicationIsASeparateExplicitDispatch` now asserts four things: the copy happens before the checkout, the publishing step uses it, and no later step runs the candidate's gate. `G014` needs no such change in `publish-nuget.yml`: its `adjudication-gate` job runs `main`'s gate on the `main` checkout, and `G014` runs on every invocation. |
