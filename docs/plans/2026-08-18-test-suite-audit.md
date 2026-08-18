# Calor Test Suite Audit

**Date:** 2026-08-18
**Version audited:** 0.13.2 (`Directory.Build.props`)
**Scope:** All test projects under `tests/`, the round-trip harness under `tools/`, CI workflows under `.github/workflows/`, and the benchmark corpus under `bench/`.
**Frame:** How well does today's suite (a) exercise what real users actually do, and (b) catch regressions when they slip in?

---

## TL;DR

The suite is **large, well-instrumented, and honest about its own coverage
ratchets**. It is also **structurally lopsided**: about 70% of the test mass
sits on compiler internals (lexer, parser, type system, Z3 lowering) that no
user calls directly, while less than a fifth of the 28 CLI subcommands have
end-to-end tests, and only one LSP integration test exists. Regression pinning
is inconsistent — 2 of 10 notable 0.13.x fixes have dedicated regression tests;
4 rely on incidental coverage; 4 have no test at all. The round-trip corpus
gate is deterministic and fail-closed on threshold breaches but does not alarm
on *incremental* drift — a project going 155/157 → 154/157 passing still
crosses the gate.

Three fixes would move the needle furthest:

1. **Add end-to-end tests for the user-facing verbs that carry adoption:**
   `calor migrate`, `calor watch`, `calor init --ai <agent>` (with actual hook
   invocation), `calor convert`, and multi-file LSP flows.
2. **Add regression pins for the specific known-open blind spots** —
   `#883` (IL-analysis inputs missing from MSBuild cache key), `#925` (cross-module
   effect resolution), `#774` (for-loop non-additive incrementor), and the
   Z3 semantics-version bump machinery.
3. **Tighten the round-trip corpus gate** so a one-test loss triggers a PR
   warning even when absolute thresholds stay green, and make the corpus
   discoverable locally so contributors can reproduce CI failures.

The rest of this document is the evidence.

---

## Section 1 — Scale and shape

| Metric | Value |
|---|---|
| Test projects | 13 (`Calor.Compiler.Tests`, `Calor.Conversion.Tests`, `Calor.Evaluation`, `Calor.Semantics.Tests`, `Calor.Verification.Tests`, `Calor.Enforcement.Tests`, `Calor.LanguageServer.Tests`, `Calor.RoundTrip.Harness.Tests`, `Calor.Ids.Tests`, `Calor.Tasks.Tests`, `Calor.ILAnalysis.Tests`, `Calor.Performance.Tests`, `Calor.Experimental.Tests`) |
| Test files (`.cs`) | ~881 |
| Test methods (`[Fact]` / `[Theory]`) | ~8,500 |
| Test LOC | ~223,000 |
| Test data files under `tests/TestData/` | 768 (~3.2 MB) |
| OSS corpus submodules | 3 (MediatR, Serilog, FluentValidation) — configured in `.gitmodules` |
| CI jobs on PR (`.github/workflows/test.yml`) | 11+ (`test`, `quality-ratchets`, `remaining-tests` matrix ×13, `ds15-fixtures`, `conversion-scorecard`, `roundtrip-verification`, `d-s1.5-fixtures`, `phase1-corpus-clean`, `phase2-gate-dryrun`, `id-validation`, `verify-phase1`, `sdk-package-consumer` ×5 rids) |
| Coverage ratchets | 7 modules pinned in `eng/coverage-baselines.json` (Binder 88%, CodeGen 82%, Migration 78%, Taint 75%, Verifier 74%, Dataflow 66%, Effects 51%) |
| Coverage enforcement | `scripts/check_coverage.py` fails CI if any component drops below baseline |
| Mutation gate | `scripts/run_mutation_gate.py` — deterministic, schema-pinned |
| LSP flake gate | `scripts/run_flake_gate.py` — 10× LSP re-run |

**Bottom line for scale:** this is a serious test suite by the standard of a
single-maintainer pre-1.0 project. It has coverage ratchets, mutation testing,
flake gates, TRX inventory enforcement, and per-module coverage baselines. The
issue is not *size*; it is *shape*.

---

## Section 2 — What is working well

Worth naming these so we do not accidentally regress them while fixing the
gaps.

- **Coverage baselines are ratcheted per-module, not aggregate.** A drop in
  `Effects/` cannot be masked by a rise in `Binding/`. `eng/coverage-baselines.json`
  pins seven distinct components with both line and branch thresholds.
