# B2 (#1422) local end-to-end runs

Machine: macOS (arm64), .NET SDK 10.0.401, Python 3.9.6. No paid compute. Nothing published.

## 1. Registered generator, re-run on the committed oracle outputs

The workflow's regeneration commands (`pair-metrics` twice on `oracle-run-2.json`, then
`pair-results` with both oracle runs and both metric runs) were run from the branch into a scratch
directory. All four outputs are byte-identical to the committed packet:

```
metrics-run-1.json identical
metrics-run-2.json identical
pair-manifest.json identical
results.json identical
```

## 2. B1's validator (the step the workflow runs before regeneration)

`dotnet test tests/Calor.Compiler.Tests -c Release --filter "FullyQualifiedName~BenchmarkResultsTests|FullyQualifiedName~BenchmarkRegistrationTests"`:
`Passed! - Failed: 0, Passed: 58`.

## 3. The gate on the real repository

A `git clone --no-local` of the branch, with `refs/remotes/origin/main` set to the branch head to
stand in for the merged commit (the only simulated part), full history, then
`python3 scripts/benchmark_publication_gate.py check --commit <HEAD> --regenerated <scratch>`:

```
0.24 B2 (#1422) benchmark publication gate
OK: comparabilityKeySha256 35ee7f8e33a8ef176728ead7b56dc0c64153b59a5db9f06fa87b7acda052104e
denominator: 226 registered, 9 excluded before registration, 217 sent to the oracle, 17 included (EQUIVALENT); by disposition {'EQUIVALENT': 17, 'EXCLUDED-PRE-REGISTERED': 9, 'NOT-EQUIVALENT': 192, 'UNCLASSIFIED': 8}
TokenEconomics/CompositeTokenEconomics: geometric mean r 1.2349617720447104 over 17 pairs, corpus-resampling interval ['1.114699569538594', '1.3565810319607556']; r = csharpSize / calorSize (r > 1: the C# file is larger; r < 1: the Calor file is larger)
no headline is published yet; nothing is compared
```

followed by the five limitations. Exit 0. The only changed files were
`website/public/data/benchmark-headline.json` (new) and
`bench/phase0-agent-native/commit-stamp-index.json` (one appended `publicationStamps` entry).
These numbers equal `docs/plans/evidence/b1-1276/results/results.json`; the gate computes nothing.

## 4. P1's provenance verifier on the gate's index entry

In the same clone: `dotnet test tests/Calor.Compiler.Tests --filter "FullyQualifiedName~LedgerCommitStampTests|FullyQualifiedName~DurableProvenance"`:
`Passed! - Failed: 0, Passed: 23`. This includes `EveryPublishedBenchmarkStampIsIndexed`,
`EveryIndexEntryStillMatchesItsArtifactsOwnStamp`, and
`EveryAuthoritativeIdentityResolvesFromProtectedMain` against the new entry
(`identical-src-tree`, complete identity at the measured commit).

## 5. Python suites

`scripts/test_benchmark_publication_gate.py`: 50 tests OK. `scripts/test_supply_chain.py`,
`scripts/check_test_quality.py`, `scripts/test_determinism_protocol.py`, and
`scripts/determinism_protocol.py validate`: pass after the rebase onto `7d37bf8d`.
