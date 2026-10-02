# 0.24 B1 benchmark registration (#1276)

**Registration version:** 1.0.0. **Contract:** v0.24 evidence contract 1.0.1 (FROZEN), section 5.
**Epic:** #1409. **Gate:** B1. **Cutoff:** `72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1`.

This is the registration that `benchmarkEquivalence.registrationRule` requires before #1276
repairs a pair, executes a differential check, or recomputes a metric. It assigns no disposition:
every pair is `UNCLASSIFIED` (awaiting the oracle) or `EXCLUDED-PRE-REGISTERED`, and none is
included. After it merges, the second #1276 PR runs the registered oracle against the merge commit.

The packet in `docs/plans/evidence/b1-1276/registration/` governs; this page summarizes it.
`registration.json` holds the rules, `pairs.json` the 226 pair rows with every contract pair field,
`corpus-inventory.json` every corpus file with its hash and role, `exclusions.json` the
exclusions, `metric-set.json` the metrics, and `sha256.json` the hashes of all of them and this page.
`python3 scripts/b1_1276_registration.py` generates the packet from the cutoff tree (`--check`
detects staleness). `BenchmarkRegistrationTests` (`B001`–`B007`, plus the contract's `E008`–`E010`)
and `PairDifferentialOracleTests` enforce it.

## Denominator

| Location | Pairs | Note |
|---|---|---|
| `tests/TestData/Benchmarks` | 226 | Each `.calr` has exactly one `.cs` with the same directory and stem |
| `benchmarks/` | 0 | Contract scope exclusion; 7 `.calr` files, no C# arm |

217 pairs come from `manifest.json` `benchmarks` (the list the metric loop reads) and go to the
oracle. 9 are scenario variants, excluded below. The other 54 corpus files are not pairs (52
committed `.g.cs` compiler outputs, `manifest.json`, `Comprehension/questions.json`). `pairId` is
the path without extension, because manifest ids `050`–`059` each name two pairs. Hashes are
SHA-256 of the cutoff git blobs; `.gitattributes` marks the corpus and metric sources `-text` so
every checkout has those bytes.

**Task statements** are the manifest entries rendered as `key: value` lines and hashed. They name
a task but specify no inputs or outputs, so equivalence is decided from behavior: an `EQUIVALENT`
pair matches its partner; it is not shown to implement the statement.

## Oracle

`b1-1276-pair-oracle` v1 (`tests/Calor.Evaluation/Equivalence/`, hashes pinned), run twice:
`dotnet run --project tests/Calor.Evaluation -c Release -- pair-oracle --registration
docs/plans/evidence/b1-1276/registration --registration-commit <merge SHA> --output <file>`.
Each pair runs in its own process.

1. Both files must match their registered hashes, or the pair is `UNCLASSIFIED`.
2. Calor goes to C# with the compiler's default options; then both arms use one Roslyn setup
   (latest language, Release, nullable, SDK implicit usings, one reference set). A build error in
   either arm is `NOT-EQUIVALENT`.
3. The public static methods must match (case-insensitive name, exact types; container names do
   not matter; `Main()` and `Main(string[])` are one entry point). A one-sided member is `NOT-EQUIVALENT`; empty surfaces, instance members, and
   unsupported types are `UNCLASSIFIED`.
4. Inputs: per-type boundary pools, a cartesian product (at most 256) or diagonal, and 64 tuples
   from SplitMix64 seeded by SHA-256 of pair id and member key. No input depends on either arm.
5. Each tuple runs Calor, C#, Calor, C# with fresh argument copies, captured stdout, invariant
   culture, and a 2,000 ms limit. A timeout or self-disagreement is `UNCLASSIFIED`.
6. Any difference in return-vs-throw, value, or stdout is `NOT-EQUIVALENT` (up to 5 witnesses);
   otherwise `EQUIVALENT`.

There are no golden outputs: each arm's expected output is the other arm's, and `EQUIVALENT`
means agreement on this finite input set, not a proof. Failure is compared by class (both return
or both throw); exception types are recorded, not compared, because Calor contract failures raise
`Calor.Runtime` types a hand-written C# arm cannot. The two runs must be byte-identical. The
contract's known witness `DomainProblems/CsvParser` must come out `NOT-EQUIVALENT`, or the run is
invalid. Apart from step 2's Calor-to-C# compile, every step treats both arms identically, and the
oracle never reads a metric.

## Exclusions

`X1-scenario-variant` excludes 9 pairs: the 8 `ErrorDetection/*_buggy` (`bugScenarios`) and
`EditPrecision/Calculator_validated` (`editTasks`). Each is a copy of a `benchmarks` pair
(`ErrorDetection/ArraySum_buggy.calr` is byte-identical to `TokenEconomics/ArraySum.calr`), so
counting it would duplicate a program, and the metric loop never reads it. The reason is
structural and independent of any outcome. Excluded pairs stay in the denominator.

## Metrics and statistics

Of BenchmarkRunner's 8 static metrics (10 implementation files pinned by hash), only TokenEconomics
`CompositeTokenEconomics` is comparative: `r = csharpSize / calorSize`, size being the cube root of
tokens × non-whitespace characters × lines, one rule set for both files. The other 7 score each arm
with separate per-language rules (for example `CalculateCalorClarityScore` vs
`CalculateCSharpClarityScore`), so they are descriptive only: no ratio, win count, interval,
ranking, or headline. `r` measures two committed files, boilerplate included. It is a static
feature score, not a coding-agent outcome or a language, productivity, correctness, or safety
advantage.

- **Population:** `benchmarks` pairs the oracle marks `EQUIVALENT`. The corpus is fixed and
  author-constructed, not a sample of programs, tasks, or developers.
- **Sampling unit:** the program pair. Repeated deterministic runs are not samples.
- **Estimate:** geometric mean of `r` over included pairs, each pair weight 1; also per category.
- **Interval:** percentile bootstrap over pairs, 10,000 resamples, SplitMix64 seed 1276, 95%,
  within category for per-category intervals; none below 2 pairs; labeled "corpus-resampling
  interval for this fixed corpus; not a confidence interval for any population".
- **Runs:** 2; per-pair values must be bit-identical, or no result.
- **Removed:** singleton `ci95`, statistics over repeated runs (SD, Cohen's d, t-test), and the
  geometric mean of category means.

The comparability key adds `pairManifestSha256`, `metricSetSha256`, `exclusionsSha256`,
`metricImplementationVersion`, `aggregationMethod` `b1-1276-agg-v1`, and `generatorVersion`. The
dispositioned manifest gets a new hash, so no earlier benchmark result is comparable with it.

## After registration

Changing a pinned field (pair files, task statements, inputs, outputs, failure behavior, oracle,
metrics, aggregation, sampling unit, exclusions) is a #1407 amendment; the tests fail when a pinned
file drifts. A repaired, dropped, or re-dispositioned pair stays reported with its previous
disposition. `website/public/data/benchmark-results.json` and its derivatives stay historical.
#1422 (B2) consumes the dispositioned manifest.

## Decisions for the maintainer

1. **Failure comparison** by class, not exception type. A stricter rule would mark every pair where
   Calor raises a contract exception and C# an `ArgumentException` as `NOT-EQUIVALENT`.
2. **One comparative metric.** Reinstating a language-specific metric needs a symmetric definition
   and an amendment.
3. **Scenario variants** are excluded rather than run.