- **The formatter has a dedicated losslessness gate.** `LosslessFormattingTests`
  plus the shared write-path validation used by `format --write`, `lint --fix`,
  MCP, and LSP catches "the formatter dropped a comment" without needing
  someone to notice.
- **The 217-program deterministic benchmark suite doubles as a regression
  detector.** Programs live under `bench/`; the 30-run statistical runner in
  `Calor.Evaluation` reports both metric ratios and per-program compilation
  status. Any program that stops compiling shows up as a scorecard delta on
  the PR.
- **Snapshot mechanism cannot be silently updated in CI.** `CALOR_UPDATE_SNAPSHOTS=1`
  is required to accept a diff; CI does not set it, so drift is always
  caller-explicit.
- **The verifier differential harness is honest about coverage.** F-4 in the
  0.13.0 release notes pins "65 modeled forms × 3 depths × 2 polarities =
  1,170 deterministic test cases" with zero mismatches. This is a real gate,
  not a benchmark.
- **The `sdk-package-consumer` job builds a real consumer project against a
  freshly packed SDK on five RIDs.** This catches "the package works on my
  machine but breaks when restored fresh" before publish.
- **The MCP memory-admission scope fix (`#897`) shipped with a dedicated
  regression test.** `tests/Calor.Compiler.Tests/Mcp/McpMemoryAdmissionTests.cs`
  pins three cases (embedded, server, threshold). This is the model for what
  bug-fix pins should look like.

---

## Section 3 — Findings

### F1. The user surface is under-tested relative to compiler internals

Users interact with Calor through **28 CLI subcommands**, **~17 MCP tools**,
the **LSP**, the **MSBuild SDK**, and **AI-agent hooks** installed by
`calor init`. The tests are not proportional to this surface.

| Surface | Items | End-to-end tests | Notes |
|---|---|---|---|
| CLI subcommands | 28 | ~5 with a dedicated E2E file; 9 have any command-level test | No E2E for `convert`, `migrate`, `review-packet`, `assess`, `analyze-convertibility`, `import`, `verify`, `run`, `test`, `effects`, and others |
| MCP tools | ~17 | 20+ tool-unit tests exist, but tests exercise tool-internal logic, not agent-driven scenarios | No test simulates a real agent (Claude Code / Codex) driving a tool |
| LSP capabilities | 6 advertised (hover, completion, definition, references, rename, diagnostics) | **1** integration test (`LspE2ETests.cs`, ~100 lines, single-document) | No multi-document flow; no rename-across-files test; no diagnostic-under-incremental-edit test |
| MSBuild task | 1 (`Calor.Tasks.CompileCalor`) | 2 integration tests (`CompileCalorIntegrationTests.cs`) | No test for solution-wide build or design-time build |
| AI-agent hooks | 6 hook subcommands, 4 agents | `InitCommandE2ETests.cs` verifies file scaffolding; `HookCommandTests.cs` exercises CLI parsing | No test that a hook installed by `init` actually blocks a `.cs` write when invoked in an agent-like scenario |

Meanwhile the compiler-internals side is thickly tested — 40+ files on
lexer/parser minutiae, 50+ on type-system internals, 60+ on Z3 and CNF
lowering, all valuable for correctness but none of which exercise a code path
that would exist without the user-facing verb around them.

Result: a user could file a bug against `calor migrate` and the test suite
would offer little diagnostic help; a user could hit an LSP rename-across-files
bug and CI would not catch it before merge.

### F2. Regression pinning is inconsistent

Sampled 10 notable fixes from `CHANGELOG.md` for 0.13.0/0.13.1/0.13.2:

| Fix | PR | Regression test? |
|---|---|---|
| Z3 semantics: integral width, signedness, promotions | `#961` | Partial — `IntegrationTests.WidthTypedIntLiterals_InContracts_VerifyWithoutError` covers width but not the promotion matrix |
| MCP memory admission scope | `#897` | **Yes** — `McpMemoryAdmissionTests.cs` (3 cases) |
| Cross-module effect resolution (`Module.Function`) | `#925` | **No** dedicated test found |
| For-loop non-additive incrementor | `#774` | **No** — referenced in a comment on `SwitchExpressionConversionTests` but not pinned |
| Records honest downgrade to interop | `#773` | Incidental — 7 tests reference #773 but none is explicitly a "records shall remain interop" regression pin |
| Formatting lossless / write-path re-enabled | `#760` | **Yes** — `FormattingTests.cs` plus shared LSP/CLI/MCP write-path gates |
| Silent substitutions eliminated (compound assignments) | `#774` follow-up | **No** — snapshot conformance updated, no before/after comparison test |
| Roslyn-valid C# generation guaranteed | `#939` | Incidental — round-trip harness validates compile, no isolated pin |
| Z3 download retry+backoff (release-only) | `#951` | **No** — bootstrap paths only run on release |
| CFG/dataflow rebuilt on explicit semantics | `#960` | Incidental — verification tests exercise CFG; no explicit "CFG must not silently absorb" pin |

