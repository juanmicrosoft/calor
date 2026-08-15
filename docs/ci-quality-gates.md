# CI quality gates

The machine-owned test inventory is `eng/test-manifest.json`. Every maintained test
project must name its owning workflow, and every release-critical project is run again
by `.github/workflows/publish-nuget.yml` before packages can be published.

## Ratchets

- `eng/coverage-baselines.json` records line and branch floors for verifier, emitter,
  binder, migration, dataflow, taint, and effects. `scripts/check_coverage.py` merges
  Cobertura reports by source line so duplicate coverage from multiple suites is not
  double-counted.
- `eng/mutation-baselines.json` defines one compiling, deterministic mutant for each
  safety-critical component. `scripts/run_mutation_gate.py` only credits an assertion
  failure as a kill; build and infrastructure failures fail the gate without improving
  the score.
- `eng/performance-baselines.json` records the performance ceiling and noise policy.
  One warmup is discarded, three isolated runs are measured, and the median is compared
  with the ratcheted ceiling. The raw run logs and JSON summary are retained as artifacts.

Each ratchet has a negative self-test that proves a regression is rejected. Raise a
baseline only after a verified improvement; lowering one requires an explicit review of
the corresponding report and rationale in the pull request.

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
