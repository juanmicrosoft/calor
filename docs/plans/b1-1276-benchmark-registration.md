# 0.24 B1 benchmark registration (#1276)

**Registration version:** 1.0.0. **Contract:** v0.24 evidence contract 1.0.1 (FROZEN), section 5.
**Epic:** #1409. **Gate:** B1. **Cutoff:** `72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1`.

This is the registration that `benchmarkEquivalence.registrationRule` requires before #1276
repairs a pair, executes a differential check, or recomputes a metric. It assigns no disposition:
every pair is `UNCLASSIFIED` (awaiting the oracle) or `EXCLUDED-PRE-REGISTERED`, and none is
included. After it merges, the second #1276 PR runs the registered oracle at the merge commit.

The packet in `docs/plans/evidence/b1-1276/registration/` governs; this page summarizes it:
`registration.json` (rules, oracle, statistics), `pairs.json` (226 rows with every contract pair
field), `corpus-inventory.json`, `exclusions.json`, `metric-set.json`, and `sha256.json` (seals of
all of them and this page). `python3 scripts/b1_1276_registration.py` builds it from the cutoff tree.
`BenchmarkRegistrationTests` (`B001`–`B008` plus the contract's `E008`–`E010`, with a pinned seal)
and `PairDifferentialOracleTests` enforce it.

## Denominator

`tests/TestData/Benchmarks` holds 226 pairs: every `.calr` has exactly one same-stem `.cs`. 217
come from `manifest.json` `benchmarks` and go to the oracle; 9 are excluded scenario variants. The
other 54 files (52 committed `.g.cs` compiler outputs, `manifest.json`, `questions.json`) are not
pairs. `benchmarks/` (a contract scope exclusion) has 7 `.calr` files and no C# arm: 0 pairs.
`pairId` is the path without extension (manifest ids `050`–`059` each name two pairs). Task
statements are the manifest entries, hashed; both arms share one, and it specifies no inputs or
outputs, so the oracle checks the arms against each other, not against the statement.

## Oracle

`b1-1276-pair-oracle` v1 (`tests/Calor.Evaluation/Equivalence/`, hashes pinned), run twice with
`dotnet run --project tests/Calor.Evaluation -c Release -- pair-oracle` (the second run with
`--compare-with` the first). It refuses to run unless the checkout is the clean registration
commit and every seal and pin matches. Per pair, in its own process: identity check; Calor to C# with default
options, then one Roslyn setup for both arms (build error: `NOT-EQUIVALENT`); public static methods of
static classes must match (one-sided: `NOT-EQUIVALENT`; any other public member or unsupported type:
`UNCLASSIFIED`); inputs from boundary pools and SplitMix64 seeded by pair id and member key, never
by either arm; each tuple runs Calor, C#, Calor, C# with fresh copies, a fresh working directory,
and a 2,000 ms limit. A difference in return vs throw, value, exception type, stdout, argument state
after the call, or created files is `NOT-EQUIVALENT`; agreement is `EQUIVALENT` on a finite input
set, not a proof. A pair that changes between runs is `UNCLASSIFIED`, and the result is invalid
unless the known witness `DomainProblems/CsvParser` is `NOT-EQUIVALENT`. Changing any pinned field
is a #1407 amendment; the tests fail on any drift until the pinned seal changes in review.

## Exclusions

`X1-scenario-variant` excludes 9 pairs: the 8 `ErrorDetection/*_buggy` and
`EditPrecision/Calculator_validated`. Each copies a `benchmarks` pair (`ArraySum_buggy.calr` is
byte-identical to `TokenEconomics/ArraySum.calr`), and the metric loop never reads it. The reason
is structural, not outcome-based. They stay in the denominator.

## Metrics and statistics

Only TokenEconomics `CompositeTokenEconomics` is comparative: `r = csharpSize / calorSize`, one rule
set for both files. The other 7 static metrics score each arm with separate per-language rules and
are descriptive only. `r` is a static feature score of two files, not a coding-agent outcome or a
language advantage. The population is the `benchmarks` pairs the oracle marks `EQUIVALENT`, a fixed
author-built corpus. The sampling unit is the program pair; 2 deterministic runs must be
bit-identical and are not samples. The estimate is the geometric mean of `r`; the interval is a
percentile bootstrap over pairs with a pinned algorithm, none below 2 pairs, labeled as
corpus-resampling only. Singleton `ci95`, statistics over repeated runs, and the mean of category
means are removed.

## Decisions for the maintainer

1. **Exact exception types:** a Calor contract exception and a C# `ArgumentException` on the same
   input make a pair `NOT-EQUIVALENT`.
2. **One comparative metric:** reinstating a language-specific metric needs a symmetric definition
   and an amendment.
3. **Scenario variants** are excluded rather than run.
4. **"Implement the same task statement"** is read relationally: the arms share one statement and
   compute the same observable function. Requiring conformance to the statement needs per-pair task
   assertions registered by amendment before the oracle runs (`registration.json`
   `taskStatement.interpretation`).