**Score: 2 dedicated, 4 incidental, 4 none.** Incidental coverage is fragile
because the general test that "happens to touch" a fix can be edited later
without anyone realizing they have removed the only proof against
reintroduction.

### F3. The round-trip corpus gate does not alarm on incremental drift

The `roundtrip-verification` CI job runs against three SHA-pinned OSS
submodules (`MediatR`, `serilog`, `FluentValidation`). Its exit policy in
`RoundTripExitPolicy.cs` fails-closed on:

- Test inventory shrinkage
- Build failures on converted code
- Test result regression (baseline pass count decreases)
- TRX parse errors
- Coverage thresholds (MediatR native 40%, coverage 55%; per-project overrides)

But those thresholds are **absolute floors**, not deltas. A project going
from **155/157 → 154/157 passing** does trip the "test result regression"
check on that project — good — but a project going from **77 native + 4 with-losses**
to **76 native + 5 with-losses** (same coverage) does not obviously alarm.
The gate's own output on the just-shipped 0.13.2 PR showed
`FAIL MediatR: MinorRegressions` while all thresholds were technically green,
and the maintainer (correctly) merged past it because the diff was docs-only.
The next contributor may not have that context.

Related: the `roundtrip-verification` job runs full `harness run --all`
(~30 minutes) even for a docs-only PR. It is protected by needing
`conversion-scorecard` to pass first, but it still burns CI on runs it should
skip.

### F4. Cache invalidation has known open blind spots

Two cache paths, both with gaps.

**Z3 verification cache.** The cache key is
`${BuildStateCache.CurrentCompilerSemanticsVersion}|${ContractTranslator.SemanticsVersion}`
in `VerificationCache.cs`. `SemanticsVersion` is a **manually bumped**
constant. `VerificationCacheTests` has ~80 unit tests for hashing but **no
test that catches "we changed the translator and forgot to bump the version"**.
The 0.13.2 release notes explicitly call out "existing verification caches are
invalidated by this release" — that discipline is on the maintainer, not on CI.

**MSBuild incremental cache.** `#883` is a known-open residual: IL-analysis
inputs (`ReferencedAssemblies`, `RuntimeDirectory`, `NuGetPackageRoot`,
`DepsFilePath`) are **not included in the hash**. A user who swaps a NuGet
package version against a warm cache silently skips IL analysis, which may
skip effect discovery. There is no test for this, and it is filed as
"residual" rather than "pending".

### F5. Snapshot management has no safety heuristics

The snapshot mechanism (`SnapshotConversionTests` and siblings) does byte-equal
comparison against `.approved.calr` files under `tests/TestData/`. Update mode
is `CALOR_UPDATE_SNAPSHOTS=1`. This is safer than most snapshot systems
because CI never sets the variable — but nothing warns a *human* who does.

Failure modes not covered:

- A conversion that silently drops all comments passes the snapshot if the
  snapshot was updated on the same PR.
- A conversion that shrinks an interop `§CSHARP` block (arguably a regression)
  passes if the snapshot was updated.
- A conversion that gains a stub or "TODO" marker passes if the snapshot
  was updated.

There is no "suspicious diff" heuristic — no rule that says "if the number of
comments in the new output is less than in the old output, warn". Given the
suite has 768 snapshot files, a bulk snapshot update would be an easy place to
smuggle a subtle regression.

### F6. Property and fuzz coverage is thin

Grep for FsCheck / Hedgehog / QuickCheck / Bogus / AutoFixture returns
essentially one hit: `EffectMatchingPropertyTests.cs` uses FsCheck for effect
subsetting invariants. That is the only property-based coverage in the suite.

Areas that would benefit from property-based testing:

- Parser round-trip (`parse(pretty(x)) ≡ x`) — currently tested only as a
  side-effect of formatter losslessness on a static corpus.
- CNF lowering equivalence (`§Q(A) → CNF → Z3 ≡ §Q(equivalent form)`) —
  would catch a class of translator regressions structurally.
- Effect algebra laws (union commutativity, subset transitivity) beyond what
  `EffectMatchingPropertyTests` already does.
