# CI quality gates

The machine-owned test inventory is `eng/test-manifest.json`. Every maintained test
project must name its owning workflow, and every release-critical project is run again
by `.github/workflows/publish-nuget.yml` before packages can be published.

## Ratchets

- `eng/coverage-baselines.json` records line and branch floors for verifier, emitter,
  binder, migration, dataflow, taint, effects, parsing, and type checking. `scripts/check_coverage.py` merges
  Cobertura reports by source line so duplicate coverage from multiple suites is not
  double-counted.
- `eng/mutation-baselines.json` defines a small set of compiling, deterministic mutants.
  Migration-emitter brace escaping is labeled separately from native C# generation.
  Native mutants exercise distinct arithmetic operands, side-effect evaluation order,
  and parser conditional-arm selection through executable production-path regressions.
  These hand-selected probes are not an estimate of general mutation coverage.
  `scripts/run_mutation_gate.py` only credits an assertion
  failure as a kill; build and infrastructure failures fail the gate without improving
  the score.
- `eng/performance-baselines.json` records the performance ceiling and noise policy.
  One warmup is discarded, three isolated runs are measured, and the median is compared
  with the ratcheted ceiling. The raw run logs and JSON summary are retained as artifacts.

Each ratchet has a negative self-test that proves a regression is rejected. Raise a
baseline only after a verified improvement; lowering one requires an explicit review of
the corresponding report and rationale in the pull request.

### Frontend floor provenance (#1197)

The new floors were selected after a corpus-enabled Release measurement of compiler
baseline `2c316156` using .NET SDK 10.0.400 on macOS. The collection used the same five
projects as `quality-ratchets` (Compiler, Conversion, Enforcement, Semantics, Verification),
the shared two-part compiler filter, and all three pinned corpus submodules. All 9,635
cases completed: 9,631 passed and four compiler cases skipped. No existing floor failed.
This is CI-comparable collection, not a claim that macOS and Linux coverage are identical.

| Component | Covered/total lines | Measured line | Covered/total branches | Measured branch | Line/branch floors |
|---|---|---|---|---|---|
| Parsing | 8,326/9,966 | 83.54% | 6,117/8,254 | 74.11% | 82% / 73% |
| TypeChecking | 665/915 | 72.68% | 701/956 | 73.33% | 71% / 72% |

Each new floor is the measured percentage rounded down to an integer, minus one
percentage point for modest variation in collection and compiler changes. Existing
binder, migration, and other floors are unchanged. The coverage self-test independently
drops line and branch coverage in each frontend component while all other components
stay green, and requires the corresponding gate to fail.

Pull requests targeting `release/**` run the same test workflow as those targeting
`main`; release integration is not exempt from the coverage or mutation ratchets.

## Published reports

CI retains test TRX, coverage, mutation, performance, migration/round-trip, and live-LSP
core-capability stress reports. Regular PR CI explicitly runs 10 repetitions; the NuGet
release workflow explicitly runs 100. The process-level E2E test owns the exact `calor-lsp`
process and asserts that exact PID exits after shutdown/disposal. The outer runner accepts
only one Passed TRX result mapped by `testId` to that exact fully-qualified test method; substring
matches, noncanonical/misnested TRX structures, duplicate containers or counters, inconsistent
or missing standard counter fields, nonzero failure/nonterminal counters, skipped results, and
missing definitions fail closed. The runner
provides bounded root-process timeout handling and best-effort cleanup of observed children;
it is not a kernel containment boundary and never certifies a timeout or supervision failure.
The NuGet release job depends on the manifest-declared test,
packaged-SDK consumer, and release-quality jobs.
