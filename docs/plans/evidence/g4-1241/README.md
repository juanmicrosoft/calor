# 0.24 G4 (#1241): truthful Tier 1 and Tier 2 verification — closure evidence

Gate G4 of the frozen 0.24 contract (`docs/plans/v0.24-evidence-contract.md`, contract
1.1.0). This packet maps the G4 closure evidence (§10; `contract.json` `children`, issue
1241) to the change that provides each item and the check that keeps it closed. It does not
amend the contract and changes no inventory record.

Sections consumed: §3 (inventory: `tier1-verification`, `tier2-corpus-verification`), §4
(outcome vocabulary and non-outcome row statuses), §8 (missingness and history rules), §9
(capacity), §10 (G4 row).

## Decision: the AST round-trip claim is removed, not implemented

`scripts/ast_roundtrip_check.py` described itself as "parse → emit → parse" but compiled each
fixture once and compared nothing. G4 removes the claim instead of building the round trip:

- The script is replaced by `scripts/fixture_compile_check.py`, which says what it does: one
  compile per fixture (or per registered multi-file workspace), compared with an explicit
  expectation. Its report carries `"astRoundTrip": false`.
- No Tier 1 or Tier 2 step, workflow, or script claims an AST round trip.
  `RepositoryGuards.test_no_live_claims_or_empty_selections` fails on any line in `scripts/`
  or `.github/workflows/` that mentions an AST round trip without a negation.
- The migrator check that does round-trip (`migrate → revert → byte-equal`) is labelled a
  source byte round trip, not an AST round trip.

A real AST round trip would need a Calor → AST → Calor emitter path with a defined
equivalence. `CalorEmitter` is lossy on source text by design (the formatter explicitly does
not use it for existing source), so the round trip would need new compiler surface and would
surface emitter findings outside G4. No equivalence is claimed.

## Closure evidence