- ID canonicalization stability (`canonicalize(canonicalize(x)) = canonicalize(x)`).

None of these need Bogus or a full DSL generator — FsCheck plus small
hand-written generators would suffice.

### F7. Determinism gaps

- **Z3 is not seeded in tests.** Under memory pressure on CI runners, Z3
  heuristics differ and the solver can time out on inputs that pass locally.
  `#897` originally presented as "6 MCP tests flaky" and cost about two weeks
  of misdiagnosis before the memory-admission scope was identified as the
  actual cause. Not seeding Z3 was not the root cause, but it made root-cause
  identification harder.
- **Benchmark sampling uses unseeded `Random()`.** The 30-run statistical
  suite in `Calor.Evaluation` shuffles corpus order with `Random.Next()`
  without a fixed seed. Composite metric variance is therefore not exactly
  reproducible across machines; two different developers running the suite
  will produce slightly different distributions. The 1.32× headline is
  reproducible only in the aggregate.

### F8. Dev/CI parity — the corpus is invisible locally

`.gitmodules` pins MediatR, Serilog, and FluentValidation, but a
`git clone` of the repo does *not* auto-init the submodules. CI opts in
per job (`submodules: recursive` at `test.yml:59`, `:229`, `:719`); local
developers who want to reproduce a `roundtrip-verification` failure must run
`git submodule update --init --recursive` themselves.

This is not called out in `AGENTS.md`, `CLAUDE.md`, or the
`docs/contributing/development-setup.mdx`. A contributor debugging a
"MediatR: MinorRegressions" failure will start with a checkout that has no
MediatR at all and will get confusing "corpus submodules not initialized"
errors from the binder-ratchet log (line 120 of `test.yml`). The workflow
handles that gracefully; the developer will still have to figure out that
submodules are the fix on their own.

### F9. Structural over-coverage that has real cost

The compiler-internals tests are proportionate to code complexity, not to
user-visible behavior. The three areas most out of proportion:

| Area | Tests | User surface |
|---|---|---|
| Parser and lexer minutiae | 40+ files | Users see error messages, never write the grammar |
| Type system unification / constraint solving | 50+ files | Users see error messages and inferred types |
| Z3 and CNF lowering | 60+ files | Users see one of seven proof statuses |

None of this is *wrong*, and much of it is load-bearing (the verifier
differential harness is real work). But the fastest way to reveal an
under-tested user path is to notice that many hundreds of tests exist for a
grammar detail one user has ever asked about, while `calor migrate` runs on a
test that mocks the .csproj.

---

## Section 4 — Recommendations

Priorities are for a single maintainer. **P0** = a few days. **P1** = a
release cycle. **P2** = when quiet.

### P0 — close known regression-detection blind spots

- **R1. Add regression pins for `#925`, `#774`, and the Z3 semantics-version
  bump.** Three small tests: (a) `Module.Function` cross-module effect
  resolution round-trip; (b) for-loop non-additive incrementor pinned as a
  currently-failing xUnit `Skip("#774 known issue")` so a fix removes the
  skip rather than editing an unrelated snapshot; (c) a semantics-version
  guard that hashes the `ContractTranslator` AST and fails if the hash
  changes without a matching version bump.
- **R2. Add a regression pin for `#883`.** Test: warm a cache, swap a
  referenced assembly, assert IL analysis re-runs. Fails today; that is
  fine — filing it as a red test is the right shape of "known open".
- **R3. Add a snapshot-diff heuristic gate.** A pre-commit or CI script
  that reads every `.approved.calr` diff in the PR and warns if any of:
  comment count drops, interop block count drops, `§CSHARP` byte length
  drops by more than 20%. Warning, not error — the human still decides,
  but they are asked.
- **R4. Document the submodule init in `AGENTS.md` and
  `docs/contributing/development-setup.mdx`.** One paragraph explaining
  when submodules are needed locally and the exact command to init them.

### P1 — cover the top under-tested user surfaces

- **R5. End-to-end test for `calor migrate`.** Consumes a fixture `.csproj`
  from `tests/TestData/Projects/`, runs the CLI, asserts the produced
  `.calr` files compile and the report envelope schema matches.
- **R6. End-to-end test for `calor watch`.** Spin up the watcher on a
  fixture directory, edit three files with realistic timing, assert
  incremental recompilation batches correctly and the cache reports the
  expected hits.
