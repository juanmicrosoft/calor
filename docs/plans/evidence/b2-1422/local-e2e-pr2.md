# B2 (#1422) PR 2 local end-to-end runs

Machine: macOS (arm64), .NET SDK 10.0.401, Python 3.9.6. No paid compute. Nothing published.

## 1. Registered generator

The workflow's regeneration commands (`pair-metrics` twice, then `pair-results`) were run from this
branch into a scratch directory. All four outputs are byte-identical to
`docs/plans/evidence/b1-1276/results/` (`metrics-run-1.json`, `metrics-run-2.json`,
`pair-manifest.json`, `results.json`). B1's validator and the R2 workflow-gate tests
(`BenchmarkResultsTests|BenchmarkRegistrationTests|ReleaseWorkflowGateTests`) pass: 79 of 79.

## 2. The gate on the real repository, with main simulated

Script: `local-e2e-pr2.py`; full output: `local-e2e-pr2.txt`. The candidate is this branch's code
commit `f2696b94` (the last commit that changes the gate). For each scenario the script builds a
local bare origin whose `main` is the candidate plus one commit, clones it with full history,
checks out the candidate detached (as `benchmark.yml` does), and runs the gate with the regenerated
packet. Only `main` is simulated. The candidate's packet, generator, and validator are real.

| Scenario | main after the candidate | Result | headline sha256 | stamp index sha256 |
|---|---|---|---|---|
| A | equals the candidate | OK | `c81adaed…a607` | `ea03d566…96fb` |
| A2 | equals the candidate (second run) | OK | `c81adaed…a607` | `ea03d566…96fb` |
| B | adds an unrelated test under `EvidenceContract/` (the C2 case) | OK | `c81adaed…a607` | `ea03d566…96fb` |
| B' | as B, run with the pre-PR-2 gate (`fd34f41f`) | **B2-08** on `tests/Calor.Compiler.Tests/EvidenceContract` (the C2 refusal) | - | - |
| C | changes `tests/Calor.Evaluation/Equivalence/PairResultsCommand.cs` | **B2-08** `tests/Calor.Evaluation` | - | - |
| D | changes a registered pair file | **B2-08** on the corpus directory and the pair file | - | - |
| E | contains A's headline and stamp index (the publication PR merged) | OK | `c81adaed…a607` | `ea03d566…96fb` |
| E' | as E, run with the pre-PR-2 gate | OK, but headline `6e79d9fb…bcfc` | differs | `ea03d566…96fb` |

**Determinism proof.** A, A2, B, and E write the same two files byte for byte, while main differs
in each case. The bytes are a function of the candidate. E' shows the defect PR 2 removes: the
pre-PR-2 gate writes a different headline for the same candidate once main has changed, because its
`comparableWithPublished`/`comparison` fields came from main's headline. An A1 hash taken at the
candidate would then fail R2's `--benchmark-worktree` comparison.

These hashes belong to the code commit `f2696b94`. Every candidate has its own hashes, because the
headline records the candidate commit and its tree hashes. The 0.24 re-frozen candidate gets its
own, which A1 adjudicates.