| Contract clause | Change | Kept closed by |
|---|---|---|
| Checkout-pinned Tier 2 run | `scripts/checkout_compiler.py`: every tier script runs `dotnet src/Calor.Compiler/bin/Release/<tfm>/calor.dll` built from the checkout. The build is refused when it is missing, or older than any build input: every non-ignored file (any type, tracked or untracked, so embedded resources and new sources count) under `src/Calor.Compiler` or `src/Calor.Runtime`; every file those projects link with `Include="..\.."` (for example the embedded metadata-references manifest under `bench/`); the root build files (`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, `NuGet.config`); and every directory under the two projects that holds an input, because adding or deleting a file, even in a commit, updates the directory. An installed `calor` on `PATH` is never used. Reports record the DLL path, its SHA-256, `HEAD`, and whether the worktree is dirty. A declared linked input that no longer exists also refuses the build. Limitations: freshness is judged by modification time, not by a content fingerprint of the build inputs; and a deleted root build file (for example `Directory.Packages.props`) is not detected, because the repository root's own mtime changes for unrelated reasons such as CI log files. | `PinnedCompilerControls` (installed tool on `PATH` + no build → error; an edited source, an edited untracked resource, an edited linked resource, a committed deletion of a linked resource, an unstaged deletion, and a committed deletion each → error; fresh build → pinned command); `RepositoryGuards.test_tier_scripts_never_resolve_an_installed_tool` |
| Explicit positive / negative / multifile expectations | `eng/tier2-fixture-expectations.json` (G4's machine-readable closure inventory) lists all 509 tracked `.calr` files under `samples/` and `tests/` by path, each exactly once, with `compile`, `reject` (`negativeKind` `designed` or `incidental`, exact error signature), or `known-failure` (real compiler failure, exact signature, defect text). A signature is one `file|code|declaration|message` entry per error (base file name; `Calor1002` carries its Roslyn code; whitespace collapsed), sorted, duplicates kept. An error in another module, on another declaration, with another cause, or one extra error fails the row. For example, E3 expects `E3_call_chain.calr|Calor0410|f001|Function 'Caller' uses effect 'cw' but does not declare it`. Non-success rows keep each error's file, line, code, and message in the report. Options mirror the consumer and come from a 4-flag allowlist. 36 multi-file workspaces compile `together`. | `RepositoryGuards.test_every_tracked_fixture_is_registered_exactly_once`; `test_benchmark_known_failures_match_the_bulk_test`; `FixtureCheckControls.test_single_fixture_outcomes` (including an extra same-code error and a same-file, same-code error with another cause), `test_negative_error_must_come_from_the_registered_file`, `test_malformed_entries_are_invalid`, `test_multifile_group_is_compiled_together_outside_the_checkout` |
| No build-output duplicates | Discovery is `git ls-files`, so untracked `bin/` and `obj/` copies are never selected; a tracked `.calr` under `bin/` or `obj/` is an error; the report counts the ignored copies (`ignoredUntrackedCalr`). | `FixtureCheckControls.test_build_output_is_never_discovered`; `test_single_fixture_outcomes` (a registered `obj/` copy is `invalid`) |
| No empty advertised selections | Removed `dotnet test --filter` on the `Unit` (Tier 1) and `DiagnosticSnapshot` (Tier 2) category traits: no test carries either trait, so both selected zero tests. An empty fixture selection fails the fixture check and both migrator checks. | `RepositoryGuards.test_no_live_claims_or_empty_selections`; `test_empty_selection_fails`; `MigratorAndDriverControls.test_migrators_fail_closed` |
| AST-roundtrip claims match the work performed | See the decision above. | `test_no_live_claims_or_empty_selections` (also asserts `ast_roundtrip_check.py` is gone) |
| Negative self-tests | `fixture_compile_check.py --self-test` (real compiler, synthetic fixtures; run by `verify_phase1.py --self-test` in the `verify-phase1` job): a broken positive is `failed`, a compiling negative is `unexpected-pass`, a negative with another signature is `failed`, a known failure is never a pass, and the empty selection fails. `scripts/test_tier2_verification.py` (16 tests with subtests, guard job, no build) adds fake-compiler controls for crashes, missing output, timeouts, unclassified and stale entries, double registration, malformed entries, wrong-file and extra errors, multi-file grouping, the migrator checks (unavailable, empty, vacuous, a dry run that reports changes but not to the control, passing, a dry run that writes, a revert that corrupts; the checkout is never modified), and both drivers. The test lists, independently of the drivers, the commands each mode must run: Tier 1 runs the fixture check on `samples`; the Tier 1 self-test runs both checkers' self-tests; Tier 2 runs Tier 1 with `--corpus all` and both migrator checks. It asserts each one ran. Then it fails each step alone with exits 1, 2, and 3, and the driver must fail every time; informational steps never decide the verdict. | Mutation check below |

### Outcome vocabulary (§4)

Only `passed` and `expected-negative` are successes. `known-failure`, `failed`,
`unexpected-pass`, `unclassified`, `invalid`, `crashed`, and `TimeoutOrUnavailable` all make
the verdict `FAIL`. There is no skip path. `unclassified`, `invalid`, and `crashed` are the
frozen non-outcome row statuses; a compile that hits the time limit and a migrator that is
missing (exit 3) are reported as `TimeoutOrUnavailable`, never as a pass. The token-delta
steps are labelled informational and never count as checks.

The migrator dry run and revert round trip previously exited 0 ("Phase 0 stub") when the
migrator was missing. They now exit 3. Both run on a scratch copy holding exactly the tracked
selection, and both add a synthetic control file that the rewrite must change (the dry run must
list the control in the `fixes` of `calor fix --format json`), because the tracked corpus has no structural IDs left to drop: the
forward rewrite changes 0 of 498 tracked `tests/` files, so without the control both checks
would pass while exercising nothing.

## Fixture classification

509 tracked fixtures (11 under `samples/`, 498 under `tests/`), 103 entries.

| Expectation | Files | Entries |
|---|---|---|
| `compile` | 428 | 43 |
| `reject`, `designed` (the consumer says the fixture must fail) | 38 | 19 |
| `reject`, `incidental` (nothing compiles it; invalid Calor rejected correctly) | 13 | 11 |
| `known-failure` (real compiler failure; keeps Tier 2 red) | 30 | 30 |

**Designed negatives** cite their consumer: `Calor.Enforcement.Tests` `*.expected.json` (E1,
E3–E6, N1), `EditScripts/README.md` (violating steps of ES-01–ES-05 and ES-08),
`QueryCorpus/expected.json` (`Leaky`, `AsksAmbiguous`, `AsksMissing`), `RN-06/script.json`
(module split, #922), and `LintScenarios/10_error_cases`.

**Incidental negatives** (13 files): 7 lint-only inputs that print or throw without declaring
`cw`/`throw`; the path-2-gate task-06 `setup/` and `expected/` workspaces (undeclared `net`/`db`
effects; `expected/` also catches undefined `NetworkError`/`IoError`, which Roslyn rejects);
`tests/E2E/scenarios/07_collections/output.g.calr`, which nothing reads; and
`Issue1104_BatchingSink_LoopAsync.calr`, which its test only parses. Each is a defect in the
fixture, not in the compiler. A codegen-only signature (`Calor1002`/`Calor1006` alone) can never
be an incidental negative (`test_codegen_only_failures_are_not_incidental_negatives`).

**Known failures** (30 files). The front end accepts each source; the generated C# fails Roslyn
or the emitter refuses it:

| Root cause (first error) | Files |
|---|---|
| `CS0266` double → float in generated C# | 15 benchmarks (HashMap, Adapter, Factory, CurrencyConverter, ScoreBoard, UnitConverter, VotingSystem, Composition, Polymorphism, AreaOfCircle, Average, CelsiusToKelvin, CompoundInterest, TemperatureConverter, TemperatureRange) |
| `CS1001`/`CS1003`/`CS1511` `base` in a static method | NetworkEffect, PureFunctions, Power; InterfaceImpl (also `CS0266`) |
| `CS0664` double literal to float | TaxCalculator |
| `CS0060`/`CS0115`/`CS0122`/… inconsistent accessibility | TemplateMethod, Visitor |
| `CS1061` `Option<int>.unwrap` does not exist | ErrorDetection/NullDeref_buggy (not in the benchmark manifest; nothing compiled it) |
| Approved converter output that does not compile | Conversion snapshots 08-03 (`CS0029`), 09-03 and 13-05 (`CS8510`), 11-03 (`CS0050`), 11-05 (`CS0246`: Calor type names `str`/`i32` reach C#), 13-02 (syntax errors), 13-10 (`Calor1006`) |

The 22 registered benchmarks among these are exactly `BulkBenchmarkCompilationTests`'
`KnownGeneratedCSharpFailures` (#939), checked by a guard test. Whether the converter or the
emitter causes each snapshot failure was not investigated in G4.

**Options.** `compile` entries use their consumer's options: benchmarks `--no-enforce-effects`
(`BulkBenchmarkCompilationTests`), enforcement contract scenarios `--no-enforce-effects`,
enforcement effect scenarios `--transpile-only` (`TestHarness.Compile`), EditScripts the
step's profile. Conversion snapshots have no compiling consumer; they use
`--permissive-effects`, which the CLI documents as the mode for converted code. Two snapshots
(07-04, 13-03) compile only under it; under default options both fail with `Calor0410`.

### Before and after

The inherited run 34291280793 reported 169 failures across 1,092 discovered files, counting
build-output copies and expected negatives. The old single-compile logic, restricted to the
509 tracked files with default options and one file at a time, fails 84 of them. Under the
registered expectations those 84 are: 30 known failures, 20 designed and 11 incidental expected
negatives, and 23 that compile with their consumer's options or inside their multi-file
workspace. Twenty further files are expected negatives only as members of a failing workspace.

## Runs

- **Local** (macOS arm64): `tier2-fixtures-local.json` is the fixture-check report from
  `python3 scripts/verify_corpus.py --report …`; its `provenance` records the commit and
  whether tracked files were modified. Verdict `FAIL`:
  `passed` 428, `expected-negative` 51, `known-failure` 30, every other status 0.
  Migrator dry run and revert round trip `OK` (forward rewrite changed 0 corpus files plus the
  control). Tier 2 exits 1 because of the 30 known failures.
- **CI** (ubuntu-latest): `tier2.yml` `workflow_dispatch` run
  [37142068674](https://github.com/juanmicrosoft/calor/actions/runs/37142068674) on
  `5fa2adc2`, before review round 1: same counts as local (428 / 51 / 30, everything else 0),
  migrator checks `OK`, job `failure` because of the 30 known failures. The CI build also
  builds every test project, and the report's `ignoredUntrackedCalr.buildOutput` is **594**:
  the `bin/` copies the old discovery counted (509 + 594 = 1,103 files, close to the inherited
  1,092). Later runs are listed in the PR description.

Tier 2 stays red until the 30 known failures are fixed. That is the intended result: the gate
reports real compiler failures instead of hiding them, and G4 does not fix emitter or
converter defects.

## Mutation check

Thirty-two single-line mutations of the checkers each make
`scripts/test_tier2_verification.py` fail (32 of 32 killed). Fixture check: accept a known
failure as a pass; drop the unexpected-pass branch; ignore the signature; drop the file from the
signature; drop the declaration and message; collapse duplicate errors; drop the failure
diagnostics; drop the unclassified rows; accept an empty selection; treat a timeout or crash as
a pass; skip the missing-output check; compile a together group file by file; accept errors with
a nonzero exit other than 1. Pinning: skip the stale check; ignore untracked inputs; ignore
input directories (committed deletions); ignore linked inputs; ignore missing linked inputs;
skip the tracked build-output check. Migrators: make a missing migrator a pass (both scripts);
accept any proposed change instead of the control's; skip the empty-selection check, the
dry-run write check, the revert control check, and the post-revert byte comparison. Drivers:
label exit 3 as OK; drop either Tier 2 migrator step; drop the Tier 1 fixture-check self-test.

## Verifier findings

None. Every known failure is in code generation or C# → Calor conversion (`Calor1002`,
`Calor1006`). No contract-verification (Z3) result is involved, so nothing is routed to S2
(#1413).

## Not changed, and why

- **Inventory records.** `tier1-verification` and `tier2-corpus-verification` stay `stale` with
  open defect `#1241`. Under `reclassificationRule` only a versioned amendment that names the
  merged repairing PR can change them; G4 holds no amendment authority.
- **History.** Historical plans (`docs/plans/path-2-drop-ids-v5-implementation.md`,
  `…-v6-implementation.md`, `phase-2-validation-criteria.md`, and others) still describe the old
  `ast_roundtrip` step. They record what was planned and claimed then and are not rewritten
  (§8). `CHANGELOG.md` and `website/content/changelog.mdx` mention #1241 only as an open
  follow-up; no website page claims an AST round trip, so no public text changes.
- **New fixtures.** A new `.calr` under `samples/` or `tests/` now needs an expectation entry,
  or the guard job fails. That is the intended "no catch-all" rule.

## Capacity (§9)

One PR. $0. CI: ordinary PR runs plus `tier2.yml` dispatches. Changed lines are counted
excluding `docs/plans/evidence/` and `eng/tier2-fixture-expectations.json`; the second
exclusion reads amendment 1.1.0's `evidenceDataRule` as covering G4's closure inventory,
because G4's closure evidence names the explicit fixture expectations. The PR description
gives both counts.

## Reviews

`reviews/` holds the Codex adversarial review rounds and the verification-only pass. Rounds 1, 2,
and 3 requested changes (5, 5, and 3 findings); each was fixed or, for two pinning residuals, recorded
as a limitation above. The verification-only pass on the round-3 fixes is CLEAN.