- **R7. Multi-document LSP test.** Extend `LspE2ETests.cs` (or add a
  sibling) to open five documents, apply a rename that crosses all of
  them, and assert that references, diagnostics, and semantic tokens
  update consistently.
- **R8. Hook-invocation test in an agent-like scenario.** Simulate a
  Claude Code `PreToolUse` invocation on a `.cs` write, assert the
  installed hook rejects it with the expected diagnostic. Does not
  require a real Claude Code; requires only that we exercise the hook
  contract the way the agent would.
- **R9. Tighten the round-trip gate.** Add an incremental delta alarm:
  when any project's pass count decreases from the previous main-branch
  run, post a PR comment noting the delta, even if absolute thresholds
  are still green. This is a soft warning — the gate stays green — but
  it surfaces slow drift.

### P2 — structural improvements when quiet

- **R10. Seed Z3 in tests.** Not as a claim about correctness (Z3 is
  supposed to be deterministic on the same query), but as a claim about
  triage: a flake is easier to root-cause when only one non-determinism
  source is unbounded.
- **R11. Seed the benchmark `Random`** so two developers running the
  suite reach the same shuffle order. Composite headline still requires
  the corpus and the runner; adding a seed does not weaken the metric.
- **R12. Introduce two or three property tests** — parse-pretty
  round-trip, CNF equivalence, ID canonicalization. FsCheck is already a
  transitive dependency (`EffectMatchingPropertyTests` uses it).
- **R13. Skip `roundtrip-verification` on docs-only PRs.** A `paths-filter`
  or `changed-files` action can gate the ~30-minute job on any change
  outside `website/**` and `docs/**`. This is safe because docs cannot
  regress round-trip; unsafe only if we forget and edit compiler code
  under `docs/`, which the `paths-filter` catches by default.
- **R14. Add a "run tests locally" section to `CLAUDE.md`.** Two
  commands: a fast lane (`dotnet test tests/Calor.Compiler.Tests/`) and
  a full-corpus lane (`git submodule update --init && dotnet test`).

---

## Section 5 — What we are NOT recommending

Explicit non-goals to avoid scope creep in follow-up PRs.

- **Not** trimming any existing compiler-internals tests. They are
  load-bearing on the invariants the verifier depends on. F1 is a
  gap in user-surface coverage, not an argument for less internals
  coverage.
- **Not** replacing xUnit with a different runner. TRX inventory
  enforcement (`scripts/check_trx.py`) is already integrated and the
  matrix shard shape is coherent.
- **Not** consolidating the 13 test projects. The shard shape gives per-project
  timeout isolation on CI, and the projects group by module ownership
  (`Verification.Tests` for Z3 vs. `Semantics.Tests` for kind-checking) in
  a way that a merged project would lose.
- **Not** adopting a heavier mutation-testing framework (Stryker.NET,
  PIT). The existing `run_mutation_gate.py` is deterministic and
  schema-pinned; a full mutation run would take hours and be flaky
  against Z3. If mutation coverage becomes a target, extend the
  existing gate rather than replace it.
- **Not** adding fuzz targets outside the parser and CNF paths without
  first sizing the crash-triage surface. A single-maintainer project
  cannot absorb thousands of fuzz reports.

---

## Appendix — Data sources

- `.github/workflows/test.yml` (lines 47–1020) — CI shape.
- `eng/coverage-baselines.json` — coverage ratchets per module.
- `tests/TestData/` — 768 files across benchmarks, CSharpImport,
  LintScenarios, EditScripts, QueryCorpus, RenameScripts, Verification,
  LiteralRawSemantics, Formatting, Security.
- `bench/corpus/` — MediatR, Serilog, FluentValidation submodules
  (per `.gitmodules`).
- `CHANGELOG.md` — 0.13.0 / 0.13.1 / 0.13.2 fix inventory used to sample
  regression pinning (Section 3 F2).
- `src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs` — cache
  key derivation cited in F4.
- `src/Calor.Compiler/Mcp/Tools/` — MCP tool inventory (~17 tools).
- `src/Calor.Compiler/Commands/` — CLI subcommand inventory (~28
  commands).
- `tools/Calor.RoundTrip.Harness/` — round-trip exit policy cited in F3.

This audit was produced by three parallel research passes (structural
inventory, user-surface mapping, regression-detection quality) followed
by a manual reconciliation against source. The single discrepancy the
reconciliation caught was between "the OSS corpus is empty" (a local-only
observation) and "the OSS corpus is deterministic in CI" (correct once
`submodules: recursive` is accounted for). That discrepancy is itself
captured as F8.
