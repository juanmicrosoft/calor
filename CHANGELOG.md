# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Fixed
- The round-trip check no longer fails a pull request because of a test that
  MediatR's own test suite is known to flake on. The known-flake list was being
  dropped on the way into the check, and a flaky failure could still trip the
  "tests exited with 1" gate on either the baseline or the round-trip run. Ignored
  flakes are still listed by name in the report.

### Changed
- **Proof-based guard elision is now on by default.** When Z3 proves a
  contract — with `--verify` (postconditions) or refinement verification
  (`§PROOF` obligations, refinement-type entry/return checks, index-bounds and
  subtype obligations, reached today through the MCP `calor_refine` tool) —
  the compiler now leaves that runtime check out of the generated C#. You no
  longer need to pass `--elide-proven-guards` (the flag still works; it just
  restates the default). Only a clean `Proven` verdict with no assumptions
  attached qualifies. Preconditions, `Assumed`, `Timeout`, `Refuted` and every
  other verdict keep their guards exactly as before, and a compile that runs
  no verification at all is unchanged.
  - **To opt out** (keep every guard and use verification as a diagnostic
    only — the 0.13/0.14 behavior): pass `--keep-proven-guards` (or
    `--no-elide-proven-guards`) on `calor compile`, `calor run` and
    `calor test`; set `ElideProvenGuards = false` on `CompilationOptions`; pass
    `"keepProvenGuards": true` in the MCP `calor_compile` options; or set
    `<CalorElideProvenGuards>false</CalorElideProvenGuards>` in an MSBuild
    project (the MSBuild cache knows about this setting, so changing it
    recompiles).
  - **Why now:** in 0.13 we said we would only turn this on once a test suite
    showed that "Z3 says proven" and "the check never fails at runtime" always
    agree. That suite now exists, runs on every CI build, and reports zero
    disagreements across the 65 contract shapes it covers (40 of which can
    actually be elided). One honest caveat: the test suite runs the guarded
    version of the code and checks the shape of the elided version — it does
    not run the elided version.
  - The default is the same on every surface — CLI, `CompilationOptions`
    (used by the SDK, MCP tools, `review-packet`, `run`/`test`), the MSBuild
    task and `Sdk.targets` — and a test pins that they agree. (`calor watch`
    never runs verification, so it is unaffected.)

## [0.14.3] - 2026-08-24

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension (StructuralClarity): 1.84x (Calor)
  - ErrorDetection (DetectionCapability): 1.49x (Calor)
  - TokenEconomics (CompositeTokenEconomics): 1.42x (Calor)
- **Benchmarks Evaluated**: 217

### Fixed
- **`§NEW{Type}` constructor expressions carry `NotAnnotated`, not
  `Oblivious`.** A `new Foo()` is provably non-null by construction, so
  the `BoundType` returned by `BoundNewExpression` now reflects that.
  Applies to bare `§NEW{Foo}`, `§NEW{Foo} §A x §/C` (with constructor
  arguments), and generic forms like `§NEW{List<int>}`. Downstream
  nullability checks that read the expression's annotation now see the
  correct non-null claim instead of degrading to Oblivious.

## [0.14.2] - 2026-08-24

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension (StructuralClarity): 1.84x (Calor)
  - ErrorDetection (DetectionCapability): 1.49x (Calor)
  - TokenEconomics (CompositeTokenEconomics): 1.42x (Calor)
- **Benchmarks Evaluated**: 217

### Added
- **`Calor0274 NullableArgumentToNonNullableParameter` now fires on pure-Calor
  call sites**, not only BCL-resolved ones. `BindCallExpression` was
  previously scoped to `TryResolveBclCall` per the S4 comment; it now
  additionally checks `resolution.Function.Parameters` (which carry
  `NullableAnnotation` from the parameter-annotation flow that landed in
  0.14) and routes through the same `NullabilityChecker.IsPossiblyNullAssignedTo`
  predicate. `?Foo` argument into `:Foo` parameter on a `§C{this.Take}` call
  now surfaces the diagnostic symmetrically with the BCL path. Scalar STRING
  Calor-native fire is still blocked by a separate scope gap in `ResolveCall`
  (does not OPTION-unwrap arg types on the Calor-native branch); pinned as a
  known-limitation follow-on.
- **`BoundCallExpression.Type` on pure-Calor calls now carries the declared
  return-type annotation**, not `Oblivious`. A call to `-> ?string` /
  `-> ?Foo` surfaces as `Annotated`; `-> string` / `-> Foo` as `NotAnnotated`.
  BCL-resolved returns retain priority (`MetadataBinder` still wins). Uses
  `TryReadDeclaredStringAnnotation` and wraps the raw `returnType` string
  so `DisplayString` stays byte-identical for downstream consumers
  (`BinderOverloadSetTests`, `IdScanner`, LSP hover).

### Not in this release
- **S8-Oblivious widening** — extending S8 to fire on `Oblivious` user-ref
  sources depends on `§NEW` constructor annotation flow and BCL user-ref
  return-type annotation flow, which aren't yet in place. Draft PR #1078
  documents the 100+ real-world corpus regressions and the missing
  precursors.

## [0.14.1] - 2026-08-24

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension (StructuralClarity): 1.84x (Calor)
  - ErrorDetection (DetectionCapability): 1.49x (Calor)
  - TokenEconomics (CompositeTokenEconomics): 1.42x (Calor)
- **Benchmarks Evaluated**: 217

### Added
- **Nullability enforcement widens to whitelisted generic instantiations
  (`Option<T>`, `List<T>`, `IList<T>`, `IEnumerable<T>`, `IReadOnlyList<T>`,
  `ICollection<T>`, `IReadOnlyCollection<T>` over `string`).** A source
  whose payload/element carries `?string` assigned into a target whose
  same-shape container declares `string` now trips the three nullability
  diagnostics symmetrically with the scalar STRING gate. Container
  annotation stays orthogonal — only the position-0 type-argument
  mismatch is diagnosed.
- **Nullability enforcement widens to user-declared reference types.**
  `§B{b:Foo} a` where `a` is `:?Foo` now trips `Calor0272` (and the
  return-site analog trips `Calor0273`). Fires only when the source
  is explicitly `Annotated` — `Oblivious` sources (the default for
  any Calor-native value that has not yet been annotated) do NOT
  fire, so ordinary `§B{x:Foo} someCall` patterns keep working
  until Calor-native call-site annotation flow lands as a follow-on.

### Changed
- **`MetadataBinder` constructs `GenericInstantiationBoundType` from
  Roslyn's `INamedTypeSymbol.IsGenericType`.** Mirrors the array-type
  handling added in 0.14.0 so BCL calls returning generic-of-STRING
  surface with correct payload/element annotations at the emit site.
- **Reference-expression type flows the declared nullability for
  user-declared reference types.** A `§MT{...} (?Foo:a)` parameter
  reference now reads as `Annotated Foo` at its use site instead of
  degrading to `Oblivious`. Leading/trailing `?` is stripped from
  the `QualifiedName` so downstream short-name comparisons work
  without double-encoding the annotation.
- **Diagnostic messages echo the target shape.** `'Option<string>'` /
  `'List<string>'` / `'Foo'` labels replace the scalar `'string'`
  boilerplate when the target is a generic or user-declared type.

### Fixed
- **Site-header "v0.13.2" pill lingered after 0.14.0.**
  `website/src/lib/version.ts` was not in the release skill's
  version-file checklist. Added to the skill so future releases catch it.
- **Performance-test synthetic fixtures now filter nullability
  diagnostics.** After the 0.14.0 severity flip promoted `Calor0272/3/4`
  to Error under SemVer.Major≥2, the taint-analysis performance tests'
  `RequireParsedAndBound` blanket `HasErrors` assertion rejected the
  synthetic modules. Nullability findings on synthetic taint fixtures
  are now allowed through — the perf tests exercise timings, not
  nullability enforcement.

### Added — infrastructure
- **`verify-release.yml`** — on-demand `workflow_dispatch`
  workflow that installs the `calor` global tool from nuget.org
  (linux-x64 / osx-arm64 / win-x64) and asserts
  `calor verify samples/Verification/proven-contracts.calr` prints
  `Proven: 14, Skipped: 0`. Reusable per release:
  `gh workflow run verify-release.yml -f version=X.Y.Z`. Fills the
  install-and-verify gap that the create-release skill's local check
  cannot cover from networks whose dotnet routes through a proxy.

## [0.14.0] - 2026-08-23

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension (StructuralClarity): 1.84x (Calor)
  - ErrorDetection (DetectionCapability): 1.49x (Calor)
  - TokenEconomics (CompositeTokenEconomics): 1.42x (Calor)
- **Benchmarks Evaluated**: 217

### Added
- **Nullability enforcement for `:string` values.** The binder now checks whether a
  possibly-null value (Roslyn `Annotated` or `Oblivious`, per D3) is being funneled into
  a Calor `:string` target and emits diagnostics with codes `Calor0272`, `Calor0273`,
  and `Calor0274`:
  - `Calor0272 NullableToNonNullableBinding` — a `§B{x:string}` binding is initialized
    from a possibly-null value. Example: `§B{s:string} §C{System.Environment.GetEnvironmentVariable} §A STR:"HOME" §/C`.
  - `Calor0273 NullableReturnFromNonNullable` — a function declared `-> string` returns
    a possibly-null value.
  - `Calor0274 NullableArgumentToNonNullableParameter` — a call passes a possibly-null
    argument into a `:string` parameter.
- **Severity is gated on `SemanticsVersion.Major`.** The three diagnostics emit at
  `Error` when Major is `>= 2` (the current default after this release's bump) and
  `Info` otherwise. Legacy modules declaring `§SEMVER[1.0.0]` will keep the pre-flip
  Info severity once the module-level `§SEMVER` directive is threaded through the
  binder in a follow-on slice.
- **Array element types participate in the check.** `[]string` targets are non-null-element
  arrays; a source with `[]?string` element annotation trips the same three codes when
  the container annotation is orthogonal to the element mismatch.
- **Declared nullability flows through parameters, fields, properties, lambda
  parameters, and foreach loop variables.** Where previously only local `§B` bindings
  carried the annotation, all `VariableSymbol` creation sites now inherit the declared
  `NullableAnnotation` — so `§F{f1:Ok:pub} (?string:name)` references correctly report
  as `Annotated` at their use sites.

### Changed
- **`SemanticsVersion.Major` bumped from 1 to 2.** Signals the new default nullability
  contract described above. Compilation-time telemetry and cache validity carry the new
  version so existing Z3 verification caches invalidate cleanly.
- **BCL call signatures now carry Roslyn `NullableAnnotation`.** `MetadataBinder` returns
  a `BclCallResolution` record with return + parameter types + parameter names, and the
  binder consults it to compute the source annotation for the three diagnostics above.
- **`BoundStringLiteral`, `BoundInterpolatedStringExpression`, `BoundBinaryExpression`
  (STRING result), `BoundStructuralExpression`, `BoundConditionalExpression`, and the
  `object.ToString()` result are stamped with the correct annotation** — literals are
  `NotAnnotated`, conditional expressions fold NEVER branches, and `ToString` is
  narrowed to `Annotated` because overrides can return `null`.
- **Local functions now fail safe during C# migration (#777).** Any containing member is preserved
  verbatim as counted C# interop instead of hoisting nested functions and breaking captures,
  recursion, generic constraints, modifiers, or ref semantics. Feature scorecards now report this
  containment as partial rather than native conversion.
- **Standalone C# block scope is preserved during migration (#751).** Members containing bare
  `{ }` statement blocks now remain counted C# interop instead of flattening lexical scopes,
  duplicating sibling local names, or dropping all but the first child statement.
- **AST change amplification is mechanically constrained (#791).** A single AST schema now
  generates visitor interfaces, centralized exhaustive dispatch, and structural metadata;
  aggregate transforms use metadata-preserving `ModuleNode.With` copies; emitter reuse and
  compiler component dependency contracts are enforced by architecture tests.

### Fixed
- **`BoundVariableExpression` inherits declared nullability from its `VariableSymbol`.**
  Previously local references silently degraded to `Oblivious`, causing follow-on
  bindings to miss `Calor0272`. Handles `:string`, `:?string` (prefix), `:string?`
  (postfix), and inferred-type locals.
- **`NominalBoundType.Equals` includes the `NullableAnnotation` field.** An architecture
  test guards against callers constructing `new NominalBoundType("STRING")` and
  comparing against a literal-derived Type — those silently mismatched under the
  previous annotation-agnostic equality.

### Not in this release
- **S7 (generic instantiations) and S8 (user-declared reference types)** are deferred
  to a follow-on. The `GenericInstantiationBoundType` infrastructure already carries
  annotations on type arguments, but the binder does not yet consult it for well-known
  containers (`Option<T>`, `List<T>`, etc.); user-declared classes need declaration-site
  annotation propagation.

## [0.13.2] - 2026-08-12

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (Calor)
  - ErrorDetection: 1.49x (Calor)
  - TokenEconomics: 1.42x (Calor)
  - RefactoringStability: 1.38x (Calor)
  - InformationDensity: 0.98x (C#)
- **Benchmarks Evaluated**: 217 (Calor compiled 217, C# 216)

### Note on 0.13.1

**0.13.1 was tagged but never published.** Four publish attempts failed — two on repository defects
(a publish gate that exhausted a single runner, and download retries configured with
`--retry-delay`, which *disables* curl's exponential backoff and so gave up after ~15s), one on the
#897 MCP bug below, and one on a GitHub incident affecting release-asset downloads. Rather than
publish 0.13.1 from a `main` that had since gained a compiler-internals rewrite it did not describe,
that version is abandoned and its contents ship here, described accurately. Nothing was ever pushed
to nuget.org under 0.13.1.

### Changed
- **Z3 translation aligned with executable C# semantics (#961, closes #780).** Integral literal width
  and signedness are preserved through translation, and C# binary/unary numeric promotions are
  applied across arithmetic, comparison, equality, shifts and overflow. Operations with no
  executable C# semantics stay fail-closed as `Unsupported` rather than being approximated. The
  translator's semantics are now versioned in proof results and in verification-cache validity, so
  **existing verification caches are invalidated by this release** and proofs are re-established
  under the corrected semantics. Backed by a 576-case runtime differential promotion matrix across
  every integral type and its boundaries.
- **CFG and dataflow rebuilt around explicit semantics (#960).** Control flow is constructed from
  explicit terminators and typed edges instead of positional inference; loop, exception, catch,
  finally, using, return, throw, break and continue now route structurally. Dataflow boundaries are
  separated from lattice identities and fail explicitly rather than silently on non-convergence;
  initialization, liveness and reaching-definitions analyses are symbol-keyed and semantically
  ordered. CFG/dataflow failures surface as `Calor0932` internal diagnostics instead of being
  absorbed.
- **Builds and releases are hermetic and supply-chain verified (#954).** Build-time Z3 downloads and
  `deps.json` mutation are removed: Z3 is restored by an explicit bootstrap, and every supported
  binary is verified against committed SHA-256 *and* byte-size pins before compilation. NuGet
  versions are centralized with committed lock files and locked restores in CI and releases. Adds
  offline build/test/pack, corrupt/missing-asset, lock-mismatch, clean-worktree and runtime-load
  gates, plus SPDX SBOMs and SLSA-style provenance.
- **Round-trip verification is failure-safe (#950).** Project round-trip conversion runs in lossless
  mode and validates generated C# before publication, failing closed on build, process, TRX,
  inventory and coverage failures with explicit thresholds. Excluded and failed items stay visible
  in reports rather than being silently dropped.

### Fixed
- **Z3 downloads survive a real outage (#951).** Every fetch path used
  `--retry 5 --retry-delay 3`. Per `curl(1)`, "By using `--retry-delay` you disable this exponential
  backoff algorithm" — so the flag that reads as hardening switched the backoff off, pinning retries
  to a flat 3s and exhausting them in ~15 seconds, which is shorter than a routine CDN blip.
  Measured at `--retry 5`: 16s with the old flags, 32s with backoff restored. Fixed across the
  bootstrap scripts and all four workflow fetch sites, including the publish path itself, and
  `--retry-all-errors` added so failures that are not an HTTP status (e.g. a dropped connection)
  enter the retry loop at all. The Windows path, which had no retry whatsoever, now backs off too.
- **MCP heavy-tool admission control no longer charges a host process's memory to the next tool
  call (#897).** `calor_compile`, `calor_convert`, `calor_analyze` and `calor_batch` are gated on
  process memory before they start, measured with `Process.WorkingSet64` — the *whole* process.
  Under `calor mcp` that is sound, because the server owns its process. It was applied
  unconditionally, so in any host the handler does not own it attributed that host's memory to the
  next MCP tool, waited its full 30s, and refused with *"Server under memory pressure"*.

  This is what six `McpServerTests` had been failing on since 2026-08-10 — only on Linux CI, where
  the test host (thousands of tests plus Roslyn and Z3 in memory) crossed the 50%-of-RAM threshold
  that a higher-memory dev machine never reached. It was filed and re-run as a flake for two weeks;
  it is deterministic given the memory condition.

  The gate is now scoped by an explicit policy: enabled for the stdio server, disabled for direct
  construction. **Behaviour change for embedders:** code that constructs `McpMessageHandler` (or
  `McpServer`) itself no longer gates tool calls on process memory. No public signature changed.
  `CALOR_MCP_MAX_MEMORY_MB` still tunes the server's ceiling, and is now read per construction
  rather than once per process.

### Removed
- **VS Code extension support is withdrawn.** The `editors/vscode` tree, the VSIX release-asset
  workflow, the Marketplace publishing workflow, and the single-file publish guard are all removed.
  The extension had no Marketplace presence since v0.3.8 and no demonstrated demand, and its VSIX
  build was a recurring release blocker (#951: six parallel platform builds each fetching Z3
  archives with no retry).

  **The language server is unaffected.** `calor lsp` speaks standard LSP over stdio and works with
  any LSP-capable editor; diagnostics, definition, references, symbol-exact rename, formatting and
  semantic tokens all remain supported and tested.

## [0.13.1] - 2026-08-12

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights** (advantage ratio, Cohen's d where reported):
  - Comprehension: 1.84x (Calor, d=1.80 large)
  - ErrorDetection: 1.49x (Calor, d=1.21 large)
  - TokenEconomics: 1.42x (Calor)
  - RefactoringStability: 1.38x (Calor, d=7.09 large)
  - EditPrecision: 1.36x (Calor, d=4.90 large)
  - InformationDensity: 0.98x (C#, d=-0.52 medium)
- **Programs Tested**: 217 Calor / 216 C# compiled successfully

### Added
- **Persistent project index and `calor query`** (roadmap §2.2, the fourth-deferral
  item, now shipped). `calor index build` writes a versioned index under
  `obj/calor/`; `calor index status` reports whether it may still be trusted.
  `calor query` answers six facets: `symbol`, `callers`, `callees`, `impact`,
  `contracts`, `assumptions`. Two disciplines are enforced rather than
  documented: a stale index never answers — it rebuilds, or refuses under
  `--no-build` — and every answer carries its residual, because Calor binds one
  file at a time and a cross-file call resolves only when exactly one
  declaration bears the name. Answers say `PARTIAL` and name what was dropped.
- **`calor rename`** — SymbolId-addressed rename across a project, with edits
  derived from bound-tree identity rather than text. Refuses rather than
  guessing on ambiguity, stale sources, name collisions, declarations split
  across files (#922), and type declarations whose references are not yet
  indexed.
- **Dogfood utility** (`tools/calor-allowlist-audit/`) — a real in-repo tool
  whose only source is Calor. It audits `.calor-csharp-allowlist` against rules
  that file states in prose and nothing enforced, and CI both builds and runs
  it, so a Calor regression that breaks this program breaks the build.
- **Release gates closed**: §2.5 gate 2 (full-vs-incremental diagnostic
  identity, over a registered ES-01..ES-07 edit-script corpus), gate 3
  (index/query correctness, over a hand-authored golden corpus), gate 4
  (rename, with an apply-recompile-and-run behaviour oracle), and gate 8
  (performance envelope: index build 0.40s against a 30s budget, warm queries
  0.0-0.3ms against 500ms, on a 106-file/11.1k-line corpus).
- Exact-span LSP refactoring (#765) indexes every open and closed workspace
  `.calr` file by stable `SymbolId`, resolves cursors only through exact
  identifier-token occurrences, and incrementally reparses changed or deleted
  closed files. Definition, references, and rename distinguish overloads,
  shadowed locals and fields, unrelated members, and same-spelled symbols across
  files.
- A CI-visible exact-span refactoring gate applies adversarial multi-file edits,
  reparses and rebinds the workspace, emits C#, and requires a clean Roslyn
  compilation.
- CI and release quality gates are now truthful (#938): a test-inventory
  manifest pins per-project test counts, with coverage, mutation, flake and
  performance ratchets alongside.
- `Calor.Sdk` package consumers are release-grade (#937), and round-trip gates
  require semantic validity (#945, #932).

### Fixed
- **Module-qualified cross-module calls resolve** (#925). Writing
  `§C{Module.Function}` produced a *worse* result than the bare form: it fell
  through to "unknown external call" and forbade the call inside a pure function
  even when the callee was itself pure. The dotted path now consults the same
  cross-module map the bare path always did.
- Generated C# is guaranteed Roslyn-valid (#939).
- Formatting is lossless and atomic (#918).

### Changed
- LSP rename is advertised by default. The experimental
  `CALOR_LSP_EXPERIMENTAL` gate and production name-only reference/rename
  collectors were removed after the exact-span gate passed.

- **VS Code Marketplace publishing is no longer a release commitment.** The Marketplace listing
  deliberately stays at v0.3.8. The channel has been broken since v0.4.0, five months of staleness
  produced zero complaints, and recurring token/publish-chain maintenance is not justified by
  demonstrated demand. (Superseded immediately after this release: VS Code extension support was
  withdrawn entirely — see the Unreleased section.)

### Note on scope
This is a 0.13.x release. It completes the 0.13 project-model program and its
release gates; it is **not** 0.14. The 0.14 "Null-Safe .NET" content —
metadata-backed .NET binding, the typed semantic representation, the typed CFG
null-state slice, enforced non-nullable reference types, and the 2.0.0
self-migration — remains unbuilt, and 0.14 will ship as `§SEMVER{2.0.0}` when
it does.

## [0.13.0] - 2026-08-11

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads, 32.0%)
- **Category wins**: Calor 6, C# 0 — Comprehension 1.84x, ErrorDetection 1.49x,
  TokenEconomics 1.42x, RefactoringStability 1.38x, EditPrecision 1.36x,
  Correctness 1.29x

The "Trustworthy Project Model" release (roadmap v0.13). Headline: the #762 binder
rebuild is complete — all 60 accepted expression classes structurally bind (60/60
Tier A; the unsafe/pointer classes were promoted from residual to structural by the
final totality merge, and the Calor0259 analysis-incomplete instrument is RETIRED —
no emitter remains because nothing is incomplete). Landed twice over: the reviewed
B1–B8 incremental series plus the #900 consumer migrations (SymbolIds,
full-signature overloads, exhaustive checker traversals, call graph, LSP
rename/references/cross-file).
Verification cache keys are exhaustive and semantics-versioned (#778), the MSBuild
incremental cache fingerprints every diagnostics-affecting input (#788/#890 incl.
the #883 IL-analysis inputs), and the Z3 asset chain is closed end-to-end with a
registry-verified republish.

**Release-gate scorecard (§2.5), stated plainly rather than claimed:**
- **Green, measured**: gate 1 (binding totality — zero Tier A incomplete on both
  corpus legs, ratcheted); gate 5 (verifier-runtime differential program — 65/65
  forms, 1,170/1,170 cells, zero mismatches, CI-blocking); gate 6 (PP-S4 migration
  fixture registry, enforcing).
- **Executes post-publish**: gate 7 (clean-consumer per-RID; the osx-x64 leg's
  oracle is now the documented loud degradation, per the chain-closure decision).
- **NOT met — instruments unbuilt, disclosed**: gate 2's diagnostics leg
  (full-vs-incremental identity: the F-3 edit-script corpus and identity harness
  were registered but never built) and gate 4 (rename harness with
  apply-recompile-and-test oracle and shadowing corpus). Those two are the
  unconditional gates in the plan of record; this release ships without them and
  says so — they are the top of 0.13.x. (Gate 2's index/packet legs are
  conditional and defer with the index.)
- **Not measurable, deferral recorded — including the one the plan said would not
  happen again**: gates 3 and 8 depend on the persistent index / `calor query`,
  which did not ship. Their SHOULD-tier deferral to 0.13.x is recorded here per
  §2.5's tier-decision rule — and stated plainly: the roadmap's §2.2 index bullet
  carried a pre-commitment ("executed, not deferred a fourth time"), and this IS
  the fourth deferral. No euphemism; it is the first index work item in 0.13.x.

Known channel state: the VS Code marketplace remains stuck at 0.3.8 (expired
VSCE_PAT, maintainer-only); this release does not change that.

### Added
- **Issue #779's residual verifier-vs-runtime differential gate (F-4)** now generates and executes
  1,170 deterministic cases: all 65 frozen modeled forms × `§Q`/`§S`/explicit `§PROOF` × nesting
  depths 1–3 × provable/refutable polarity. The oracle compiles guard-forced generated C#, covers
  bounded-quantifier emitter lowering and the separate obligation-elision path, rejects vacuous or
  mislabeled cases, and pins fail-safe handling for unsupported, timeout, solver-error, unavailable,
  and assumed outcomes. CI blocks on zero mismatches and byte-checks published JSON/Markdown
  metrics. Coverage now requires decisive production solver outcomes, with only exact documented
  assumption sets accepted. Field access now checks the full `u8` range `0..255`, with explicit
  `i8` and `i32` mutation controls. Dotted references never guess missing fields as `i32`; the
  module registry merges partial/nested declarations and includes accessible inherited instance
  fields with exact types. Scalar and array-element rows now use width/signedness boundary
  predicates and aligned runtime witnesses instead of self-equality, including an array-select
  signedness regression pin. Generated assemblies are collectible, worktree `.git` files are
  supported, and report paths are LF-pinned. Current coverage is 65/65 solver-handled forms and
  1,170/1,170 solver-handled cells with zero mismatches; 40/65 forms currently elide.

### Removed
- **The `osx-x64` Z3 native is no longer shipped** (Z3 chain closure). Upstream's
  x64-osx archive contains an arm64 binary under the x64 label, so Intel Macs
  installed successfully and silently lost verification — no honest asset exists
  to ship. Intel macOS is unsupported for verification (Z3-dependent features
  report "Z3 unavailable"; compilation is unaffected). The osx-arm64 native now
  ships from the same checksum-verified upstream archive as every other RID
  (the last source-built, non-reproducible asset is gone), and a native
  arch-vs-RID assertion in the packaging workflow fails closed if any upstream
  archive is ever mislabeled again.

### Changed
- **The binder dispatches every expression class** (#762 B1): a single authoritative dispatch
  table replaces the partial switch; expressions without a structural binder yet produce an
  explicit `Calor0259` (analysis incomplete; Info until B8, Warning at release) instead of a silent opaque fallback,
  counted by a ratcheted corpus instrument. `BoundCallExpression` is no longer `sealed` (the
  incomplete node subclasses it to preserve analysis behavior exactly) — an API-surface note
  for SDK consumers pattern-matching bound trees. MCP `analyze` no longer counts Info-severity
  diagnostics as issues.
- **Proof-based guard elision is now opt-in** (`--elide-proven-guards` on the CLI,
  `ElideProvenGuards` on `CompilationOptions` and the MSBuild task). By default a `Proven`
  postcondition or `Discharged` `§PROOF` obligation keeps its runtime check — verification
  verdicts are diagnostic. This executes roadmap v0.13 §2.1: the elide surface has produced
  seven false-`Proven` vectors to date. Its differential gate (freeze registration F-4) is
  now BUILT AND GREEN in this release (65/65 forms, 1,170/1,170 cells, zero mismatches — see
  Added); flipping the default back on is a deliberate recorded decision for the next cycle
  (roadmap §3.5 gate 6), not an automatic consequence of the gate turning green mid-cycle. The `run`/`test` execution paths always keep guards regardless of the flag.
  Note: `§PROOF` obligation guards have always ignored `--contract-mode` (pre-existing for
  Failed/Timeout); with the flip, a Discharged obligation under `--contract-mode off` now
  also emits its guard unless the opt-in is set — Off-mode output is guard-free by mode
  only for `§Q`/`§S` contracts, not obligations.

### Corrected
- **The v0.12.1 notes below understated the VS Code outage by about six times, and implied a
  publish that did not happen.** They said the extension "can be published again" after "three
  consecutive releases" (v0.9.0, v0.10.0, v0.12.0). Both parts were wrong. The publish has failed
  on **every release since v0.4.0 (2026-03-09)** — the last success was v0.3.8 and the marketplace
  has been stuck there for twenty-seven releases across five months. (Erratum 2026-08-10: this
  correction originally said "roughly nineteen"; the release list v0.4.0–v0.12.1 counts 27,
  excluding the z3-binaries asset tag.) The claim was also
  self-refuting: had only three failed, the marketplace would read v0.8.0. And v0.12.1 itself did
  not publish either — the `IL3000` build failure was fixed, but the publish step then hit an
  expired marketplace token, a second and older blocker that the build failure had been masking.
  The v0.12.1 entry has been amended in place.

### Fixed
- **Formatting is lossless and write paths are re-enabled (#760).** The formatter
  now edits the original source representation instead of re-emitting a
  trivia-free AST; comments/doc comments, blank lines, strings, raw C#, user
  identifiers/types/member targets, structural IDs, newline sequences, encoding,
  and BOM are preserved. The broad tag/identifier abbreviation regex is gone.
  `format --write`, `lint --fix`, MCP formatting, and LSP formatting share
  semantic-token, idempotence, generated-C# Roslyn compilation, and public-API
  equivalence gates. File writes use a same-directory flushed temporary file and
  atomic replacement, with byte-identical rollback on any validation, concurrent
  edit, or injected failure. Inputs with pre-existing semantic/generated-C#
  failures are explicitly reported as unsupported and conservatively left
  unchanged; structural ID migration remains the dedicated, structurally
  classified `fix --compact-ids` operation.
- **ARM64 macOS no longer builds Z3 from source**, cutting `download-z3.sh` from roughly 20
  minutes to under 15 seconds there. The workaround it replaces was justified as "pre-built Z3
  binaries have compatibility issues" on that platform; whatever the original failure was, it is
  not reproducible from the record, and the upstream `arm64-osx` native is verified working today
  (`Calor.Verification.Tests` 359/359, zero skips, on a suite that *skips* rather than passes when
  Z3 cannot load). This was the sole reason the VS Code publish workflow took ~23 minutes.
- **Z3 downloads can no longer install a native under the wrong RID.** Both download scripts
  selected the extracted library with a recursive search over one shared scratch directory, but
  the library names are not unique across archives — three ship a `libz3.dll`, two a `libz3.so`,
  two a `libz3.dylib`. A leftover extract could therefore be picked for a different RID, giving
  (for instance) an arm64 `libz3.dll` to `win-x64`. The checksum guard cannot catch this: it
  verifies the *archive*, never the file that lands in `runtimes/`. Extraction is now isolated per
  RID, and the scratch directory is cleaned on failure as well as success.
- **Stale Z3 artifacts are now detected and replaced.** `z3/` and `runtimes/` are gitignored, and
  every check was a bare existence test, so artifacts were kept regardless of origin. A checkout
  that had run the old ARM64-macOS source build kept its Debug, source-compiled wrapper
  indefinitely while newly added RIDs came from upstream — a silent mix, from which a local
  `dotnet pack` shipped the Debug wrapper. A provenance stamp now records which upstream archive
  supplied the wrapper, and a mismatch discards and refetches everything. `Calor.Compiler.csproj`
  gates on that stamp instead of on a single RID's native, which it previously did — and that RID
  was precisely the one the old source build produced, so the download step never ran on exactly
  the machines that needed it.
- **`publish-vscode` no longer strands platforms after the first failure.** The publish loop ran
  under `bash -e` with no error handling, so one failing target aborted the step and the remaining
  platforms were never attempted. Every target is now attempted, an already-published target
  counts as success (making retries idempotent), and the step fails at the end with a list.

### Removed
- **`build-z3-from-source.sh`**, now unreferenced by any executable path. It cloned a **mutable
  git tag** with no commit pin and no checksum — the only unverified fetch left in the toolchain,
  and a documented residual under #789 — and produced exactly the Debug wrapper v0.12.1 was cut to
  stop shipping. Its removal retires that residual; the disclosure in
  `scripts/z3-upstream-4.15.7.sha256` is updated accordingly.

### Resolved (was "Known issues" while this section accumulated)
- **The mislabeled `libz3-osx-x64.dylib` no longer ships — resolved inside this release** by the
  Z3 chain closure (see Removed above): the RID was dropped, the stale asset was deleted from the
  binaries release, and the arch-vs-RID packaging assertion prevents recurrence. The earlier text
  of this entry ("the remedy is undecided") described the pre-closure state.

## [0.12.1] - 2026-08-07

**A packaging release. v0.12.0 was tagged but never installable** — both publish workflows
failed, so nuget.org continued to serve `0.10.0` and the VS Code marketplace continued to serve
`0.3.8`. Nothing in the language, compiler, or verifier changed here; if you already build from
source, this release contains nothing for you.

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32 (Calor/C#)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (large effect)
  - ErrorDetection: 1.49x (large effect)
  - TokenEconomics: 1.42x (small effect)
  - InformationDensity: 0.98x (**C# wins**, medium effect)
- **Programs Tested**: 217

**Identical to v0.12.0 in every digit, and that is the expected result, not a coincidence.** The
suite was re-run at the documented 30 iterations; the regenerated dashboard JSON differs from
v0.12.0's by exactly two lines, the timestamp and the commit hash. These metrics are computed by
deterministic static analysis, and this release changes no analyzed code path. The zero-width
confidence intervals carry the same defect disclosed in v0.12.0 and are still not fixed.

### Fixed
- **The VS Code extension builds again** — though it still did not publish; see the correction
  note under Unreleased. The `IL3000` build failure below is fixed and all six platform packages
  are produced, but the publish step then failed on an expired marketplace token, so the
  marketplace remains at `0.3.8`. The language server is packed with `PublishSingleFile=true`,
  which promotes `Assembly.Location` to an `IL3000` error under `TreatWarningsAsErrors`. Fixed at
  both sites: `Z3ContextFactory` carries a justified suppression (the empty location is a
  supported input there and is already guarded), and `ImportCommand` now uses
  `RuntimeEnvironment.GetRuntimeDirectory()`, the single-file-safe equivalent — the previous
  expression would have silently emptied the framework probe root used by IL effect analysis.
- **The NuGet publish no longer fails its own checksum gate.** The gate was right; the pinned
  `z3-binaries` release had been republished underneath its manifest. Two underlying defects are
  fixed rather than papered over by rehashing: the managed `Microsoft.Z3.dll` was selected with
  `find | head -1` across all build artifacts, and `linux-arm64` was built from source despite
  upstream publishing a binary for it. Both now come from checksum-verified upstream archives, so
  the published assets are reproducible.
- **The shipped Z3 managed wrapper is now upstream's Release build.** Because of the selection
  race above, released packages had been carrying a **Debug** `netstandard1.4` assembly compiled
  on a CI runner, with that runner's absolute build path embedded in it.
- **Z3 now loads on ARM64 consumers.** Upstream's `x64-win` archive ships an AMD64-marked wrapper
  that throws *"the assembly architecture is not compatible with the current process
  architecture"* in an arm64 process; the glibc archives ship the architecture-neutral build.
  Both `download-z3.sh` and `download-z3.ps1` had designated the AMD64 one — latent because x64 CI
  cannot observe it and macOS/arm64 diverts to a source build. This affected `linux-arm64` and
  `win-arm64`.
- **Republishing the shared Z3 binaries is now a deliberate act.** `build-z3.yml` no longer runs
  on push, which is what invalidated the pin originally: the commit that *introduced* the manifest
  re-triggered the workflow and republished every asset a day later.

### Added
- **CI now exercises both release-only paths**, which is why neither failure was catchable before
  release day. `test.yml` runs the same single-file publish the VSIX build uses (~1m10s), and a
  new scheduled `z3-pin-check` workflow verifies the binary pins daily and on changes to the
  pinning machinery — against both the repo's release and the upstream Z3Prover archives.
- `build-z3.yml` asserts that the wrapper it is about to publish is architecture-neutral, and
  fails closed otherwise. An architecture-specific wrapper builds fine and passes x64 CI, so it
  would only break on consumers whose machines never run the publish.

## [0.12.0] - 2026-08-06

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32 (Calor/C#)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (large effect)
  - ErrorDetection: 1.49x (large effect)
  - TokenEconomics: 1.42x (small effect)
  - InformationDensity: 0.98x (**C# wins**, medium effect)
- **Programs Tested**: 217

**Read these with the caveat they deserve.** Every confidence interval in this run is
zero-width (`[1.84, 1.84]`), because these metrics are computed by static analysis and are
deterministic: the 30 runs are 30 identical runs. The interval measures run-to-run variance,
of which there is none, and *not* uncertainty about the underlying claim — sampling error over
the 217-program corpus is real and is not what these numbers report. The reported p-values
inherit the same defect. The measurements are unchanged from v0.10.0 on this corpus; only the
timestamp and commit moved. Filed as a known defect in the dashboard's statistics, not fixed
in this release.

**And a scope caveat.** These are the C#-versus-Calor micro-benchmarks. They are **not** the
release gates — PP-A1 and PP-W5 are, and PP-W5 explicitly adjudicates nothing about Calor
versus C#.

**This release covers the v0.11 range as well** — there is no `v0.11.0` tag; the maintainer folded
v0.11 forward, so everything below ships together.

**Both release gates pass.** **PP-A1** (CI adoption gates, all nine frozen items) at A-1.8;
**PP-W5** (toolchain parity, epoch `w5-parity-002`) at A-1.9 — point estimate 1.0016 after a metric
erratum, registered as *no large tax detected*, explicitly not a proof of parity.

**The headline is soundness.** Six distinct false-`Proven`-elides vectors were closed — cases where
a proof deleted a runtime check that would have failed (five of them on the `calor run --verify`
path; the sixth reached agents through the MCP refine tool, see below). Enumerated so
the count is checkable rather than asserted: (1) **D4** non-ordinal comparison modes; (2) **D4** bare
`StartsWith`/`EndsWith`/`IndexOf`, which use the *current culture* in .NET and so diverge with no
mode argument present to signal it; (3) **D3**, Z3 strings are null-free; (4) **D12**, Z3 counts UTF-8
**bytes** where .NET counts UTF-16 units; (5) **D14**, array and user-type sorts are total and non-null;
(6) the third `$length` mint site, which the D14 fix's own first cut missed.

**Which of the six were demonstrated, and which were argued.** (1), (2), (3), (4) and the array
half of (5) carry a recorded `calor run` versus `calor run --verify` pair — the check was observed to
disappear. Only the **user-type half of (5)** rests on inspection of the encoding rather than a run.
(6) **cannot** have such a pair: `VerifyRefinements` has no CLI flag, so that site is not reachable
from `calor run --verify` at all — it served a false `proven` to agents through the MCP refine tool,
which is how it was found.

This paragraph took three attempts. The first revision claimed a reproduction for all six, which was
wrong; the second withdrew it for (4) as well, which was also wrong — #876 records the D12 pair in
its own commit body (`§S (== (len result) INT:2)` over `§R STR:"é"`: throws under `calor run`, prints
under `--verify`). Both are withdrawn in favour of the line above. Recorded rather than quietly
fixed, because over-correcting a claim is the same defect as over-claiming it, and this release's
whole thesis is that asserting instead of demonstrating is what kept failing.

That is six closures, not six review rounds — several arrived in the same round, and two were found
*inside* the fix for an earlier one. The count of *vectors still unfound* is not knowable and is not
claimed to be zero.

What closed the class was a change of mechanism, not a better enumeration. Hand-enumeration was tried
at three levels — divergence rows, then Z3 sorts, then `$length` mint sites — and missed something at
every one. Proofs carried by a sort Z3 models as total are now **demoted to `Assumed`**, which never
elides, so the class closes by construction. See `docs/verification-modeled-forms.md` §4.1 and
divergences D3, D4, D12, D14.

**Known limitations shipping with this release**, all pre-existing rather than regressions:
`§MT` contract violations report the wrong function id (#879); a contract call expression carrying a
keyword argument crashes the translator instead of diagnosing it (#874); `str` is not yet enforced
non-nullable at the binder, which is what would let the string demotion be lifted (#875); and the
MSBuild task's IL-analysis inputs are still absent from its options hash, so a changed
referenced-assembly set against a warm cache can silently skip findings (#883, found in this
release's own review and disclosed rather than fixed).


### Added
- **WS-W2 — the five effect-soundness holes are closed (#842).** Effect enforcement previously had branches that resolved to *silently pure*, which is the fail-open direction. Now: invoking a delegate held in a value (parameter, `§B` binding, field) is an **error** (`Calor0418`) rather than an assumed-pure no-op, with `--permissive-effects` the only waiver; **effect variance is checked on both legs** — an override's `§E` must be a subset of its base's (`Calor0420`) and an implementation's a subset of its interface's (`Calor0421`), which is what makes charging declared effects at in-module virtual call sites dispatch-sound; **interop is `Assumed`, and propagates** (`Calor0419`) transitively over the reverse call graph rather than stopping at the boundary; the **mutator purge** (#785) removes `Add`/`Remove`/`Clear`/`Insert`/`Sort`/`CopyTo` and friends from the known-pure list, with untyped receivers now failing loud; and both `_ => EffectSet.Empty` catch-alls are replaced by exhaustive node-kind switches, so an AST node nobody taught the pass about becomes `Calor0419`, never silently pure. `--enforce-effects` now **defaults on** for `build` and `watch` (`--no-enforce-effects` opts out), ending the CLI/SDK split-brain where the SDK enforced and the CLI did not.
- **WS-W3 — the adoption surface (#841).** `calor import <package>` generates effect manifests from a real assembly in three tiers — IL-derived (`Confidence: inferred`), curated-manifest (reported, not re-emitted), and **unresolved, which is surfaced loudly (`Calor1351`) and excluded from the manifest** rather than defaulted to pure; classification is per (type, member, kind) group and any unresolved overload poisons the member. Nothing is ever emitted as `verified`. Validated live on Serilog (207 members: 76/115/16) and MediatR (38: 19/4/15). Contract synthesis writes a `<pkg>.calor-contracts.json` sidecar in which **every entry carries `provenance: "assumed"`** (`Calor1353`); the consumption path is deliberately not built, so these are annotation-only and cannot launder into a proof. `calor review-packet` leads with the **unproven remainder** — seven-status counts, assumption lists, vacuity flags, counterexamples, and per-module interop/waiver fractions with waiver disclosure on the first line (`Calor1357`) — plus caller-impact from the in-memory call graph (`--changed`/`--baseline-ref`). Ships with `docs/guides/adoption-playbook.md` and an **11-test eject suite** that compiles and *executes* the ejected C# to pin what survives leaving Calor (`§Q`/`§S` degrade to runtime guards; `Off` strips them; the #764 early-return refusal is pinned as a known gap).
- **WS-W4 — conversion honesty completed, and the real-scale benchmark venue built and then retired (#844/#846/#848/#849/#852/#853/#854/#856).** The remaining **silent** conversion substitutions became loud (#844), and namespace and local-function handling moved SILENT → LOUD with preprocessor/record behavior pinned (#846). On top of that, a real-scale task venue: mutate-then-convert task generation with an eligibility predicate (#849), a vendored corpus pinned at fixed SHAs with the first fidelity measurement against it (#848), a gold-standard bug-fix-revert task source (#852), an expressible-defect stratum — verification-addressable mutation operators plus an addressability gate, so the tasks a verification claim is tested on are ones verification could in principle catch (#856), an epoch runner verified end-to-end against a null agent (#853), and a dry-run record with spend authorisation (#854). **The venue was then retired by Call S before it adjudicated anything**, on the finding that its "Calor arm" bundles contained no Calor — recorded in `docs/plans/` rather than quietly dropped, because a venue that was built, paid for, and found unfit is a result.
- **W1 Slice 4 — `Calor.Sdk` is a functional, published, consumer-tested package (#787/#790/#788 subsets; PP-A1 items 1/2/4):**
  - **Self-contained MSBuild SDK package**: the nupkg carries `Sdk/` props+targets, the full `Calor.Tasks` dependency closure (compiler, `Calor.Runtime`, `Microsoft.Z3`) under `tasks/net10.0/`, and per-RID Z3 natives — staged via `dotnet publish` and packed through the supported `TargetsForTfmSpecificContentInPackage` extension point with hard `<Error>` assertions for every required file (the old late-glob target could silently pack nothing). `Calor.Runtime` is bundled and injected as a `Reference` by the targets (version lockstep by construction; `CalorSdkImportRuntime=false` opts out). `Z3ContextFactory` now probes assembly-relative `runtimes/<rid>/native` and registers its resolver on the task assembly's own AssemblyLoadContext — verification genuinely runs inside consumer MSBuild builds.
  - **M-A1 is enforced in CI**: `tests/SdkConsumer/` + `.github/scripts/test-sdk-package.sh` pack the SDK into a local source-mapped feed, then restore/build/test a consumer that uses `<Sdk Name="Calor.Sdk"/>` with no project references — asserting build+tests green AND that the verify gate produced a real `Calor0712` counterexample in the task context (`Calor0710` absent). Package-content inspection via `.github/scripts/inspect-sdk-nupkg.sh`.
  - **Publishing is gated and atomic**: `publish-nuget.yml` packs CLI + SDK together (one push of both, same version) and now `needs:` the full test suite and the M-A1 consumer check — release events can no longer publish unconditionally.
  - **id-validation unmasked** (#790): the `|| true`/`|| echo` swallows are gone and the check covers `samples/` per-directory (which the masked check had let rot — real ID-prefix violations and cross-file duplicate IDs in `samples/Generics` and `samples/Verification` are fixed in this change); `docs/` is deliberately not ids-checked (it has no standalone `.calr` files — its fenced calor blocks are drift-checked by `self-check docs`). `Calor.Performance.Tests` (wall-clock-threshold tests) now runs on a nightly workflow with the flakiness rationale recorded.
  - **Incremental-build honesty** (#788 subset): the MSBuild task's options hash now covers all four of the diagnostics-affecting params it previously omitted (`enforceEffects|verify|ilAnalysis|experimental`, with experimental flags canonicalized); `typeCheck` was added to the token later in this same release, making five. **Recorded residual, found in release review:** the IL-analysis inputs — `ReferencedAssemblies`, `RuntimeDirectory`, `NuGetPackageRoot`, `DepsFilePath` — feed effect diagnostics and are still hashed nowhere, so changing the referenced-assembly set against a warm cache with `EnableILAnalysis=true` can still silently skip findings. Same shape as #788, not closed by it (#883), and IL-analysis/cross-module-enforcement init failures now **fail the build** instead of warn-and-continue.

### Changed
- **String comparison with an explicit non-ordinal mode, and bare `StartsWith`/`EndsWith`/`IndexOf`, are refused rather than modeled (D4; #872).** Z3's string theory is byte/code-point ordinal only. A contract written `(== (§C{s.Equals} §A t §A StringComparison.OrdinalIgnoreCase §/C) BOOL:true)` was translated as ordinal equality and could be **proven** while the runtime call returned the opposite — and `Proven && !IsVacuous` then deleted the check. The second half is the one hand-enumeration missed: the *mode-less* overloads are not ordinal either. `string.StartsWith(string)`, `EndsWith(string)` and `IndexOf(string)` use the **current culture** by default in .NET, so the whitelist's ordinal model diverged from the runtime with no mode argument present to signal it. All such forms now report `unsupported`, which keeps the runtime check. **Cache format 1.8 → 1.10** (two bumps, one per half) — 1.8/1.9 entries hold `Proven` for exactly these shapes.
- **Every proof whose translation touched Z3's string sort is demoted to `Assumed` (D3 + D12; #876).** Two divergences, one mechanism. **D3:** Z3's `String` sort is total — there is no null string in the theory — so `(> (§C{s.Length}…) INT:0)` is provable while the .NET call throws `NullReferenceException` on a null receiver. **D12:** Z3 models strings as **UTF-8 byte** sequences while .NET's `string.Length` counts UTF-16 code units, so the two disagree on every non-ASCII character — `"é"` is one UTF-16 unit and two UTF-8 bytes, and `"😀"` is two units and four bytes. (An earlier revision of this entry said "code points" and "off by one per surrogate pair"; under a code-point model the recorded `"é"` reproduction would not exist at all. The compiler's own assumption string, `string-model — … UTF-8-byte-counted`, is the authority.) Refusal was rejected here because it would have withdrawn *reporting* on the whole string surface; demotion withdraws only **elision**, which is the sound half — `Assumed` proofs are still computed, still reported, and still never elide (the D8 precedent). Lifting this requires `str` to be non-nullable at the binder, tracked as #875. **Cache format 1.10 → 1.12.**
- **Postcondition elision is withdrawn for any signature naming an array or a non-primitive type (D14).** Z3's array and user-type sorts are **total and non-null**; .NET's `T[]` and class types are nullable references. `<name>$length` is minted as an unconstrained `u32`, so `a.Length >= 0` is a solver tautology while the same expression throws at runtime on a null array — a false `Proven`, and `Proven && !IsVacuous` deletes the runtime check. Reproduced end-to-end: `calor run` crashed with a `NullReferenceException` where `calor run --verify` printed and exited 0. Such proofs are now **`Assumed`**, which never elides, on both the postcondition and `§PROOF` channels.
  - **What this costs, stated plainly:** the trigger fires when the sort is *minted*, and parameters are declared before any contract is translated — so a function taking `[i32]` or a class loses postcondition elision **even when its postcondition names only `result`**. Only signatures made entirely of modeled primitives still elide. Coarse on purpose: being wrong in the narrow direction deletes a runtime check; being wrong in the broad direction costs an optimization. Contract *proving* and reporting are unchanged.
  - **Cache format 1.12 → 1.13**, which invalidates every persisted verification entry: 1.12 entries hold `Proven` for array-carried proofs, exactly the verdict that elides.
  - This is vector (5) in the headline's enumeration, and the fifth of one class — *a sort Z3 models as total where the .NET value can be null* — after D4 (string comparison modes) and D3/D12 (null-free, byte-counted strings). The class, not its members, is the thing; enumerating members by hand is what repeatedly failed.

- **`EnableTypeChecking` is now default-ON, and the type checker no longer rejects valid programs (#761; PP-A1 item 9).** **Two opt-outs**, because a default flip that can reject a previously-compiling program needs one on every entry point: `--no-type-check` on `build`, and **`CALOR_NO_TYPE_CHECK=1`** everywhere else — `run`, `test`, `watch`, `verify`, the MCP tools, and the MSBuild task inside the published SDK, where a consumer otherwise has no lever at all. The variable is read at the `CompilationOptions` default rather than threaded through the ~30 sites that construct it; the first cut threaded a flag and reached `build` only. `CalorTypeCheck=false` is the MSBuild spelling. The flip was blocked by defects in the checker itself, not by the flip: turning it on produced **92 test failures**, every one of them a working program the checker refused. They were already live for agents — `calor_check` and `calor_refine` set `EnableTypeChecking = true`, so the MCP primer's own `§M{m3:Files}` module, two shipped benchmarks and the syntax exemplar were all being rejected. Fixed:
  - **Types the checker did not know.** `char` (37 of the 92), `object`, `decimal`, and every sized/unsigned integer (`i8 i16 i64 u8 u16 u32 u64`, `f32`) — all documented in the syntax reference, all reported as `Unknown type`. Arrays (`T[]` and `[T]`) did not resolve either. Sized types reach the checker **expanded** (`INT[bits=64][signed=true]`), so they are now normalized through the same surface-spelling helper the diagnostics use — and they carry the width the user wrote, so a mismatch on an `i64` binding says `i64` rather than the collapsed `i32`.
  - **String concatenation.** `(+ str str)` is concatenation in Calor and in the emitted C# — `calor run` prints `helloworld` — and the checker called it `Arithmetic operators require numeric operands`.
  - **Static member access.** `Math.PI`, `int.MaxValue`, `StringComparison.Ordinal`, `System.Environment.NewLine` were reported as *undefined variables*. A bare unknown identifier is still an error.
  - **Cascades from the checker's own blind spots.** The checker models no BCL surface, so an external call yields an unknown type; it then reported `IF condition must be bool, got <error>`, `Logical operators require bool operands` and `Cannot access field on non-record type <error>` at every downstream use. Unknown types now suppress the downstream complaint instead of becoming the user's error — the rule the arithmetic check already followed.
  - **A duplicated diagnostic.** `§B{x}` with no type and no initializer is `BindValidationPass`'s Calor0250, which carries a quickfix; the checker reported the same condition as the vaguer Calor0202, so enabling it *replaced* a precise diagnostic with a worse one.
  - Unresolved type names now become an explicit **external type** and are reported as a **warning**: the checker cannot distinguish "a .NET type I do not model" from "a typo", so an error rejects working interop — but silence is also wrong, because on `calor_check`, `calor_refine` and `calor -i/-o` **nothing else compiles the generated C#**, so a misspelt type would vanish rather than resurface as CS0246. (An earlier revision of this entry claimed it would resurface; that was true only for `calor run`/`test`.) Refinement base types are exempt and still error, because that is a Calor-level construct the refinement machinery has to reason about.
  - **Collection and pattern checks no longer hard-error on shapes the checker does not model.** `SETIDX` learned about arrays, `PUSH`/`PUT`/`REM`/`CLR`/`INS`/`EACHKV` stay silent on unmodeled receivers, and `CheckPattern` — which modelled 7 of the AST's 19 pattern kinds and reported "Unsupported pattern type" for the rest — now binds `§VAR`, type patterns and composites and stays silent otherwise. The match **expression** path never entered a scope or bound patterns at all, so `§K §VAR{d} §WHEN (> d 0) → d` reported `Undefined variable 'd'`. Found by adversarial review: without these, the flip broke two agent-native benchmark **gold references**, one dropping from 53 proven contracts to zero with a non-zero exit.
  - `decimal` mixed with a floating type in arithmetic is now rejected — C# has no implicit conversion in either direction, so the checker was accepting programs whose emitted C# fails with CS0019.
  - Two test fixtures were themselves invalid and had only ever compiled because checking was off: one referenced an undeclared loop variable, and one wrote `§L{l1:i:INT:0:INT:10:INT:1}` — the loop header is colon-delimited, so `INT` parsed as the `from` expression and the misparse was silent until the checker named it.

- **W1 Slice 5a — round-trip harness fidelity core (#776 subset; the conversion-fidelity substrate for the real-scale benchmark's gate):** the harness now reports **per-project conversion coverage with reverted files in the denominator** (build-recovery reverts were relabeled as compile errors and silently dropped from coverage), populates `Gaps`/`InteropBlocks` **from the Slice-3 loss ledger** (they were declared-but-never-assigned false zeros), aggregates **every** TRX file with definition-joined test identity (assembly::executor::class::displayName — newest-file-only parsing could conflate duplicate display names across assemblies), and separates the verdict into independent ConversionCoverage / BuildOutcome / TestOutcome dimensions in both JSON and markdown reports. Synthetic corpus: 100% native coverage, 52/52 tests both phases. 16 new pins. Slice 5b (threshold enforcement, inventory-shrink=fail, CI blocking flip, corpus vendoring) remains.
- **W1 Slice 6 — latency fixture re-baselined (both recorded triggers had fired: the #807 fix and the 0.10.0 version bump):** the D3.3 10k-line fixture regenerates (seed 6089) with post-#807 **result-referencing contract forms** (§B-chain result-cap/floor/max shapes alongside the legacy chains); 1,988/1,988 contracts verify plain `proven` before AND after the 200-edit script; content criteria re-matched against the grown 61-file corpus; the W1 Slice 1 refusal classes are the recorded new form boundary. **ml1-003** re-baselines M-L1 on the new lineage: MCP edit→envelope **P50 2 ms / P99 7.42 ms** vs the frozen PP-L1 thresholds (300/1000 ms) — PASS, same band as ml1-002 (old lineage, which remains that lineage's record). Honest limitation: the watch (reported-only) surface is unmeasurable until #764 retires `Calor1001` (diagnostic-bearing files are excluded from the incremental cache — pre-existing interaction); PP-L1 adjudicates on MCP only.

- **W1 Slice 3 — conversion honesty: shared compile validation, no silent loss, no silent substitution (#770/#771/#773/#774/#761 gate subsets):**
  - **One Roslyn compilation validator** (`GeneratedCSharpCompiler`, full reference resolution + split syntax-vs-compilation diagnostics) now backs the self-check exemplar checker, the round-trip tests, and the conversion scorecard — round-trip tests **assert** compilation (previously computed-and-logged), and the scorecard's `FullyConverted` requires full compilation, not parse. **Scorecard re-baselined 96 → 93 honest** (exact, per-fixture zero-regression via regenerated `baseline.json`; the 7 partials are real, named emitter defects: local functions, indexers, generic methods/constraints, two pattern shapes, deconstruction). The CI regression script was also reading a renamed field and silently fail-open — fixed.
  - **`calor convert` no longer lies**: validation runs before the write; "✓ Conversion successful" prints only with zero losses; otherwise a grouped `file:line` loss summary (JSON envelope gains `lossCount`/`losses[]`). Preprocessor stripping reports each directive with dropped-branch line counts.
  - **`§ERR "TODO"` poison is gone**: an unconvertible *expression* now escalates its containing member to a `§CSHARP` interop block (counted, located) in every mode — including PP-region members that previously vanished silently.
  - **Silent substitutions eliminated (#774)**: char literals convert natively (`char-lit`, round-trips and compiles); unknown binary operators no longer become `+`; relational/binary patterns no longer become `==`/`and`; unknown switch labels no longer broaden to match-everything; **compound assignments beyond `+ - * / %` no longer mis-emit** (`>>>=` was silently becoming `x = 1`, and expression-context `x += 1` became `x = 1` — real corruption, now exhaustively mapped incl. `&= |= ^= <<= >>= ??=`, with escalation for unmapped kinds).
  - **Registry honesty and executable capability contract (#773)**: a centralized Roslyn syntax classifier now forces containment-first, counted C# interop for async-resource lifetime constructs, using declarations, scoped parameters, ref structs, file-local types, non-representable interface contracts, and delegate surfaces that Calor cannot model exactly. `interface` is Partial, `scoped-parameter` and `using-declaration` are NotSupported, records remain preserved rather than lowered to broken classes, and conformance tests bind these claims to lossless round trips. `PreserveDocumentationComments` now states the actual XML-doc-only behavior while `PreserveComments` remains a compatibility alias.
  - Hardened by the PR's adversarial review (all repro-verified): `§RAW` statement preservation now actually emits (the emitter discarded raw statements while the ledger claimed them preserved) with half-conversion leaks cleared at both statement and member escalation sites; escalation inside an active `#if` no longer captures the dangling directive (CS1027); disabled-branch preprocessor members preserve verbatim with a counted loss instead of vanishing as `_PP_Fallback_` stubs; emitter-side `§CS{…}` fallbacks reconcile into the ledger so the success line can never coexist with raw C# in the output; lone-surrogate char literals escalate instead of silently becoming U+FFFD.
  - Ride-along real-bug fixes exposed by the honest gate: block-emitting collections dropped return values (bare `§R`); discard-await emitted invalid `var _ = await Task.Delay(...)` (CS0815); generic delegates lost their type parameters (now interop). Known limitation recorded: `@object`-style keyword identifiers cannot round-trip yet (13-02 reclassified with a tracking note).

- **W1 Slice 2 trust surface (wedge plan v0.11, kickoff §4 Slice 2):**
  - **Telemetry is now OPT-IN and metadata-only (#792; hardened by the #834 review).** A default invocation sends nothing; telemetry activates only with `CALOR_TELEMETRY=1` (`--no-telemetry`/`CALOR_TELEMETRY_OPTOUT=1` still force it off). Payloads are stripped: diagnostic **codes** only (never message text, which can embed source fragments and paths); exception **type names** only (never messages or stack traces — `ExceptionTelemetry` replaced by a bare event); the **machine hostname the App Insights SDK auto-attaches is scrubbed** to the constant `calor-cli` (review M1); the **input/output content hashes** the determinism tracker sent are removed — they enable exact-file identification (review M2); `calor_help` queries send only their **length** and hit/miss, never the query text (review M2). Full audited inventory in the new `docs/telemetry.md`. Previously telemetry was default-on and sent raw diagnostics and full exceptions from every CLI run.
  - **The #793 release-policy containment is now code, not prose (kickoff T3; completed by review C1):** `calor format --write` **and `calor lint --fix`** — both ride the same formatter write path — refuse with **`Calor1346`** unless `--experimental` (or `CALOR_EXPERIMENTAL_FORMAT_WRITE=1`) acknowledges the known defects (#760: ID-rewriting regexes, comment loss); read-only modes (`--check`/`--diff`/stdout/lint reporting) stay available; the LSP **formatting** and **rename** handlers register only under `CALOR_LSP_EXPERIMENTAL=1` (#760/#765) — all read-only handlers unchanged.
  - **Z3 binaries are checksum-verified on every scripted fetch path (#789; widened by review M3).** `publish-nuget.yml` previously fetched natives with a bare `curl -L` (no `-f`, no verification) — a 404 page could ship inside the nupkg as `libz3`. Now fail-closed against committed SHA-256 manifests at all four fetch sites: the publish workflow (repo `z3-binaries` assets, `.github/z3-binaries-4.15.7.sha256`), `download-z3.sh`, `download-z3.ps1` (the Windows dev path), and `build-z3.yml`'s prebuilt job — the job whose output *becomes* the `z3-binaries` release, so an unverified fetch there would have laundered a poisoned upstream archive into a "pinned" release (upstream manifest: `src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256`). Recorded residual: the ARM64-macOS from-source build clones the z3-4.15.7 **tag** (not a commit pin); manifests are trust-on-first-use, disclosed in each file.

### Fixed
- **A seventh false-`Proven`-elide vector, on the emitter side (D15) — found by review of this release, fixed before it shipped.** The runtime lowering of a bounded `forall` mined the antecedent for loop bounds and then emitted **only the consequent** as the predicate, discarding every antecedent conjunct that is not a bound on the loop variable — and, because bound extraction uses `??=`, every bound after the first in each direction. Z3 proved `∀i. (bounds ∧ G) → P(i)`; the emitter then checked `∀i ∈ [lo,hi). P(i)`, a strictly **stronger** proposition, and `Proven && !IsVacuous` deleted it. Reproduced on a postcondition that is a **tautology as written**: it threw under `calor run` and printed under `calor run --verify`.
  - **This one matters beyond its own fix.** The six vectors above all live in the Z3 *encoding*, and the mechanism this release adopted — demote any proof carried by a sort Z3 models as total — closes that class by construction. D15 mints no sort at all: it is a mismatch between what was proved and what was lowered. **No amount of re-auditing the encoding could have found it**, and the release's own headline mechanism is structurally blind to it. The claim this release can honestly make is that one class is closed by construction; it is not that the elide surface has been exhausted, and D15 is the evidence.
  - Fixed by emitting the whole implication as the loop predicate. Sound in both directions: inside the range the bound conjuncts hold, so it reduces to the consequent; for any value a too-wide range admits but the antecedent excludes, the implication is vacuously true — and `??=` can only ever widen. `§Q` preconditions were mis-lowered the same way (a pure false alarm, since preconditions never elide) and are fixed by the same change. `exists` shares the extraction logic but fails safe.
- **Every directly self-recursive function failed to compile under effect enforcement — fixed before release, found by review of the release itself.** `ProcessScc`'s fast path was commented "single-function SCCs **with no self-recursion**" and never checked for a self-edge. Tarjan reports a self-recursive function as a singleton SCC exactly as it reports a non-recursive one, so `§F{f:Fact}` calling `§C{Fact}` took that path with an empty member set; the recursive call then failed the SCC-membership test, found no computed entry (it was mid-computation), and fell through to unknown-call handling — `Calor0411 Unknown call target 'Fact': not an internal function`, on a function declared three lines above, then `Calor0410 uses effect 'Unknown:*'`, which **cannot be declared away**. It affected `build`, `run`, `test` and the MCP tools alike; the only escapes were the global `--no-enforce-effects` / `--permissive-effects`. Mutual recursion was unaffected, because an SCC of size ≥ 2 populates the set — which is why nothing caught it. Introduced by the WS-W2 effect rewrite in this same release: `v0.10.0` with `--enforce-effects` explicitly on compiles the same program. Measured over the 237-file tracked corpus: 8 files failed before the fix, 5 after — it repairs 3 and breaks none, and the 5 remaining are the intended flip (3 genuinely undeclared `cw`) and the documented chained-receiver residual (2).
- **`calor_refine` reported `success: true` on programs that did not compile.** Three branches returned an empty result set with `Success = true` and no `isError` when the obligation tracker was null — which is exactly the state after a failed compile. An agent reading the obvious field saw "clean, zero obligations, zero failed" about a program that was never analyzed. That is the false-clean-signal shape this release exists to close, on the tool through which its own sixth soundness vector reached agents.
- **The VSCode extension shipped without its runtime dependency and could not activate.** `.vscodeignore` excluded `node_modules/**` while `out/extension.js` requires `vscode-languageclient/node` unconditionally at module load. The package was 10 files / 54 KB; it is now 314 files / 488 KB with the dependency present. **Pre-existing** — the published `0.3.8` has the same defect — so this is a fix to a long-standing break rather than a regression. (Also recorded: the v0.10.0 marketplace publish never landed; latest published remains 0.3.8 across all six targets. A workflow firing is not evidence it succeeded.)
- **~365 Z3-backed verification tests were silently skipping in CI, and now cannot (#858).** The natives were seeded *after* MSBuild evaluation, so the Z3-gated suites resolved to "solver unavailable" and skipped rather than failed — which means **v0.10.0 was published with those tests not actually running**, and the gate that was reported green was green for the wrong reason. Natives are now seeded before evaluation, and a new `Assert no Z3-gated test silently skipped` step gates `publish-nuget.yml`: a skip is now a build failure rather than a quiet subtraction from the denominator. Recorded here rather than left in the commit log because it changes what the previous release's green CI meant.
- **W1 Slice 1 soundness batch (wedge plan v0.11, kickoff T1/T2 + D6–D10):** every known false-`Proven`-that-elides vector on the modeled-forms whitelist is closed, and the postcondition runtime-check lowering no longer silently skips or reorders checks. Verification cache format bumps to **1.8** (verdict semantics changed in both polarities — warm 1.7 caches could serve stale verdicts for all shapes below).
  - **`string.Replace` un-whitelisted (D9):** Z3 models first-occurrence replacement, .NET replaces all occurrences; contracts using it now report `unsupported` (runtime check kept) instead of proving through the divergence.
  - **Narrow-int arithmetic refused (D1):** arithmetic/shifts/negation where every operand is sub-32-bit report `unsupported` — C# promotes narrow integers to `int` while the solver would wrap at the narrow width (`§S (< (+ x y) 128)` over `i8` was provable while runtime 100+100=200 violated it). A 32-bit-or-wider operand rescues the pair (width normalization matches promotion); comparisons on narrow operands stay modeled.
  - **Mixed signed/unsigned semantics modeled correctly (D10; widths corrected by the PR's adversarial review, C1/C2):** genuinely-mixed operations model **C#'s binary numeric promotion** — a `uint` side (or anything with `i64`) promotes both to 64-bit signed (`-1 == 4294967295u` is now false, as at runtime), while a **narrow** unsigned side (`u8`/`u16`) with a signed side ≤ 32-bit promotes to **int and wraps at 32** (C# has no byte arithmetic). 64-bit unsigned mixed (`u64` vs signed) is refused — no common C# type. Companion typing fix, correctly scoped: a non-negative literal converts to a paired unsigned type only when that type is **32-bit or wider** (`u32 x - 1` is `u32`; `u8 x - 5` is `int` and can be negative) — the old signed-poisoning made `(x-1) < x` spuriously disprovable and several array-length pins were updated to the now-runtime-correct verdicts.
  - **Shift counts are masked like the runtime (D11, review C3):** C# masks shift counts by width−1 (`1 << 32` is `1`); the unmasked solver shift yielded 0 and could prove `(x << 32) == 0` and elide a failing check. The mask is modeled; shifts bypass binary numeric promotion (the left operand promotes individually); counts wider than 32 bits are refused.
  - **Contract-expression division carries divisor side conditions (D8; completed by review C4/C5):** `/` and `%` inside `§Q`/`§S` were totalized (`x/0 = -1`) — a proof could rely on a state where the runtime contract check itself throws. Divisors in unconditional positions now assert `≠ 0` **and, for signed division, `¬(dividend = MinValue ∧ divisor = −1)`** (that state throws `OverflowException` in C# in checked *and* unchecked contexts while `bvsdiv` wraps); a proof that needed the conditions reports **`assumed`** with the new canonical assumption `exceptional-paths:contract-division …`, unless the preconditions already entail every condition (a `§Q (> y 0)` guard and literal divisors ∉ {0, −1} stay plain `proven`; `§Q (!= y 0)` alone leaves the overflow residual → `assumed`); conditional-position divisors — short-circuit RHS, `?:` arms, **implication consequents, and quantifier bodies** (previously silently skipped by the collector) — report `unsupported`. Refutation models are now runtime-genuine. Two INT_MIN÷−1 pins that expected `proven` under "models unchecked hardware" were corrected: C# division overflow throws even unchecked, so those proofs are `assumed`. *Recorded residual:* `Z3ImplicationProver` (the `--weakening-check` CLI) still totalizes division.
  - **Unregistered user-type fields refused (D7):** a field the user-type registry doesn't know reported at a guessed `i32` width; now `unsupported`. (D6, arrays first seen at an access site, was adjudicated instead: the on-demand path is unreachable from the elision-relevant `§Q`/`§S` path — parameters and bound variables are declared with their true types, and contracts naming undeclared variables are rejected upstream — so the obligation surface keeps its historical semantics; recorded in the divergence table.)
  - **Postcondition lowering stopgap (#764, T2):** the emitter's `result`-substitution is now word-bounded (`resultCode` is no longer corrupted to `__result__Code`), and bodies with early, nested, or raw-C# returns — whose checks the direct-child-only lowering would silently skip or whose execution order it would change — are emitted **untransformed** with a new **`Calor1001` warning** ("postcondition runtime checks not emitted for this body shape"); a void function with `§S` and any `return` gets the same loud omission (its trailing checks were unreachable). All **five** lowering sites are gated — the operator-overload site (review M1) additionally never substituted `result` at all. The structural lowering that retires `Calor1001` remains #764.
  - **`ContractSimplificationPass` preserves all node fields (#781 preservation half):** the pass runs unconditionally on the main compile path and its reconstruction dropped module `EnumExtensions`, `InteropBlocks`, `RefinementTypes`, `IndexedTypes`, and `TypePreprocessorBlocks` — and interface `Properties`/`Indexers` — whenever any contract simplified. All fields now survive, pinned by tests.

## [0.10.0] - 2026-07-30

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7 categories, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (Calor)
  - ErrorDetection: 1.49x (Calor)
  - TokenEconomics: 1.42x (Calor)

### Added (release assembly)
- **`calor verify --weakening-check <declId> <frozen.calr> <final.calr>`** — mechanical contract-weakening verdict between two versions of a declaration (guarantees plan G5 / gates Annex A-1.3.1 two-leg rule): weakened iff the `§S` conjunction was relaxed OR the `§Q` conjunction was strengthened (the prover-appeasement move); emits JSON with `weakened`/`indeterminate`/`intactOrStrengthened` and all four implication directions. By-rule verdicts (renamed/removed declaration, changed signature, unparseable final, emptied contract set) need no solver.
- **`CalorVerify` MSBuild property** — `Verify="$(CalorVerify)"` on the `Calor.Tasks` compile task runs Z3 contract verification inside MSBuild builds; refutation warnings (`Calor0711`/`Calor0712`) surface as MSBuild warnings on successful compiles (previously the task dropped all non-error diagnostics on success), an armed-but-solverless gate warns loudly (`Calor0710`), the native Z3 library deploys to the Tasks output root (MSBuild task contexts get no deps.json native probing), and `Verify` participates in the incremental-build options hash so flipping it on over a warm cache recompiles.

### Fixed (release assembly)
- **Integer literals outside the signed 32-bit domain are refused by the contract translator instead of silently wrapped mod 2^32** — the wrap corrupted verdicts in both polarities (false refutations with nonsense counterexamples, inverted implication verdicts). Such contracts now report `unsupported`. Verification cache format 1.7 (pre-fix entries may hold wrapped-literal verdicts).


### Added
- **Verifier depth: immutable `§B` binding chains are now encodable (guarantees plan G4/D-G3.1).** Result-referencing postconditions over bodies composed of immutable bindings, guard-clause branching, and value returns now prove via SSA-style substitution — including all three W5-B probe contract shapes (`total = a + b; if (total > cap) return cap; return total ⊨ result <= cap`), the surface PP-G3's threshold depends on. Soundness edges pinned: mutable (`§B{~}`) bindings, use-before-declaration, and branch-local bindings leaking into fall-through all refuse (`unsupported`); rebinding shadows lexically; a *dividing* initializer keeps its side condition anchored at the binding site (an unused dividing initializer still yields `assumed`, and a branch-local one is conditionally-evaluated → `unsupported`); nesting depth and substitution size are bounded. Cache keys cover binding content (no format bump needed: `§B` bodies were previously `unsupported`, which is never cached). **Deferred with rationale:** the D1 (narrow-type promotion) and D2 (literal signedness) divergence fixes turn out not to be cheap — both change translator width semantics with cache and test-matrix impact — and stay recorded in the divergence table.
- **`assumed` goes live: *body*-division-carrying proofs are now conditional, not silently strengthened (guarantees plan D-G2.5 — the seven-status vocabulary's first `assumed` producer).** A result-referencing postcondition proof over a body containing `/` or `%` was reached under divisor-nonzero side conditions; it now reports **`assumed`** with the canonical assumption `exceptional-paths:division …` (envelope `assumptions` list, `Calor0720` info diagnostic) instead of plain `proven`. Behavior change: such proofs **no longer elide the runtime postcondition check** — `assumed` never elides, per the frozen D-G2.2 rule. Refutations under the same side conditions stay `refuted` (their models are genuine non-throwing executions). Corpus fixture `assumed-division.calr` pins the shape end-to-end (status, assumption, kept guard, wire payload). The verification cache format bumps to 1.5: a warm cache holding a pre-producer plain-`proven` verdict for a division body would otherwise keep eliding the guard the `assumed` verdict deliberately keeps. **Stated limit:** division inside *contract expressions* (`§Q`/`§S` themselves) is still totalized with no side conditions — a pre-existing divergence now recorded as D8 in `docs/verification-modeled-forms.md`, to be routed through the same producer in a later slice.

### Fixed
- **Cross-module calls now emit qualified C# and link under MSBuild/csc (#809; guarantees plan WS-G4/G3).** A bare-name call into another module (`§C{SaveSnapshot}` from `Catalog` into `Store`) passed the front-end but emitted verbatim — `CS0103` in every multi-module build, with no working alternative spelling. The multi-file driver now pre-parses all inputs into a bare-name → module map (unambiguous public functions only, mirroring cross-module effect resolution's skip-ambiguous rule so emission and enforcement agree), and the emitter qualifies matching call targets as `global::{Module}.{Module}Module.{Function}`. Self-module calls, dotted/generic targets, and ambiguous names stay bare. Scope: modules within one invocation (single-project) — both the CLI multi-input driver and the `Calor.Tasks` MSBuild task; cross-project references remain out. **Known limitation:** classes with a base type never qualify bare calls (inherited members are not enumerable at emission, and mis-qualifying one would silently run another module's code) — a genuinely-cross-module bare call from a derived class stays bare and fails loudly at csc, the pre-fix status quo. The build-state cache bumps to format 2.1 and additionally fingerprints the cross-module map — warm builds must re-emit when the module set changes, and pre-fix outputs with bare cross-module calls are invalidated once. Pinned end-to-end: the emitted pair compiles and links under Roslyn in CI, including a contracts-plus-effects cross-module chain (the first multi-module Guarantees surface).

### Changed
- **`unsupported` is now decided by a positive modeled-forms whitelist, not by accident (guarantees plan D-G2.3 core).** The contract verifier gates every `§Q`/`§S` expression through `ModeledForms.TryValidate` before translation: anything outside the enumerated surface reports `unsupported` with a reason naming the offending construct ("floating-point literal", "binary operator 'Power'", …). A whitelist-accepted form that nevertheless fails to translate is surfaced as **whitelist drift** in the outcome reason — a loud inconsistency instead of a silent fallback ("a blacklist by accident" ends here). The whitelist is machine-enumerated: `docs/verification-modeled-forms.md` gains a generated Appendix A that CI byte-checks against `ModeledForms.RenderWhitelist()` — the document is no longer the only enumeration. A drift-detector test proves every whitelisted binary/unary operator translates on representative typings (string/array/quantifier surfaces are covered by the existing verifier suites); "drift" is reserved for genuine whitelist↔translator inconsistencies — routine unmodeled operand typings report as such, with extended diagnoses (comparison operators, non-array bases). Contract translation is hardened against solver exceptions (a crashing sort mismatch is now `unsupported`, not a command error), and the verification cache format bumps to 1.6 (precautionary: after deriving the gate exactly from the translator's surface no known verdict tightening remains, but the bump guards any that review failed to construct). The gate covers the `§Q`/`§S` path; obligation/implication paths remain ungated (documented).
- **Bug-pattern checker translator refuses unsigned types instead of half-modeling them (guarantees plan D-G2.3, first installment).** The second translator applies signed operators throughout; declaring `u8`–`u64`/`byte`–`ulong` variables produced checker verdicts under wrong semantics. Unsigned declarations now route to the checker's no-verdict path — which also means **previously-correct unsigned findings disappear by default** (e.g. `Calor0920` on an unguarded `u32` divisor: zero-ness is signedness-independent, but the old signed modeling could equally *suppress* true warnings via wrong path conditions, so honest no-verdict wins). The refusal covers every checker backed by this translator (division-by-zero, overflow, index-out-of-bounds); literal-zero divisors still error via the pre-solver path. `docs/verification-modeled-forms.md` §5 updated.

### Breaking
- **Envelope schema 2.0: the verification status vocabulary grows from five to seven (guarantees plan D-G2.1/D-G2.2/D-G2.4).** New statuses: **`assumed`** — the obligation holds *conditionally* on a named assumption set the solver did not discharge (payload gains a sorted `assumptions` list; assumed never aggregates into proven, never elides runtime checks, and maps to no legacy Proven-equivalent anywhere downstream) — and **`unavailable`** — no solver was present (Z3 missing/disabled), split from `unknown` ("no solver" and "solver gave up" are different facts with different remedies). The `verification` payload also gains optional `vacuous: true` on vacuous proofs. **Migration:** consumers switching exhaustively on the five 1.x statuses must add the `assumed` and `unavailable` arms; a consumer that treats unrecognized statuses as "inconclusive, runtime check kept" remains behaviorally correct. Major bump per the schema's own rule that the status vocabulary is closed. New diagnostics: `Calor0720` (contract holds on assumptions), `Calor0721` (verification unavailable); solver-unavailable outcomes previously surfaced as `unknown`.

### Fixed
- **Verifier: five soundness/honesty holes in the new body→result binding closed (adversarial review of #818).** (1) A parameter named `result` aliased the postcondition result variable into one solver constant — a contradictory binding (`result == result + 1`) made the whole query UNSAT and minted a **false Proven that deleted the runtime check**; the shape is now `unsupported` with a rename hint. (2) Dotted references (`result.Value`) were invisible to the result-reference walker, silently reviving #807's fabricated refutations for user-typed results. (3) `§LEN result` was likewise invisible, and array-returning bodies bound `result == xs` without linking the synthetic length variables — array-result obligations are now honestly `unsupported` until length linkage is modeled. (4) The translator auto-declares unknown *array* references as fresh free variables; body encoding now validates every referenced base identifier against the declared parameters first. (5) Z3 totalizes `x/0`, so division-carrying bodies could refute with a divisor-zero model that runtime never produces (it throws before the postcondition evaluates) — divisor-nonzero side conditions are now conjoined for divisors evaluated on every normal-return path; a divisor in a *conditionally-evaluated* position (branch bodies, elseif conditions, statements after branching, short-circuit `&&`/`||` right operands, conditional-expression arms) makes the obligation `unsupported` instead — a bare global constraint there would exclude violating inputs on the other paths (found by re-review; a path-guarded encoding is planned with D-G2.5). Division refutations are runtime-genuine and division proofs are sound under `§S`'s normal-return semantics. **(6) `%` was translated as Z3's `bvsmod` (result takes the divisor's sign) but C#'s `%` is remainder (dividend's sign, `bvsrem`)** — under result binding this minted a false Proven that deleted the check (`a % -3` "proving" `result <= 0` while `M(7)` returns `+1`) and fabricated modulo counterexamples; both the contract translator and the bug-pattern checker translator now use `bvsrem`, and the verification cache format bumps to 1.4 so warm caches holding pre-fix `bvsmod` verdicts are evicted (a stale Proven would otherwise keep eliding the guard). Also: the vacuity pre-check now runs before body encoding (a vacuous `§Q` set wins over an unencodable body, keeping `Calor0719` visible), and the encoder memoizes fall-through continuations (exponential re-encoding on nested guard-clause chains).
- **Verifier soundness: result-referencing postconditions are now checked against the function body (#807; guarantees plan D-G1.1).** Previously the obligation left `result` completely unconstrained, so any postcondition mentioning `result` was "refuted" with a fabricated counterexample (`Identity` with `§S (== result x)` reported violated at `x=0, result=-1`). The verifier now asserts `result == encode(body)` for bodies inside the encodable surface — value-carrying `§R` returns composed with `§IF`/`§EI`/`§EL` branching (including guard-clause fall-through) over modeled expressions. Bodies outside that surface make the obligation **`unsupported`** (runtime check kept), never refuted against a free `result`. Verify verdicts change on real code: previously-spurious refutations now prove, and *genuine* refutations (e.g. two's-complement overflow: `x*x` wrapping negative) surface with real models. The verification cache keys result-referencing postconditions on the body (cache format 1.3; old entries invalidated).
- **Emitter soundness: precondition guards are never elided on verification results (#755; D-G1.2).** The verifier's precondition "Proven" is a satisfiability result (∃ an input meeting it), not validity (∀ inputs meet it), but the emitter treated it as license to delete the runtime guard — `Half(-5)` with `§Q (> x 0)` sailed through unchecked. Precondition guards are now always emitted (in debug/release contract modes); only postcondition proofs — genuine ∀-proofs via UNSAT-on-negation — elide checks.
- **Contract-level vacuity is detected and loud (D-G1.3, `Calor0719`).** A jointly-unsatisfiable precondition set (e.g. `§Q (> x 10)` + `§Q (< x 5)`) previously made every postcondition silently "Proven" — and elided its runtime checks — because no valid call exists. The verifier now runs a precondition-set SAT pre-check: such postconditions report Proven with a **vacuous** flag, raise warning `Calor0719`, and keep their runtime checks. The flag survives the verification cache.
- **`samples/Verification/proven-contracts.calr` repaired — the flagship "proven" sample was itself unsound.** Four of its contracts were genuinely violable under two's-complement arithmetic (`Square(46341)` wraps negative; `AbsoluteValue(int.MinValue)` is the classic `Math.Abs` violation; `AddPositive(int.MaxValue, 1)` wraps): the old verifier masked this behind #807's fabricated counterexamples. The sample now carries overflow-excluding `§Q` bounds and proves 14/14; the unbounded shapes are pinned in the outcome corpus as honest overflow refutations (`refuted-overflow.calr`).
- **`calor verify` text output labels non-refutation detail as `Note:` instead of `Counterexample:`.** Unsupported diagnoses and vacuity notes are reasons, not counterexamples; only refutations carry the `Counterexample:` label.

## [0.9.0] - 2026-07-29

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads, 32.0%)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (Calor)
  - ErrorDetection: 1.49x (Calor, large effect d≈1.2)
  - TokenEconomics: 1.42x (Calor)
  - RefactoringStability: 1.38x (Calor, large effect d≈7.1)
  - EditPrecision: 1.36x (Calor, large effect d≈4.9)
  - InformationDensity: 0.98x (C#, medium effect)
- **Benchmarks Evaluated**: 217 (Calor compiles 217/217, C# 216/217)

### Measured (the Loop program, v0.9's centerpiece)
- **PP-L5 HIT — the WS2+WS3 agent loop tooling reduces convergence cost ~35%.** One simultaneous A/B comparison epoch (`m5-compare-001`, 130 runs, pinned model): median paired tokens-to-green ratio 0.6465 (arm B = MCP transactional writes + warm sessions vs arm A = WS1-only baseline), one-sided 95% CI excluding zero effect, all 9 warm pairs improved. PP-L6 (science-integrity guard) passed; PP-L1 (warm feedback P50 2 ms / P99 9 ms edit→envelope) published under Added below.
- **PP-W1 HIT — enforcement catches seeded defects the C# toolkit misses.** Injected-defect probe epoch (`ws5-probe-001`, 9 defect pairs × both arms): Calor 9/9 vs C# 4/9 (Δ+5), concentrated in the runtime-contract channel (`§Q`/`§S` guards) plus effect build-blocks. Full records under `bench/phase0-agent-native/epochs/`.

### Added
- **Warm-feedback latency measured and published (loop plan WS3/M4, PP-L1: PASS).** On the pinned 10k-line multi-module latency fixture (`bench/phase0-agent-native/latency/`, 106 files, 230 committed timed edits), the MCP transactional write path (`calor_file_write`, warm session) measures **P50 2 ms / P99 9 ms** edit→envelope — against PP-L1 thresholds of 300 ms / 1 s. `calor watch` incremental rebuilds on the same fixture measure P50 27 ms / P99 32 ms (excluding the configured 200 ms debounce, recorded alongside); cold session open and cold watch compile of all 106 files are ~110 ms and ~390 ms. Full run record: `bench/phase0-agent-native/latency/ml1-002/` (adjudicating, run on merged main; `ml1-001/` is the agreeing pre-merge record). Instrumentation shipped with this release: `mcp-write/2` telemetry records carry `latencyMs` with a refresh/check/apply phase breakdown, and `calor watch` journals `watch-rebuild/1` records to `CALOR_WATCH_REBUILD_LOG` and prints per-rebuild wall time.

### Changed
- **Final envelope sweep: remaining MCP tools emit envelope schema v1.1 diagnostic entries; the `calor migrate --report file.json` report is envelope-wrapped (loop plan D1.3 — M-E1 reaches 100%).** Every remaining source-anchored diagnostic in MCP tool outputs moved from flat strings/DTOs to the shared `EnvelopeDiagnostic` entry shape (`{code, message, severity, location, declarationId?, suggestion?}`); non-diagnostic payload fields are unchanged. Per surface: `calor_convert` — conversion issues (`{severity,message,line,column,suggestion}` DTOs) became `Calor1343` entries with the feature name prefixed (the same mapping `calor convert --format json` uses); validate mode's `diagnostics[]` message strings became real parse/compile diagnostics (with `declarationId`), and roundtrip mode's `conversionErrors[]`/`compilationErrors[]` strings became entries. `calor_batch` — convert-mode per-file `issues[]` became `Calor1343` entries and compile-mode per-file `errors[]` strings (`"[Code] Ln: msg"`) became the real compiler diagnostics as entries (with `declarationId`); `errorCategories` and summary fields are kept. `calor_migrate` — per-file `errors[]`/`warnings[]` strings became entries across all phases (assess blockers as message-level `Calor1343`, convert issues as `Calor1343`, compile results as real compiler diagnostics with `declarationId`); `summary.errorCategories` is now keyed by diagnostic code, and the fix phase reports a numeric `fixesApplied` field instead of a pseudo-warning string. `calor_navigate` / `calor_structure` — parse `errors[]` string lists became entries built from the parser's `DiagnosticBag`. `calor_format` — format-action `errors[]` strings became the real parser diagnostics as entries, and ids-action `issues[]` (own `{type,line,kind,name,id,message}` shape) became the real `Calor0800`-band diagnostics with `declarationId` (mirroring `calor ids check --format json`); summary counts (`totalIds`, `issueCount`) are kept. `calor_fix` was audited: its payload reports applied fixes, not diagnostics — no shape change. **`calor migrate --report <file>.json`** now writes `{version, command: "migrate", diagnostics, summary, data}` with the pre-existing report shape unchanged under `data`; per-file conversion issues surface as `Calor1343` envelope diagnostics. The `.md` report and text stdout are unchanged.

### Fixed
- **`calor migrate` exit code now propagates (same defect class as the #754 review item).** The handler parked its exit code on `Environment.ExitCode`, which `Program.Main`'s `InvokeAsync` return value stomps — a failed migration exited 0. It now returns through the invocation context (the `format`/`ids` pattern): 1 on missing path, failed files, or unhandled errors.
- **MCP `calor_check`: `commonMistake` moved off `diagnostics[]` (migration note, review of #754 item 4).** Diagnostic entries in MCP tool outputs are now pure envelope-schema objects; `calor_check`'s per-diagnostic `commonMistake` field moved to a sibling `hints[]` array (`{line, column, code, commonMistake{...}}`), emitted for diagnostics without a compiler suggestion. Agents reading `diagnostics[].commonMistake` must switch to joining `hints[]` on `(line, column, code)`. This is a field relocation shipped under the 1.1 minor bump as part of the envelope unification; flagged here per the schema's own migration-note rule.
- **Counterexample rendering now filters internal solver variables everywhere** *(landed in #754; listed here because [Unreleased] is cumulative)*. The three duplicated model-extraction implementations were unified into `Counterexample.FromModel`, which skips internal `$`/`__` variables for all producers (previously only obligation counterexamples filtered them). Contract and implication counterexamples no longer show internal solver variables they previously included.
- **`calor verify` now exits 1 on refuted contracts** *(landed in #754, review item 1; listed here because [Unreleased] is cumulative)*. Previously verify's exit code was effectively always 0: the handler parked its code on `Environment.ExitCode`, which `Program.Main`'s `InvokeAsync` return value stomped, so a refuted (disproven) contract — or even a missing input file — still exited 0 with the refutation buried in `data.summary.refuted`. Verify now exits **1** when any file is missing, any compile error occurs, or any contract is refuted, and **0** when all contracts are proven or merely inconclusive (unknown/timeout/unsupported — runtime checks are kept, so inconclusive is not failure). Applies to both text and JSON modes.

### Fixed
- **CLI exit codes now propagate on error paths (review of #754 item 1).** `verify`, `convert`, `coverage`, `benchmark`, `effects resolve`, `effects validate`, and `effects suggest` parked their error exit codes on `Environment.ExitCode`, which `Program.Main`'s `InvokeAsync` return value overwrote — their error paths actually exited 0. All now return through the invocation context (the `format`/`ids` pattern), keeping each command's documented code values (verify/convert/coverage/benchmark use 1; assess/fix/effects-usage errors keep 2).
- **JSON mode always emits exactly one envelope document, including error paths.** `benchmark`, `assess`, `fix`, `effects resolve`, and `effects suggest` returned with empty stdout (or, for benchmark, mixed status text into stdout) on error paths in JSON mode, violating the envelope schema contract ("stdout carries exactly one document, always"). Each now emits a schema-v1.1 envelope with a CLI-band diagnostic (`Calor1310` missing input, `Calor1311` usage error, `Calor1312` internal error) before exiting; `coverage`'s existing error envelope now carries the CLI-band diagnostic too. Human-readable errors keep going to stderr; text-mode output is unchanged.

## [0.8.0] - 2026-07-23

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads, 32.0%)
- **Categories**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension (StructuralClarity): 1.84x (Calor)
  - ErrorDetection: 1.49x (Calor, large effect)
  - RefactoringStability: 1.38x (Calor, large effect d≈7.1)
  - EditPrecision: 1.36x (Calor, large effect d≈4.9)
  - InformationDensity: 0.98x (C#)
- **Benchmarks Evaluated**: 217 (Calor compiles 217/217, C# 216/217)

### Breaking
- **New hard-error diagnostics reject programs that previously compiled (`Calor0254`–`0258`).** Several exit-0-then-broken-`dotnet build` holes in the binding/rebind/shadowing family are now rejected at `calor -i`: array→concrete-collection (`Calor0254`), enclosing-scope shadowing (`Calor0255`, CS0136), type-changing mutable rebind (`Calor0256`, CS0029/0266), foreach iteration-variable write (`Calor0257`, CS1656), and same-scope duplicate `§B` (`Calor0258`, CS0128). Programs that relied on the old silent-accept behavior (and then failed a downstream C# build) now fail earlier, with a clear diagnostic. See the per-issue entries below for the exact shapes and remedies.
- **Contract-verification result codes renumbered `Calor0700`–`0705` → `Calor0710`–`0715` (#702).** Any tooling that filters verification output on `Calor0700`–`Calor0705` must switch to `Calor0710`–`Calor0715`; `Calor0700`/`Calor0701` now unambiguously mean the semantics-version diagnostics. (Full detail under **Changed** below.)

### Added
- **Same-scope duplicate-binding error + converter reassignment fix (#731).** Two `§B` declarations reusing a name in the *same* scope — `§B{x:i32} 1` then `§B{x:i32} 2` — now fail `calor -i` with `Calor0258` instead of exiting 0 and emitting `int x = 1; int x = 2;` (CS0128). Closing this required the converter half the issue flagged: the C#→Calor converter emitted an `arr = new int[]{…}` reassignment (and `List`/`Dictionary`/`HashSet` equivalents) as a fresh same-name creation *block* (`§ARR{arr:…}`), which round-tripped to a duplicate `int[] arr = …` declaration — itself an exit-0-then-CS0128 hole. The `CalorEmitter` now emits such a reassignment into a fresh temp followed by `§ASSIGN target temp`, so the target is reassigned rather than re-declared (covered end-to-end for 1-D/2-D arrays and `List`/`Dictionary`/`HashSet` — all round-trip Roslyn-clean). `Calor0258` is distinct from `Calor0255` (shadowing an *enclosing* scope, CS0136): a mutable rebind (`§B{~x}`) in the same scope is still a valid reassignment, sibling-block name reuse is still fine, and a local reusing a parameter name is still the CS0136 case. **Known limitation:** the converter also flattens standalone `{ }` statement blocks (dropping their braces), so valid C# with two sibling blocks that each declare the same name now surfaces as a clear `Calor0258` at `calor -i` rather than the previous *silent* downstream CS0128 — the root cause is the converter's block-scope fidelity, tracked in #751; the `Calor0258` message names this converter-origin case explicitly. This closes the hand-authored and array/collection-reassignment lanes of the rebind/shadowing family; the bare-block converter lane remains open in #751.
- **Surface-spelled types in every diagnostic + a leak guard (#741).** Diagnostics that echo a type now always print the compact surface spelling agents write (`i32`, `str`, `bool`, `i64`, `Option<str>`) — never the internal/expanded form (`INT`, `STRING`, `BOOL`, `INT[bits=64][signed=true]`, `Option<INT>`), which is not valid annotation syntax and would teach an agent a mistake that produces a new error. The leak surface was the `TypeChecker`, rooted in `CalorType.Name` rendering the internal spelling: a new `CalorType.SurfaceName` property (primitives map `INT`→`i32` etc.; `Option`/`Result`/`Function`/`Refined`/`Generic`/`TypeVariable` recurse) now backs every type-echoing message, the hardcoded `must be BOOL` / `require BOOL` became `bool`, and the "unknown type" message routes the echoed name through `ToSurfaceSpelling` so a **sized numeric type** (`i64`, `f32`) — which reaches the checker as its expanded `INT[bits=64]…` form — surfaces correctly instead of leaking `INT`/`[bits=`. Everywhere else already routed through `AttributeHelper.ToSurfaceSpelling` (#739) or used the user's authored surface text. Backed by `DiagnosticSurfaceSpellingTests`, the durable guard the #739 review asked for — two layers, both with type-checking on: a curated trigger set (including the sized-numeric family) and a scan of the whole in-repo corpus (237 files), each asserting no message contains an internal spelling token. Mutation-verified, and the corpus scan caught a hardcoded `BOOL` the trigger set missed. The legitimate typed-literal keyword forms (`INT:42`, "Invalid INT literal") are correctly not flagged. Scope note: the opt-in `TypeChecker` still does not *resolve* sized numeric widths (a valid `i64` binding gets a spurious "unknown type `i64`" — now correctly spelled but still spurious); modeling sized widths in the checker is a separate pre-existing gap, not a spelling issue.
- **`calor convert --passthrough` (#736).** The CLI C#→Calor converter now exposes the #717 §CSHARP post-validation fallback that the `calor_convert` MCP tool already had. With `--passthrough`, any top-level member whose emitted Calor would not parse is preserved verbatim as a `§CSHARP{…}§/CSHARP` interop block, so the written file always parses instead of being silently-broken Calor (the pre-#717 CLI behavior, which `--validate` could only warn about after the fact). The flag sets `ConversionOptions.PassthroughOnError`. Whenever the output contains §CSHARP interop blocks — from this fallback *or* the visitor's own wrapping of unsupported features — the CLI reports the count (`ⓘ N member(s) preserved as §CSHARP …`), attributing the subset the passthrough fallback rescued (`… (M via --passthrough fallback)`); if the output still can't be made to parse the conversion fails loudly (parse-fallback reason first) rather than shipping broken text. `--passthrough` is a no-op in the Calor→C# direction and says so. Default `calor convert` behavior is unchanged (fallback off).
- **Unannotated non-literal rebind type check + numeric-widening false-positive fix (#740).** `Calor0256` (#733) now also catches a mutable rebind whose value is *unannotated and non-literal* when the value's type can be inferred — a reference to a typed local/parameter (`§B{~x:i32} 0` then `§B{~x} s` where `s: str`) or a call with a known return type (a user function, or a curated BCL method like `File.ReadAllText → str`, in the new `ScalarReturningBcl` table). This closes the last exit-0-then-CS0029 lane in the rebind family. Critically, the comparison was reworked from exact type-string equality to **primitive-category** comparison (string / bool / numeric): only a cross-category rebind — the CS0029 case with no implicit conversion — is flagged. This **fixes a pre-existing false positive** in the #733 check, where `§B{~x:i64} 0` then `§B{~x} 5` (valid C#: `long x = 0; x = 5;`, an int literal widening to long) was wrongly rejected. Implicit numeric widening (`i32`→`i64`, `i32`→`f64`) and any unknown/reference/`char`/`object`/user type are now never false-positived. The trade-off is deliberate conservative misses *within* the numeric category — conversions that require an explicit cast (CS0266), accepted though Roslyn rejects them, each pinned by a differential `KnownGap` row: an integral narrowing (`i64`→`i32`) and the `decimal`↔`float`/`double` pair (an explicit conversion exists but no implicit one, so CS0266 — not CS0029). A precise fix for the decimal pair would split `decimal` into its own category and reclassify decimal literals. Flips the `ShadowingDifferentialTests` `#740` known gap to a rejected idiom. Same family as #722/#724/#725/#727/#732/#733/#738.
- **Foreach iteration-variable rebind error (#738).** `calor -i` now rejects a write to a `§EACH`/`§EACHKV` iteration variable — both a mutable `§B` rebind (`§EACH{e1:x} arr` then `§B{~x:str} "y"`) and an `§ASSIGN` to it (`§ASSIGN x "y"`), including a `§EACHKV` key/value (`Calor0257`). A foreach iteration variable is **read-only** in C#: the emitter emits `x = "y"` inside the loop (CS1656 — cannot assign to an iteration variable) and a re-declaration would shadow it (CS0136), so there is no valid emission — previously `calor -i` exited 0 and produced C# that failed `dotnet build`. `BindValidationPass` now tracks the set of live foreach *iteration* variables (nested same-name safe) and this reject supersedes the `Calor0256` type-mismatch check for that variable. Scoped precisely to the read-only variables: a `§L` **for-loop** variable and a `§EACH` **index** counter (`§EACH{e1:x:T:i}`, emitted as a plain `var i = -1; … i++`) are reassignable locals and stay legal, as is a fresh `§B` of the same name **after** the loop closes. This change also adds the missing `DictionaryForeachNode` (`§EACHKV`) case to the pass's statement walker, which is what first descends the analysis into `§EACHKV` bodies at all — so the array-to-collection (`Calor0254`), shadowing (`Calor0255`), and type-changing-rebind (`Calor0256`) checks now fire inside `§EACHKV` loops too (they never did before; corrects the #724 note that claimed `§EACHKV` traversal). Flips the `ShadowingDifferentialTests` foreach-var-rebind case from a known gap to a rejected idiom and adds a `§ASSIGN`-shaped row. Also extends `Calor0255` (#727) to the reverse shadowing direction: a **loop variable** (`§L` for-var, `§EACH`/`§EACHKV` item/index/key/value) that reuses the name of an enclosing local or parameter is now rejected — e.g. `§B{~x} 0` then `§L{l1:x:…}` is CS0136 in C# and was previously accepted. Same family as #722/#724/#725/#727/#732/#733.
- **Type-changing mutable-rebind error (#733).** `calor -i` now rejects a mutable `§B` that rebinds a variable with a *different* type — e.g. `§B{~x:i32} 0` then `§B{~x:str} "hi"` (`Calor0256`). A mutable rebind is a reassignment; the emitter emits `x = value` against the variable's original type, so a mismatched value fails `dotnet build` with CS0029/CS0266 — previously `calor -i` exited 0. The rebind's type is the explicit annotation when present, otherwise the statically-known type of a **literal** initializer (so `§B{~x:i32} 0` then `§B{~x} "hi"` is also caught); an unannotated **non-literal** mismatched value still needs value-type inference and is tracked in #740. Same-type rebinds and the unannotated accumulator idiom (`§B{~result} (* result i)`) are unaffected, and sibling same-named variables of different types are new declarations, not rebinds. Both types are canonicalized (`i32`≡`INT`) before comparing, so a matching parameter/loop-variable rebind is not falsely flagged, and the message is surface-spelled (`i32`/`str`, never the internal `INT`/`STRING`) via the new `AttributeHelper.ToSurfaceSpelling` (systemic retrofit + guard test tracked in #741). Flips the `ShadowingDifferentialTests` type-changing-rebind case from a known gap to a rejected idiom.

### Fixed
- **Scope-aware mutable-rebind codegen (#732).** `CSharpEmitter` tracked declared variables in a *flat per-function* set, so a mutable `§B{~x}` in a sibling block was emitted as an assignment to an out-of-scope variable (`x = 2;` where `x` from a since-closed sibling block no longer exists) — valid-looking Calor that failed `dotnet build` with CS0103. The emitter now tracks declarations in a **scope stack** (push per control-flow block, pop on exit), so a rebind in a closed sibling block re-declares (`int x = 2;`, valid) while the accumulator idiom (a rebind of a still-live enclosing local, e.g. `§B{~result} (* result i)` in a loop) stays a reassignment. Construct-introduced names — `§L` loop variables, `§EACH`/`§EACHKV` iteration variables, `catch`/`using` bindings — and **parameters** are registered in the emitter's scope model too, so a mutable rebind of a reassignable one (for-loop var, parameter, catch/using binding) emits a valid `x = …` rather than a CS0136 re-declaration. `BindValidationPass`'s reassignment classification was made scope-aware to match. Flips the `ShadowingDifferentialTests` sibling-mutable-rebind case to the clean invariant and adds for-loop-var/parameter rebind rows; `self-test` goldens unchanged. (A `§B` rebind of a `§EACH` iteration variable is invalid C# either way — CS1656 — and is tracked as a reject-diagnostic follow-up in #738.)

### Added
- **Converter §CSHARP fallback on unparseable output (#717).** The C#→Calor converter now parse-validates its own output: when a C#-preserving mode is active (`passthroughOnError`, or interop mode — e.g. the `calor_convert` MCP tool with `passthroughOnError: true`) and the emitted Calor for a top-level member does not parse, that member is re-emitted as a `§CSHARP{…}§/CSHARP` interop block carrying its original C#, so the output is always valid Calor instead of silently-broken text (previously it produced identical broken output with `interopBlocksEmitted: 0`). Defense-in-depth: the visitor's own §CSHARP wrapping already handles known-unsupported features (~65 exotic constructs probed, none reached this path), so this guards future/unknown emitter gaps like the #705 block-lambda bug.
- **Local-shadowing error (#727).** `calor -i` now rejects a `§B` that declares a new local reusing the name of a local, parameter, or **loop variable** already in an **enclosing** scope (`Calor0255`) — e.g. an immutable `§B{x}` inside a block when an outer `§B{x}`, a parameter `x`, or a `§L`/`§EACH` loop variable `x` is in scope. C# forbids this (CS0136), so the emitted code would fail `dotnet build`; previously `calor -i` exited 0 and produced broken C#. The check mirrors the emitter's mutable-rebind rule: a **mutable** `§B{~x}` reusing a name already bound in the function is a reassignment (`x = …`), not a shadowing declaration, so the accumulator idiom (`§B{~result} (* result i)` in a loop) is unaffected; and a local may legally shadow a **field**. A new `ShadowingDifferentialTests` enforces the load-bearing invariant — *if `calor -i` accepts a program, its emitted C# compiles under Roslyn* — and pins three still-open exit-0-then-broken-build gaps in the same family: same-scope duplicate (CS0128, #731), sibling mutable rebind (CS0103, #732), and type-changing mutable rebind (CS0029, #733). Same family as #722/#724/#725.
- **Array-to-collection check extended to argument position (#725).** `Calor0254` now also fires when an array is passed where a user function/method declares a concrete-collection parameter — e.g. `§C{Take} §A §C{File.ReadAllLines}` when `Take` takes a `List<str>`. `BindValidationPass` gained a `name → parameter types` map (keyed by name/arity, so Calor's arity-based overloads resolve) and a recursive expression walker that finds calls in every checked expression position (binding initializers, return/assign values, call statements, print/expression statements, and conditions). Only same-module user callees are resolved (BCL and cross-module callees are conservative false negatives); block-lambda bodies are still not traversed. This completes the array-vs-collection trap across all four positions (binding/return/reassign/argument).
- **Array-to-collection check extended to return and reassignment positions (#724).** `Calor0254` (the #722 array-vs-collection trap) now also fires when a function/method declared `-> List<T>` (or another concrete generic collection) returns an array (`§R §C{File.ReadAllLines}`), and when `§ASSIGN` reassigns an array into a collection-typed local, parameter, or class field — not just in binding position. `BindValidationPass` gained proper lexical scoping (so an inner-block binding no longer mis-types a same-named outer variable) and now traverses `§EACH` bodies — a pre-existing traversal gap that had silently exempted the file-iteration idiom where this trap most often occurs. (`§EACHKV` bodies were still not traversed at all; that gap was closed later in #738.) The diagnostic message no longer echoes the internal normalized type spelling. Same rule (collection interfaces still accepted), same shared array-source recognition. Argument position (an array passed to a `List<T>` parameter, which needs call-site type flow) is tracked in #725; block-lambda bodies are not traversed.
- **Array-to-collection type error at the language level (#722).** `calor -i` now rejects a **binding** declared as a concrete generic collection (`List<T>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, …) whose initializer is an array — e.g. `§B{lines:List<str>} §C{File.ReadAllLines}` — with a dedicated diagnostic (`Calor0254`) pointing at the binding, instead of emitting `List<string> x = File.ReadAllLines(...)` that fails a downstream `dotnet build` with CS0029. Mirrors C#'s rule: an array satisfies the collection *interfaces* (`IList<T>`, `IEnumerable<T>`, …) but not the concrete classes, so interface-typed bindings are still accepted. The array source is recognized for known array-returning BCL methods (a table shared with #712's docs guard, so the two cannot drift) and any user function declared `-> [T]`. This is the language-level counterpart to #712's docs guard — it protects agents who write the mistake independently, not just those copying the exemplar. Scope: this covers **binding** position; the same trap in reassignment (`§ASSIGN`), return, and argument positions is tracked in #724.
- **Exemplar compile-checking (#712).** `calor self-check docs` now compiles every complete `§M` program in the agent syntax exemplar (`Resources/agent-syntax-exemplar.md`, served to agents as `calor://primer`) all the way to C# and runs the **generated C# through Roslyn's full semantic model** (`Calor1330`) — the only layer that catches type errors the Calor pipeline itself emits without complaint, such as binding `File.ReadAllLines` (an array) to `List<str>` (CS0029). The copyable fragment reference lines, which intermix prose and free identifiers and cannot be compiled standalone, get a targeted lint for that same array-vs-collection trap (`Calor1331`). Mutation-tested: reintroducing the `List<str>` `ReadAllLines` bug fails self-check whether it lands in a complete program or a fragment line. Backed by `ExemplarCompilesTests` (runs in every CI environment). Scope note: this guards the exemplar *document*; the language-level fix that rejects the mistake in any source is #722 (above).

### Changed
- **CI hardening — test-suite C# exempt from the Calor-first guard; resilient Z3 download.** The `calor-first-guard` (which rejects new `.cs` files to keep product source Calor-first) now structurally exempts the `tests/` tree — xUnit test suites are C# by nature, so adding a test no longer needs an allowlist entry or an admin merge; product source under `src/`/`tools/` stays fully governed (behavioral test added). Separately, `download-z3.sh` now fetches the Z3 release through a retrying `curl` (`--retry 5 --retry-all-errors` with backoff and connect/max timeouts) so a transient network/TLS failure — e.g. the `curl exit 35` that intermittently failed the `test` build — retries instead of breaking CI.
- **Diagnostic renumbering — contract-verification results moved to Calor0710–0715 (#702).** The contract-verification pass previously reused `Calor0700`/`Calor0701`, which already meant `SemanticsVersionMismatch`/`SemanticsVersionIncompatible` — one number, two meanings. All verification-result codes now occupy a disjoint sub-band and each has a named `DiagnosticCode` constant: Z3-unavailable `Calor0700→0710`, precondition-may-be-violated `Calor0701→0711`, postcondition-may-be-violated `Calor0702→0712`, postcondition-proven `Calor0703→0713`, verification-summary `Calor0704→0714`, verification-cache-stats `Calor0705→0715`. `Calor0700`/`Calor0701` now unambiguously mean the semantics-version diagnostics. **Action for agents:** any tooling filtering verification output on `Calor0700`–`Calor0705` must switch to `Calor0710`–`Calor0715`.

## [0.7.0] - 2026-07-16

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x (Calor wins)
  - ErrorDetection: 1.49x (Calor wins)
  - TokenEconomics: 1.42x (Calor wins)
  - RefactoringStability: 1.38x (Calor wins)
  - EditPrecision: 1.36x (Calor wins)
  - InformationDensity: 0.98x (C# wins)
- **Programs Tested**: 217

> The agent dev-loop release: Phase 1 of the agent-native strategy (docs/plans/agent-native-strategy.md) complete — six items, each hardened by adversarial review. Static benchmark profile unchanged from v0.6.8 (these are tooling/dev-loop changes).

### Added
- **Source maps (#696).** `CSharpEmitter` emits `#line` directives mapping generated C# back to `.calr` source: downstream Roslyn errors, runtime stack traces, and debugger sessions now report `.calr` file/line instead of stranding agents in generated `.g.cs` files. Opt-out via `CompilationOptions.EmitLineDirectives`.
- **`calor run` and `calor test` (#697).** One-command execution of any `.calr` file or directory via temp-project materialization: effects enforcement on by default (`--permissive` to relax, now visible as warnings and threaded through cross-module enforcement), `--verify`/`--contract-mode`/`--enforce-effects` pass-through, process timeouts with entire-tree kill, exit-code propagation. Compilation unified in a shared `CompilationDriver` used by run/test and the root compile. The `CompileCalor` MSBuild task gains an `EnforceEffects` parameter and `Sdk.targets` passes `$(CalorEnforceEffects)`.
- **Structured diagnostics (#698, Phase 1 item 3 part 1).** `--format text|json|sarif` on the root compile and `lint`; a JSON/SARIF document is always emitted in structured mode (including early-exit errors, new Calor1300-band codes); `--verbose` routes status to stderr so stdout stays machine-parseable; lint returns real exit codes; schema documented in docs/cli/structured-output.md.
- **Write-path robustness (#699, Phase 1 item 5).** Fixable indentation diagnostics (`Calor0008`/`Calor0009`/`Calor0117`, all with machine-applicable one-pass fixes, no-op fixes never emitted); `calor format --heal` source-level repair with ambiguity reporting (not semantics-preserving — decisions surfaced per `file:line`); MCP `calor_check` auto-heal with post-heal diagnostics. Note: `Calor0008`/`Calor0009` warnings now fire on legacy tab/4-space files (fixes attached).
- **Doc drift detection (#700, Phase 1 item 6 part 1).** `calor self-check docs` machine-verifies agent-facing docs against the implementation: §-keywords vs the lexer, diagnostic codes vs bands, effect codes bidirectionally, hardcoded versions, and fenced `calor` examples parsed with the real parser (Calor1320-band findings; `drift:ignore` suppression convention). Runs in CI. First run found and fixed 30+ drift instances including documented-but-nonexistent keywords (`§INV`→`§IV`, `§FOREACH`→`§EACH`, `§MATCH`→`§W`) and 14 undocumented effect codes.
- **`calor watch` + CLI incrementality (#701, Phase 1 item 4).** Debounced incremental recompiles with NDJSON structured output; the MSBuild `BuildStateCache` moved into the compiler and shared. Cache trust boundaries hardened after adversarial review: content hashed from the bytes actually compiled (TOCTOU), summary-less cache hits recompile (cross-module effect enforcement survives warm builds), outputs verified by content hash. Plain-compile caching is opt-in via `--cache`; watch caches by default.
- **Phase 0 agent-native benchmark (#687–#694).** Two-arm live-agent measurement harness (`bench/phase0-agent-native/`), 16 determinism-validated fixture pairs, ~165 published live runs, and the pre-registered gates protocol (docs/plans/agent-native-gates.md). Outcome recorded honestly: the escaped-bugs gate is unmeasurable at authorable-fixture scale at current model capability (strategy §9, Option B); durable finding — Calor pays 2.7x iterations on green-field authoring but reaches full parity on modification tasks.

### Fixed
- **Obligation fact scoping (#686).** `FactCollector` collected if/while guards function-wide, so contradictory sibling guards made the assumption set UNSAT and vacuously discharged every obligation in the function; facts are now scoped to the source range they dominate, killed on rebinding, and an UNSAT pre-check refuses vacuous discharge.
- **`NullDereferenceChecker` (#686):** `unwrap_or`/`unwrap_or_default` classification was order-dependent due to an operator-precedence bug.
- **Calor runtime effect manifests (#687):** `Option`/`Result` combinators are manifest-entered as pure-modulo-arguments and Calor surface types (`?T`, `T!E`) resolve to runtime manifest keys, so combinator calls no longer hit the unknown-call path.
- **macOS portability (#688):** agent-invocation timeout no longer requires coreutils.

### Changed
- **Agent-facing docs corrected and drift-guarded:** CLAUDE.md/syntax-reference fixes (closer-form guidance, effect-code table completeness, keyword accuracy) now enforced by the CI spec-drift check.
- Diagnostic code space extended: 1300–1399 (CLI lint findings and command-level errors), 1320–1328 (doc drift). Calor0700/0701 band collision tracked in #702.

## [0.6.8] - 2026-07-01

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x ± 0.00 (Calor wins, large effect d=1.80)
  - ErrorDetection: 1.49x ± 0.00 (Calor wins, large effect d=1.21)
  - TokenEconomics: 1.42x ± 0.00 (Calor wins, composite metric)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.09)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.90)
  - Correctness: 1.29x ± 0.00 (Calor wins, large effect d=1.31)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, small effect d=0.34)
  - InformationDensity: 0.98x ± 0.00 (C# wins, medium effect d=-0.52)
- **Programs Tested**: 217

> **Note:** this release is CLI tooling and an internal refactor only — a source-level `calor fix --heal-closers` migrator and a shared return-classification helper. It contains no benchmark-affecting code changes, so the profile is unchanged from v0.6.7.

### Added
- **`calor fix --heal-closers` — a source-level CLI that finishes the `Calor0830` auto-heal story (#683).** Closer-form syntax (`§/F`, `§/M`, `§/L`, …) hard-errors at parse time, so the AST-based `calor format` / `calor lint --fix` paths cannot heal such a file — the error *is* a parse error, so those commands abort before they can read it. The new `calor fix --heal-closers <root> [--log <file>] [--revert] [--dry-run]` deletes legacy structural closers at the source level, rewriting a file into canonical indent-only form, and `--revert --log` restores it byte-exactly. A lexer-backed `LegacyCloserFormLint.ScanForHeal` keeps only closers that are genuine tokens, so a `§/F` embedded in a string literal or a `//` comment is left untouched (a raw text scan would corrupt it); removals are recorded as UTF-8 **byte** ranges (the `§` code point is two bytes) via the shared reversible migration-log schema, so revert is byte-exact even across non-ASCII content and CRLF line endings. This delivers the CLI heal command deferred in v0.6.6.

### Changed
- **Single-sourced return-value classification in a shared `Analysis/ReturnShape` (#684).** The void / async-void / iterator / accessor "does this owner return a value" classification was duplicated between `ReturnValidationPass` (which drives `Calor0205`) and `ContractVerifier` (which decides whether `result` is referenceable in a postcondition), risking drift between the two. Both now defer to a single `Analysis/ReturnShape` classifier, which deliberately distinguishes the *runtime* shape (`Classify`, folding in async/iterator lowering) from the narrow *header* predicate (`DeclaresValueOutput`, which does not — an iterator still *declares* `IEnumerable<T>`, so `result` stays referenceable in its postcondition). The refactor is behavior-preserving and the emitter's own signature / `WrapInTask` codegen is intentionally left untouched; a 31-case unit table pins every owner shape including the iterator divergence. This retires the "shared emitter `ReturnShape` refactor" follow-up noted in v0.6.7.

## [0.6.7] - 2026-07-01

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x ± 0.00 (Calor wins, large effect d=1.80)
  - ErrorDetection: 1.49x ± 0.00 (Calor wins, large effect d=1.21)
  - TokenEconomics: 1.42x ± 0.00 (Calor wins, composite metric)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.09)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.90)
  - Correctness: 1.29x ± 0.00 (Calor wins, large effect d=1.31)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, small effect d=0.34)
  - InformationDensity: 0.98x ± 0.00 (C# wins, medium effect d=-0.52)
- **Programs Tested**: 217

> **Note:** this release is compile-time-diagnostic, docs, and test-correctness only — two new hard-error diagnostics that reject non-compiling Calor *earlier* (closing the deferred "F-prerequisite invariant" gap from v0.6.6), plus an agent-docs sweep to indent-only syntax. It contains no benchmark-affecting code changes, so the profile is unchanged from v0.6.6.

### Added
- **`Calor0116` — malformed four-field `§F`/`§AF` function headers are now a parse error (#680).** A header like `§F{f1:Add:i32:pub}` looks reasonable but is silently wrong: function headers take at most `{id:name:visibility}`, and the return type belongs in the signature (`(...) -> type`). Left unflagged, the parser read the extra field's type as the visibility and *discarded the real visibility*, emitting a void method (e.g. `void Add() { return 0; }`, then **CS0127** in the generated C#). The parser now reports `Calor0116` with the correct 3-field-plus-arrow form. Only `§F`/`§AF` are affected; `§MT`/`§AMT` legitimately take a fourth *modifier* field, so they are untouched.
- **`Calor0205` — a value returned from a no-value owner is now a hard error (#681).** An always-on `ReturnValidationPass` flags a value-returning `§R expr` in the body of an owner that returns no value: a `void`/async-`void` function or method, an iterator (its body uses `§YIELD`/`§YBRK`), a constructor, a property/indexer `set`/`init` accessor, or an event `add`/`remove` accessor. Previously this silently produced non-compiling C# (**CS0127** / **CS1622**) — the classic case being a correct `void` header followed by `§R INT:0`. Because the check is always-on and reports a hard error, the design is conservative to guarantee **zero false positives**: it flags only expressions that are *definitely* a non-void value and can never be a valid C# statement-expression (literals, arithmetic/logical ops, references, ternaries, tuples, interpolated strings, ranges, `typeof`/`nameof`/`sizeof`); calls, `new`, `await`, and `++`/`--` are left unflagged because they can be void-typed or valid void statement-expressions (which is what keeps the C#→Calor migration lowering of `void F() => VoidCall();` safe). Completeness is enforced by construction via a reflection-based structural walker plus a completeness meta-test, and a corpus-clean pin asserts zero firings across all samples and benchmarks. Together with `Calor0116`, this closes the deferred "value returned from void function" / F-prerequisite follow-up noted in v0.6.6. (Scoped as diagnostic-only; a shared emitter `ReturnShape` refactor remains a tracked follow-up.)

### Documentation
- **Swept every agent-readable surface to indent-only syntax (v0.6.7 Item 0, #679).** The MCP primer surfaces, the `copilot-instructions`/`AGENTS`/`CLAUDE`/`GEMINI` templates, `README.nuget.md`, the evaluation skills doc, and the correct-Calor fields of the JSON resources were audited and corrected so no agent-facing teaching material still shows removed closer-form tags, four-field headers, or other syntax the compiler rejects. A new `AgentDocsSyntaxGuardTests` compiles/scans every surface and fails if any teaches non-compiling forms (four-field headers, `§B =` bind-equals, structural closers), keeping the guarantee from drifting.

### Fixed
- **`AgentDocsSyntaxGuardTests` surface paths are now cross-platform (#679).** The guard's doc-surface relative paths were written with Windows `\` separators and passed straight to `Path.Combine`, so on the Linux CI runner they resolved to a single literal filename segment and threw `FileNotFoundException` — failing every case on CI while passing locally on Windows. Each relative path's separators are now normalized to `Path.DirectorySeparatorChar` before combining.

## [0.6.6] - 2026-07-01

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x ± 0.00 (Calor wins, large effect d=1.80)
  - ErrorDetection: 1.49x ± 0.00 (Calor wins, large effect d=1.21)
  - TokenEconomics: 1.42x ± 0.00 (Calor wins, composite metric)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.09)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.90)
  - Correctness: 1.29x ± 0.00 (Calor wins, large effect d=1.31)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, small effect d=0.34)
  - InformationDensity: 0.98x ± 0.00 (C# wins, medium effect d=-0.52)
- **Programs Tested**: 217

> **Note:** this release is docs / tooling / test correctness only (primer + reference-doc fixes, `Calor0830` auto-heal, and two compile-time primer guards); it contains no benchmark-affecting code changes, so the profile is unchanged from v0.6.5.

### Fixed
- **`calor://primer` MCP resource now compiles (Track 1 / D1, #674).** The agent primer served at `calor://primer` (`McpMessageHandler.GetPrimerContent`) taught syntax the compiler rejects today — closer-form tags (`§/F`, `§/M`, `§/I`, `§/L`), ULID IDs, `§RESULT`, `§I`/`§O` markers, and empty `§R` — so an agent onboarded from the primer at session start wrote non-compiling Calor (`Calor0830`/`Calor0006`/…). The primer was rewritten to be fully indent-only and empirically compilable: 3-field `§F` headers with arrow signatures (`(i32:a, i32:b) -> i32`), lowercase `result` in postconditions, BCL-only effectful calls with declared `§E{cw}`, no structural closers, plus a "Common mistakes" section and a quick reference. Exposed to tests via `McpResourceValidator.GetPrimer()`.
- **`Calor0830` (legacy closer form) is now auto-healable, and its remediation no longer points to a dead end (Track 1 / D1b, #676).** The diagnostic told users to run `calor format`, but `calor format` and `calor lint --fix` parse the file first and abort on `HasErrors` — and `Calor0830` *is* a parse error, so those commands could never read, let alone fix, the file. `Parser.ReportLegacyCloser` now reports through `ReportErrorWithFix`, attaching a `SuggestedFix` that deletes the entire closer line (keyword + any optional `{id}` payload). This flows to the LSP quick-fix and the `calor_check apply:true` MCP tool, and the healed source compiles. The message now explains the block ends at its body's dedent; stale doc comments in `Diagnostic.cs` and `LegacyCloserFormLint.cs` that also referenced `calor format` were corrected. (No CLI heal command yet — parse-first `calor format`/`lint --fix` remain; wiring `LegacyCloserFormLint.Scan` into a CLI remediation is a tracked follow-up.)

### Documentation
- **Purged removed closer-form from teaching/reference docs (Track 1 / D1, #675).** Phase 4d removed structural closer tags (`§/M`, `§/F`, `§/I`, `§/L`, …), which now hard-error `Calor0830`, but the Markdown docs still claimed closers were "still accepted" and showed closer-form / stale pseudo-syntax — so an agent following Calor's own docs wrote non-compiling Calor. Corrected the false "still accepted" claims in `syntax-reference/structure-tags.md`, `syntax-reference/index.md`, and `ids.md` §2.2; modernized stale if / loop / match / class / try-catch code blocks in `semantics/core.md`, `dotnet-backend.md`, `inventory.md`, and `normal-form.md` from removed closer-form + obsolete AST pseudo-notation to current indent-only syntax. Every concrete example rewritten was compiled with `calor` and succeeds. (Records, with-expressions, and property patterns remain a deferred semantics-doc modernization pass.)

### Tests
- **`PrimerCompilesTests` — the semantic guard that every correct module the primer teaches compiles (#674).** Extracts every complete `§M` module from `calor://primer` and compiles it via `Program.Compile` under the same options `calor_compile` uses by default, asserting zero errors, plus a guard that all taught modules are discovered. This is the guard that would have caught the closer-form/`§RESULT` lies that 5 review loops and every string-based test missed.
- **`PrimerMistakesRejectedTests` — the dual guard: every "Common mistakes (these do NOT compile)" example genuinely fails to compile (Track 1 / D2a, #677).** Each curated fragment is rewritten into the smallest complete module where it would naturally appear and asserted to fail at **either** the Calor layer (`HasErrors`) or the generated-C# layer (Roslyn). The 4-field `§F{f1:Add:i32:pub}` header is caught only at the C# layer (**CS0127** — Calor accepts it but emits `void Add() { return 0; }`; a Calor-level "value returned from void function" check is the deferred "F-prerequisite invariant" follow-up). Drift guards (`Primer_ListsEachCuratedMistake`, `Primer_MistakeCount_MatchesCuratedSet`, `CorrectModule_CompilesAtBothLayers`) keep the curated set and the primer in sync.

## [0.6.5] - 2026-06-30

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.32x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x ± 0.00 (Calor wins, large effect d=1.80)
  - ErrorDetection: 1.49x ± 0.00 (Calor wins, large effect d=1.21)
  - TokenEconomics: 1.42x ± 0.00 (Calor wins, composite metric — see Fixed)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.09)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.90)
  - Correctness: 1.29x ± 0.00 (Calor wins, large effect d=1.31)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, small effect d=0.34)
  - InformationDensity: 0.98x ± 0.00 (C# wins, medium effect d=-0.52)
- **Programs Tested**: 217

> **Note:** the overall and TokenEconomics figures rose vs v0.6.4 (1.28x → 1.32x; 1.11x → 1.42x) as a **measurement correction**, not a Calor improvement — the TokenEconomics metric now reports the composite it always computed (see Fixed). Calor still uses more *raw tokens* than C# on small programs.

### Fixed
- **`TokenEconomics` benchmark metric now reports the composite it computes (the discarded-composite bug, #668).** `TokenEconomicsCalculator.CalculateAsync` computed a composite advantage — the geometric mean of the token, character, and line ratios — and then **discarded it**, reporting the raw token-count ratio only despite the metric being named `CompositeTokenEconomics`. The category now reports the composite. The metric is deterministic (pure token/char/line counting, no LLM sampling), so its 95% CI equals its point estimate. **This raises the headline numbers — TokenEconomics from `1.11×` (token-only) to `1.42×` (composite), and overall from `1.28×` to `1.32×` — purely as a measurement correction; it is not a Calor improvement.** The honest caveat is documented: Calor still uses *more raw tokens* than C# on small programs (the `§`-sigil premium), but is more compact once character and line counts are included. Fix applied to both calculator copies (`tests/Calor.Evaluation/Metrics/TokenEconomicsCalculator.cs`, `src/Calor.Compiler/Evaluation/Metrics/TokenEconomicsCalculator.cs`); the misleading "Token savings: … fewer tokens" report line was corrected to "Compactness: … more compact (composite)". Regression coverage: `MetricCalculatorTests.TokenEconomicsCalculator_ReportsCompositeAdvantage_NotRawTokenRatioOnly` pins that the reported advantage equals the geometric mean of the three ratios (not the token ratio alone).

### Changed
- **v0.7 `TokenEconomics` gate recalibrated against the corrected metric (#668).** The deferred v0.7 acceptance criterion ("lower-95%-CI > 1.122") was a token-only target derived from the buggy metric. It is superseded by a composite gate of **≥ 1.40×** (regression guard anchored to the measured 1.42× v0.6.5 baseline). Documented transparently in `docs/plans/v0.6-call-closer-elision.md` §8 criterion 4, with correction notes in `docs/plans/v0.6-bind-inference-formalization.md`, `docs/plans/v0.6.4-roadmap.md`, and the public `token-economics` benchmark metric pages (which previously and incorrectly reported the category as "C# wins").

## [0.6.4] - 2026-06-16

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.28x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.84x ± 0.00 (Calor wins, large effect d=1.80)
  - ErrorDetection: 1.49x ± 0.00 (Calor wins, large effect d=1.21)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.09)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.90)
  - Correctness: 1.29x ± 0.00 (Calor wins, large effect d=1.31)
  - TokenEconomics: 1.11x ± 0.00 (Calor wins, negligible effect d=-0.12)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, small effect d=0.34)
  - InformationDensity: 0.98x ± 0.00 (C# wins, medium effect d=-0.52)
- **Programs Tested**: 217

### Fixed
- **Parser: elided-call statement no longer steals the parent block's terminating Dedent.** `ParseCallStatement` previously called `ExpectBlockEnd(EndCall)` unconditionally after a `§A`-argument list (and excluded `Dedent`/`Eof` from the zero-arg implicit-close branch), which consumed the enclosing function/if/loop body's terminator when no `§/C` was actually present. The bug manifested whenever an elided call (`§C{X}` or `§C{X} §A arg`) was the last statement of a function body that was followed by a sibling top-level declaration (e.g. another `§F`) — the parser then tried to parse `§F` as a statement and reported `Calor0100: Expected statement but found Func`. Discovered while modernizing `samples/TypeSystem/typesystem.calr` for v0.6.4 item C. Fix at `src/Calor.Compiler/Parsing/Parser.cs ParseCallStatement` + new `DedentRunEndsAtEndCall` helper. Regression coverage: 4 new tests in `CallStatementImplicitCloseTests` (`V064_ZeroArgStmt_LastInBody_BeforeSiblingFunc_Parses`, `V064_OneArgStmtViaA_LastInBody_BeforeSiblingFunc_Parses`, `V064_OneArgStmtInline_LastInBody_BeforeSiblingFunc_Parses`, `V064_LegacyMultiLineCall_StillParses`).

### Internal
- **`samples/TypeSystem/typesystem.calr` and matching E2E scenario `tests/E2E/scenarios/04_option_result/input.calr` modernized to v0.6.3 canonical syntax.** Replaced the legacy `§OK{§ARR{arr_init:any} §ARR{arr_init:any} value §/ARR{arr_init} §/ARR{arr_init}}` triply-nested-array form (an artifact of mass C# → Calor conversion that produced incorrect type-erased generated C# like `Result.Ok<object, string>(new object[] { new object[] { new object[] { 100 } } })`) with the canonical short form `§OK value` / `§ERR "msg"`, which now generates the intended `Result.Ok<int, string>(100)` / `Result.Err<object, string>("msg")`. Also elided `§A` and `§/C` on all `§C{...}` calls per v0.6.3 emitter rules. The matching `output.g.cs` golden was regenerated. Closes v0.6.4 roadmap item C; the underlying skip the v0.6.3 bulk migrator (`calor fix --elide-call-closers`) hit on this file was the parser bug above. Latent emitter asymmetry remains: `CalorEmitter` still writes `§OK{value}` (with braces) for non-array `Result.Ok` values, which round-trips through the parser as `Ok<object, string>(new object[] { value })`. Tracked separately for v0.7.

### Documentation
- **v0.6 bind-inference RFC §7 — `Calor0250` open question resolved.** The RFC asked "Should `Calor0250` be promoted from warning to error in v0.7?" but the diagnostic was always shipped as a hard error (see `Binder.cs:279` and `BindValidationPass.cs:223`, both `ReportError`); §5's severity table already listed it as **error**. The open-question bullet was a stale carry-over from the RFC v1 draft. Updated §7 to record the resolution and cite the v0.6.4 corpus-clean audit (zero firings across 230 `.calr` files in `samples/` + `tests/TestData/Benchmarks/`).

### Tests
- **`BindCorpusCleanTests.Corpus_HasZeroBindInferenceFirings`** — permanent CI-enforced pin that runs `BindValidationPass` (strict inference on) against every `.calr` file under `samples/` and `tests/TestData/Benchmarks/` and asserts zero firings of `Calor0250`/`Calor0251`/`Calor0252`/`Calor0253`. Lex/parse failures are skipped (some corpus files use experimental shapes outside this audit's scope); only the well-parsed subset is audited. Any future regression in the corpus or a tightening of the bind-inference checks will now block merge with the offending file + diagnostic in the failure message.

### Added
- **7 new TokenEconomics benchmark fixtures** (ids 053–059, `tests/TestData/Benchmarks/TokenEconomics/`) exercising v0.6.3 expression-context call elision and v0.6 bind-inference, with two neutral controls. These broaden corpus coverage of elision/bind-inference patterns (parser, formatter, delegation, aggregation shapes):

  | ID | Name | Pattern | Composite ratio |
  |---|---|---|---|
  | 053 | ParseAndDouble | bind from one-arg expr-context call (parser pattern) | 1.42x |
  | 054 | FormatHeader | bind from one-arg expr-context call (formatter pattern) | 1.43x |
  | 055 | ReturnMapped | direct return from one-arg expr-context call (delegation) | 1.45x |
  | 056 | AggregateStats | bind-inference from typed arithmetic (mean-of-three) | 1.52x |
  | 057 | TemperatureRange | bind-inference for chained typed intermediates | 1.47x |
  | 058 | ThreeWayMerge | three-arg expr-context call (NEUTRAL control — no elision) | 1.34x |
  | 059 | NamedConfig | named-arg expr-context call (NEUTRAL control — `§A[name]` excluded from elision) | 1.32x |

  **Correction / honest measurement:** these fixtures were originally added (v0.6.4 roadmap item A) to push the `TokenEconomics` 30-run lower-95%-CI past the v0.7 gate of 1.122. They do **not** achieve that. The `TokenEconomics` category measures **raw token count only** — `TokenEconomicsCalculator` computes a token×char×line composite (the ratios in the table above) but discards it and reports `calorTokenCount`/`csharpTokenCount`. On small focused programs Calor's `§`-sigil punctuation costs *more* tokens than the equivalent C#, so the new fixtures' token ratios average ~0.80 (C# leaner) and nudged the category from 1.12x (v0.6.3) down to **1.11x**. They are retained because they are representative, honest programs — the benchmark deliberately includes cases C# wins (e.g. InformationDensity 0.98x). The v0.7 `TokenEconomics` gate remains **open**, now correctly understood to require token-favorable (high-C#-ceremony) programs rather than composite-favorable ones; the discarded-composite in the calculator is flagged as a latent bug for v0.7 review.

## [0.6.3] - 2026-06-13

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.29x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.86x ± 0.00 (Calor wins, large effect d=1.84)
  - ErrorDetection: 1.51x ± 0.00 (Calor wins, large effect d=1.25)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.10)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.85)
  - Correctness: 1.30x ± 0.00 (Calor wins, large effect d=1.37)
  - TokenEconomics: 1.12x ± 0.00 (Calor wins)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, marginal)
  - InformationDensity: 0.99x ± 0.00 (C# wins, small effect d=-0.47)
- **Programs Tested**: 210

### Added
- **`calor fix --elide-call-closers` bulk migrator (CLI + SDK).** New `calor fix` subcommand that rewrites existing `.calr` source trees to the v0.6.x call-closer-elided form: zero-arg `§C{X} §/C` → `§C{X}` and same-line one-arg `§C{X} §A arg §/C` → `§C{X} arg`. Multi-line forms, named-arg (`§A[name] x`), multi-arg, and `ref`/`out`/`in` arg modifiers are left untouched. Computes token-precise byte spans on the original source and records them as `{file, byte_offset, byte_length, removed_bytes_base64}` entries (shape shared with `StructuralIdDropper.LogEntry`) so `--revert --log <file>` restores byte-for-byte. Includes a canonical-emit safety net (re-parse the migrated source, re-emit both ASTs through `CalorEmitter`, drop the file's edits on any divergence) that catches semantics-changing edits (e.g. a trailing `§+ y` sibling that would be absorbed into the call's arg expression). Mutually exclusive with `--drop-structural-ids` and `--compact-ids`; supports `--dry-run` and `--log`. Implementation: `src/Calor.Compiler/Migration/CallCloserElider.cs`. Tests: 12 cases in `tests/Calor.Compiler.Tests/Migration/CallCloserEliderTests.cs` (zero-/one-/multi-arg, named args, nested, multi-line skip, round-trip byte equality, idempotence, lex-error skip). Closes the v0.6.3 item from `docs/plans/v0.6-call-closer-elision.md` §2.3 ("No new migrator (yet)").
- **LSP quick-fixes for strict bind-inference diagnostics `Calor0251`/`Calor0252`/`Calor0253`.** Each diagnostic now ships a `SuggestedFix` that inserts the recommended `:type` annotation right before the closing `}` of the bind's attribute block. Concrete templates: `:Option<object>` (for `§NN`), `:object?` (for `null`), `:Vec<object>` / `:Map<object, object>` / etc. arity-aware per the matched generic factory, and `:f64` (for ambiguous numeric). Surfaces in any IDE talking to `calor-lsp` via the existing `CodeActionHandler` and in the CLI's existing fix-application paths. Closes #644. Only fires on canonical bind shapes (`§B{name}` / `§B{~name}`) so the edit placement is provably correct.
- **`Calor.LanguageServer.DocumentState.Reanalyze` now runs `BindValidationPass`** so strict-bind diagnostics (and their quick-fixes) surface in editors; previously the LSP only ran the lexer/parser/binder and these diagnostics were CLI-only.

### Changed
- **Expression-context `§C` calls now elide `§/C` by default for one-argument forms.** `CalorEmitter.Visit(CallExpressionNode)` extends the v0.6.1 zero-arg elision and the v0.6.2 stmt-context one-arg elision to expression context: `§C{target} arg` (no `§A`, no `§/C`) when the argument is unnamed, the rendered first token is in the `StartsWithExpressionStarter` whitelist, and we are not inside an inline-sibling context. Conversion scorecard: 96/100 → 99/100 round-trip pass (+3 net, 0 regressions). RFC: `docs/plans/v0.6-call-closer-elision.md` §2.1/§2.2/§8.1.
- **Strict bind-inference diagnostics `Calor0251`/`Calor0252`/`Calor0253` are now default-on** (RFC v0.6 bind-inference-formalization §6 Phase 4). These flag bindings that cannot infer a concrete type without an explicit `:type` annotation: untyped `§NN`/`null`, well-known generic factory calls (`Vec.empty`, `List.empty`, etc.), and binary ops mixing integer and floating-point literals. Audit across `samples/` and `tests/TestData/Benchmarks/` (230 files): zero firings — the corpus is already strict-clean. Opt out for one release with `--no-strict-bind-inference` (CLI) or `CompilationOptions.StrictBindInference = false` (SDK). The `--strict-bind-inference` flag continues to be accepted for backward compatibility.

### Fixed
- **Parser: `Calor0150` no longer fires across sibling-statement boundaries.** When the next expression-start token after a one-arg elided call is on a different line, it is a sibling statement, not an ambiguous second positional arg. Previously the parser misclassified patterns like `§B{p} §C{f} §IDX{a} i` followed on the next line by `§IF p ...` as a second arg, raising a spurious Calor0150. Now gated by a same-line check at `Parser.cs ~7992`. Regression test: `ExpressionContext_OneArgFollowedBySiblingStatement_NoCalor0150`.
- **Emitter: `§LAM` body, `§WITH` target, and `§LIST`/`§HSET` element emit sites now use `AcceptInInlineSibling`.** These same-line sibling positions previously used raw `node.X.Accept(this)`, which could silently corrupt the AST after the one-arg expression-context elision landed. Guarded by the existing `CalorEmitter_HasNoRawAcceptInSpaceSeparatedSiblingPosition` static test.

### Internal
- **In-repo `.calr` corpus migrated to the elided form** by running `calor fix --elide-call-closers` against `samples/` and `tests/TestData/Benchmarks/`: 9 files changed, 92 elisions total (40 in `samples/`, 52 in `tests/TestData/Benchmarks/`), 0 regressions. `samples/TypeSystem/typesystem.calr` was skipped automatically by the migrator's canonical-emit safety net (it uses an older `() -> void` signature shape that does not survive re-parse after elision) — left untouched, still parses, still compiles.
- Closed stale PRs #559, #619, #625 (superseded by later work).
- Updated four conversion snapshots (`tests/Calor.Conversion.Tests/Snapshots/{05-01,05-02,05-03,12-02}.approved.calr`) for the mechanical `§A arg §/C` → `arg` shape change.

## [0.6.2] - 2026-06-10

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.29x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.86x ± 0.00 (Calor wins, large effect d=1.84)
  - ErrorDetection: 1.51x ± 0.00 (Calor wins, large effect d=1.25)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.10)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.85)
  - Correctness: 1.30x ± 0.00 (Calor wins, large effect d=1.37)
  - TokenEconomics: 1.12x ± 0.00 (Calor wins)
  - GenerationAccuracy: 1.02x ± 0.00 (Calor wins, marginal)
  - InformationDensity: 0.99x ± 0.00 (C# wins, small effect d=-0.47)
- **Programs Tested**: 210 (was 207 in v0.6.1 — three new TokenEconomics fixtures exercising statement-context call elision: `VoidSequence`, `LogPipeline`, `PairLogger`)

### Added
- **Elision-aware TokenEconomics benchmark fixtures.** Three new programs (`VoidSequence`, `LogPipeline`, `PairLogger`) added to `tests/TestData/Benchmarks/TokenEconomics/` to exercise the new statement-context `§/C` elision path. Two are favorable to Calor (zero-arg and one-arg call sequences); `PairLogger` is a neutral control using multi-arg calls where elision does not apply. See PR #653 for the bias analysis.

### Changed
- **Statement-context `§C` calls now elide `§/C` by default (when safe).** `CalorEmitter.Visit(CallStatementNode)` rewrites zero-argument calls as `§C{target}` and one-argument unnamed calls (with safe-prefix arguments) as `§C{target} arg`, matching the v0.6.1 behavior for expression-context calls. Elision is gated by `UseImplicitCallCloser` and is suppressed inside inline-sibling contexts (e.g. short lambda bodies) to avoid AST corruption. RFC: `docs/plans/v0.6-call-closer-elision.md` §3.2/§4. See PR #652.

### Removed
- **`calor diagnose` CLI command removed.** The command was deprecated in v0.5.x (PR #609) with a removal target of v0.6.0; this release completes that deprecation. For machine-readable diagnostics use the `calor_check` MCP tool with `action: "diagnose"` (or `calor_compile` with automatic fix application). Documentation pages and cross-links have been removed.

### Fixed
- **Contract verifier: class methods, user-defined types, and visibility preservation.** `ContractSimplificationPass` now preserves the `Visibility` of class methods so the contract verifier can be reached for `§MT` members. `ContractVerificationPass` extended to walk class-method bodies. The Z3 contract translator gained support for user-defined types and dot-path field access (`a.b.c`). PR #618.

## [0.6.1] - 2026-06-09

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.29x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.85x ± 0.00 (Calor wins, large effect d=1.84)
  - ErrorDetection: 1.52x ± 0.00 (Calor wins, large effect d=1.26)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.10)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.83)
  - Correctness: 1.31x ± 0.00 (Calor wins, large effect d=1.40)
- **Programs Tested**: 207

### Changed
- **`ConversionContext.UseImplicitCallCloser` now defaults to `true`** (was `false` in v0.6.0). The C# → Calor converter (`CalorEmitter.Visit(CallExpressionNode)`) now elides `§/C` for zero-argument calls by default, producing more idiomatic Calor output. The opt-out (`UseImplicitCallCloser = false`) is preserved and tested (`CallExpressionImplicitCloseTests.Emitter_ZeroArgCall_ImplicitCloserFlagFalse_PinsExplicitCloser`). One-argument elision remains intentionally deferred — see `docs/plans/v0.6-call-closer-elision.md` §2.2.

### Fixed
- **Parser: `§C` standard form no longer swallows trailing `Dedent`.** `Parser.ParseCallExpression` previously routed zero-arg calls (followed by `Dedent`) into the standard-form branch (which calls `ExpectBlockEnd(EndCall)`), and `ExpectBlockEnd` consumed the `Dedent` thinking it was an indent-only block terminator. Because `§C` is an inline expression (not an indent-aware block), this corrupted the structural parse of the enclosing method/if body. Fixed by changing the implicit-close gating predicate from `!IsBlockEnd(EndCall)` (which is `true` on Dedent/Eof) to `!Check(EndCall)`. Regression test: `Emitter_ZeroArgCall_AsLastStatementBeforeDedent_RoundTripsCorrectly`.
- **Parser: `§C` no longer absorbs a same-column sibling structural opener on the next line.** Because `IsExpressionStart()` returns `true` for `§IF`/`§MATCH`/`§NEW`/etc., a sibling opener immediately following a zero-arg `§C` (same column) was being absorbed as the call's inline argument. Fixed in both `ParseCallExpression` and `ParseCallStatement` by gating the inline-arg branch on `Current.Span.Line == startToken.Span.Line` — the inline-arg form only triggers when the candidate argument is on the same source line as `§C{target}`. Regression test: `Emitter_ZeroArgCall_FollowedBySiblingOpener_RoundTripsCorrectly`.
- **Parser: `§C` expression form now refuses implicit-close when the next `§A` is on the same line.** `ParseCallExpression`'s implicit-close branch previously allowed `Check(Arg) == true` to be treated as "no more args" whenever any `§A` was visible, including a same-line `§A` that genuinely belonged to *this* call. Now the inline branch only triggers when the next `§A` (if any) is on a different line — preventing the parser from prematurely returning a zero-arg call when more inline `§A`s follow on the same line (matters for `§BASE`/`§THIS` constructor initializers spread across multiple lines).
- **Parser: `§C` statement form supports zero-arg implicit close before sibling statements.** Previously `ParseCallStatement` fell through to the standard-form branch (which required `§/C`/Dedent/Eof) when a sibling statement followed a zero-arg `§C{target}` on the next line at the same indent, reporting `Calor0100`. The statement-form parser now recognizes a zero-arg implicit close when the current token is not `§A`, `§/C`, `Dedent`, or `Eof`.
- **Emitter: zero-arg `§C` inside an inline-sibling context now keeps explicit `§/C`.** With the new default (`UseImplicitCallCloser = true`), naively eliding `§/C` from a zero-arg call emitted inside another call's `§A` chain or inside any space-separated sibling position caused **silent AST corruption**: e.g. `M(A(), 2)` round-tripped as `M(A(2))`, and `new[] { A(), B() }` round-tripped as a single element `A(B())`. `CalorEmitter` now tracks an `_inInlineSiblingContext` counter via the `AcceptInInlineSibling` helper; zero-arg `§/C` elision is suppressed whenever the counter is non-zero. The helper is applied at every emit site producing two or more expressions on a single line: `§A` args of calls (`§C`/`§NEW`/`§BASE`/`§THIS`), `§KV` key+value of dict entries (`§DICT` body, `DictionaryNode`, standalone `KeyValuePairNode`), `§PUT`/`§SETIDX`/`§INS`/`§IDX` collection ops, Lisp-form binary ops (`(op a b)`), null-coalesce (`(?? a b)`), inline conditional (`(? c t f)` and `§IF` form), forall/exists/implication bodies, and `STR_OP`/`CHAR_OP`/`SB_OP` arg lists. Top-level / leaf-position calls (binding initializers, return values, etc.) still elide as before. Regression tests: `Emitter_ZeroArgCallAsArgInMultiArgCall_KeepsExplicitCloser`, `Emitter_AdjacentZeroArgCallsInArrayInitializer_KeepsExplicitClosers`, `Emitter_ZeroArgCallAsTopLevelExpression_StillElidesCloser`, plus 6 coverage tests pinning §NEW args, §BASE/§THIS args, §KV, §PUT, §SETIDX, §INS, §IDX, Lisp binary-op, null-coalesce, conditional, and forall/exists/implication bodies.

### Compatibility
- **Calor source emitted by v0.6.1 may not parse on v0.6.0 or earlier `calor` toolchains.** The new default emits more zero-arg `§C` calls without explicit `§/C`. While the v0.6.0 parser nominally accepts the implicit-close form, the two parser fixes above (`Dedent` swallowing and same-column sibling absorption) only ship in v0.6.1 — sources that exercise those layouts will mis-parse on v0.6.0. To produce v0.6.0-compatible output from v0.6.1, use any of:
  - **CLI single-file:** `calor convert --explicit-call-closers <input.cs>`
  - **CLI project migration:** `calor migrate --explicit-call-closers <path>`
  - **MCP `calor_convert` / `calor_migrate`:** `"explicitCallClosers": true`
  - **SDK:** `new ConversionOptions { UseImplicitCallCloser = false }`

  Note: round-trip (`C# → Calor → C#`) remains semantic/structural; the intermediate `.calr` is intentionally *not* byte-identical to v0.6.0 converter output unless the opt-out is used.

## [0.6.0] - 2026-06-04

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.29x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.85x ± 0.00 (Calor wins, large effect d=1.84)
  - ErrorDetection: 1.52x ± 0.00 (Calor wins, large effect d=1.26)
  - RefactoringStability: 1.38x ± 0.00 (Calor wins, large effect d=7.10)
  - EditPrecision: 1.36x ± 0.00 (Calor wins, large effect d=4.83)
  - Correctness: 1.31x ± 0.00 (Calor wins, large effect d=1.40)
- **Programs Tested**: 207

Per-program metrics are unchanged from v0.5.1 — the v0.6 token-economics features (compact IDs, `§C` call-closer elision, formalized `§B` inference) shrink agent-facing serialisations and tighten the binder/parser surface, but the benchmark corpus uses test IDs and explicit `§/C` forms by design, so the headline numbers are stable.

### Added
- **`§C` call-closer elision (RFC `v0.6-call-closer-elision`).** Expression-context `§C{target}` calls may now omit the trailing `§/C` in two cases: (1) zero arguments — `§B{n} §C{items.Count}` is equivalent to `§B{n} §C{items.Count} §/C`; (2) exactly one inline argument (no `§A`) — `§B{y} §C{Math.Abs} x` is equivalent to `§B{y} §C{Math.Abs} §A x §/C`. The parser disambiguates nested elided calls (e.g., `§C{Foo.bar} §C{Baz.qux} y` ≡ `Foo.bar(Baz.qux(y))`) by counting consecutive `§/C` closers relative to enclosing `§A` depth (`Parser._inOuterCallArgDepth`, bumped in `ParseCallStatement`, the expression-target-call branch, and the standard `§A`-loop branch). Trailing member access on inline arguments binds to the argument (`§C{Identity} obj?.Length` ≡ `Identity(obj?.Length)`); trailing member access on zero-arg calls binds to the call result (`§C{Maybe}?.Length` ≡ `Maybe()?.Length`). The explicit form continues to parse unchanged. See [Calls reference](/syntax-reference/calls/) and `tests/Calor.Compiler.Tests/CallExpressionImplicitCloseTests.cs` (24 tests pinning every RFC §3.2 case).
- **`Calor0150 AmbiguousCallContinuation`** — New diagnostic in the reserved `Calor0150-0159` range. Fires when an elided `§C` already consumed one inline argument and is followed by either (a) a second expression-start token (literal, identifier, nested `§C`, `§NEW`, …) or (b) a `§A` token (signalling a mixed inline/explicit form). The fix message recommends the explicit `§C{target} §A a §A b §/C` form.
- **`ConversionContext.UseImplicitCallCloser` emitter flag.** New opt-in property on `Migration/ConversionContext`. When `true`, `CalorEmitter.Visit(CallExpressionNode)` elides `§/C` for zero-argument calls. Default `false` for v0.6.0 backward compatibility. One-argument elision is intentionally deferred to v0.6.1 — flipping it on inside Lisp argument lists (`(+ §C{f} a §C{g} b)`) currently triggers `Calor0150` and requires context-aware tracking before it can be safely enabled.
- **`docs/syntax-reference/calls.md` and `website/content/syntax-reference/calls.mdx`** — Full user-facing reference for both call forms, covering all three disambiguation cases (A: trailing member on inline arg; B: ambiguous continuation / Calor0150; C: nested implicit-close calls), plus statement-context, expression-context (zero-arg, one-arg, multi-arg), and trailing member access examples.
- **`§B` bind-inference formalization (RFC `v0.6-bind-inference-formalization`).** The four supported `§B` forms — `§B{name}` (requires initializer), `§B{name} initializer` (inferred, immutable), `§B{name:type}` (explicit, no initializer), `§B{name:type} initializer` (explicit wins) — and the binder's shallow inference rule (bound type = `initializer.TypeName`, with `INT`/`STRING`/`BOOL`/`FLOAT` mapping to user-facing `i32`/`str`/`bool`/`f64`) are now documented in `docs/syntax-reference/binding.md` and `website/content/syntax-reference/binding.mdx`, with a per-initializer-shape inference table pinned by `tests/Calor.Semantics.Tests/BindInferenceDocsTests.cs`.
- **`Calor0250 BindRequiresTypeOrInitializer`** — `§B{name}` with no `:type` annotation **and** no initializer is now a hard error. Replaces the pre-v0.6 silent fallback that bound `x` as `INT` and produced wrong-typed C# with no diagnostic. Wired into the `calor compile` pipeline through `BindValidationPass` so the diagnostic carries proper span info and is reported once per offending binding.
- **`Calor0251` / `Calor0252` / `Calor0253` strict-mode bind-inference diagnostics (opt-in via `--strict-bind-inference`).** Three new diagnostics in the `Calor0250-0259` range, each silenced by an explicit `:type` annotation, scheduled to become default-on in v0.7 per RFC §6:
  - **`Calor0251 BindCannotInferNullLiteral`** — fires on `§B{x} §NN` or `§B{x} null` (untyped null literal). Suggested fix: add an `Option<T>` annotation.
  - **`Calor0252 BindCannotInferGenericReturn`** — fires on `§B{x} §C{Vec.empty} §/C` and other well-known generic factory targets (`Vec.empty`, `List.empty`, `Array.empty`, `Set.empty`, `Map.empty`). Suggested fix: add the collection's element-type annotation.
  - **`Calor0253 BindAmbiguousNumeric`** — fires on `§B{x} (+ INT:0 FLOAT:0.0)` — a binary op mixing integer and floating-point literal operands. Suggested fix: annotate with the intended result type.
- **`docs/syntax-reference/binding.md` and `website/content/syntax-reference/binding.mdx`** — New syntax-reference pages with all 4 `§B` forms, inference table, examples, round-trip behavior, and the full Calor0250–0253 diagnostic catalogue.
- **`docs/plans/v0.6-call-closer-elision.md` and `docs/plans/v0.6-bind-inference-formalization.md`** — Token-economics RFCs covering both v0.6 features (motivation, syntax, disambiguation rules, implementation plan, strict-mode rollout schedule).
- **v6 compact stable identifiers (default).** `IdGenerator.Generate(IdKind)` now mints 12-char Crockford-lowercase compact IDs (`f_7k9m2npqrstv`) per [v6 implementation plan](docs/plans/path-2-drop-ids-v6-implementation.md) and v5 RFC §16.F. The legacy 26-char Crockford-uppercase ULID form (`f_01J5X7K9M2NPQRSTABWXYZ12`) remains accepted by the parser, validator, and migration tooling, and is still produced by the new `IdGenerator.GenerateUlid(IdKind)` / `GenerateUlidWithPrefix` entry points. Saves ~9.7 tokens per ID in agent-facing serialisations.
- **`calor fix --compact-ids <root>`** — bulk repo-wide migrator from legacy ULID payloads to v6 compact payloads. Two-pass design with deterministic compact derivation (last 12 chars of the ULID payload lowercased), within-file and cross-file collision detection (re-mints fresh compact IDs on collision), and byte-exact revert via `--revert --log <file>`. Only rewrites payloads inside whitelisted ID-bearing section markers (`§M`, `§F`, `§AF`, `§L`, `§IF`, `§TR`, `§CL`, `§IFACE`, `§MT`, `§CTOR`, `§EN`, `§EXT`, `§RTYPE`, `§PROOF`, `§ITYPE`, `§IXER`, `§OP`, and their closers); ULID-shaped strings in comments, prose, or string literals are left untouched. Idempotent on already-migrated source.
- **`src/Calor.Compiler/Ids/CompactIdGenerator.cs`** — public generator for v6 compact IDs. Exposes `Alphabet` constant (`0123456789abcdefghjkmnpqrstvwxyz` — Crockford lowercase, excludes `i/l/o/u`), `PayloadLength = 12`, `GeneratePayload()`, `Generate(IdKind)`, `GenerateWithPrefix(string)`, `DeriveFromUlid(string)`, and `IsValidPayload(string)`. Uses `RandomNumberGenerator.Fill` + `byte & 0x1F` (no modulo bias).
- **`IdValidator` accepts both compact and legacy ULID forms.** New predicates `IsCompactId`, `IsLegacyUlidId`, and `IsCanonicalId` (union of the two for back-compat). New constant `IdValidator.CompactLength = 12`. New `Calor0821 LegacyUlidPayload` diagnostic code reserved for the opt-in lint that flags ULID payloads (the lint emits a fix-it patch pointing at `calor fix --compact-ids`).
- **`IdGenerator` prefix coverage extended to all 14 `IdKind` values.** Adds constants `EnumExtensionPrefix = "ext_"`, `RefinementTypePrefix = "rt_"`, `ProofObligationPrefix = "po_"`, `IndexedTypePrefix = "it_"`, `IndexerPrefix = "ix_"`. `GetPrefix` and `GetKindFromId` switches now exhaustively cover `EnumExtension`, `RefinementType`, `ProofObligation`, `IndexedType`, and `Indexer` — previously `IdAssigner.Generate(IdKind.EnumExtension)` would have thrown `ArgumentOutOfRangeException` at runtime. New `IdGenerator.ExtractPayload(string)` is format-aware (returns the payload regardless of whether it's a 12-char compact or 26-char ULID); `IdGenerator.ExtractUlid(string)` is retained but now returns `null` for compact payloads.
- **47 new tests across the v0.6 surface.** `tests/Calor.Compiler.Tests/CallExpressionImplicitCloseTests.cs` (24 tests pinning every RFC §3.2 case for call-closer elision), `tests/Calor.Compiler.Tests/Migration/CompactIdMigratorTests.cs` (23 tests covering single-ID rewrite, extra positionals preserved, closing-tag rewrite, untouched-compact, untouched-name, per-file collision, cross-file collision, existing-compact collision, byte-exact round-trip, idempotency, no-rewrite-outside-section-markers, determinism, parser-validation), plus expanded coverage in `tests/Calor.Ids.Tests/IdGeneratorTests.cs`, `tests/Calor.Semantics.Tests/BindInferenceDocsTests.cs`, and `tests/Calor.Compiler.Tests/CallStatementImplicitCloseTests.cs`.
- **`docs/ids.md` §3.1 / §3.3 / §8.3 / §10.2** and **`docs/philosophy/stable-identifiers.md`** updated to document the dual ID format, the new CLI command, and the compact-form properties.

### Changed
- **`Migration/CalorEmitter.Visit(CallExpressionNode)`** — Zero-argument calls in expression context now conditionally elide `§/C` when `ConversionContext.UseImplicitCallCloser` is `true`. The multi-argument and one-argument paths are unchanged in v0.6.0; the multi-argument path always emits `§/C`, and the one-argument path is pinned by tests as unchanged (zero-arg-only elision) pending the v0.6.1 context-aware enablement.

### Fixed
- **Binder no longer silently defaults `§B{x}` to `INT`.** A `§B{name}` with neither a `:type` annotation nor an initializer expression was silently treated as `INT` by the pre-v0.6 binder, producing wrong-typed C# with no diagnostic. v0.6 surfaces this as `Calor0250 BindRequiresTypeOrInitializer` through `BindValidationPass`. Existing well-formed code (which always carried either an annotation or an initializer) is unaffected.

## [0.5.1] - 2026-06-03

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.29x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.85x (Calor wins, large effect d=1.84)
  - ErrorDetection: 1.52x (Calor wins, large effect d=1.26)
  - RefactoringStability: 1.38x (Calor wins, large effect d=7.10)
  - EditPrecision: 1.36x (Calor wins, large effect d=4.83)
  - Correctness: 1.31x (Calor wins, large effect d=1.40)
- **Programs Tested**: 207

### Added
- **Phase 4c PR-4 — Parser strict-mode rejection of legacy structural closers.** A new opt-in `Parser` constructor (`new Parser(tokens, diagnostics, rejectLegacyClosers: true)`) escalates the previous opt-in lint `Calor0830 LegacyCloserForm` to a parser-level error at every site that would otherwise silently consume a legacy structural closing tag (`§/M`, `§/F`, `§/AF`, `§/MT`, `§/SW`/`§/W`, `§/L`, `§/WH`, `§/I`, `§/EACH`, `§/EACHKV`, `§/IFACE`, `§/CL`). Closers that still carry payload (`§/DO` condition, `§/PP` condition, `§/K` case delimiter) and inline expression closers (`§/C`, `§/T`, `§/NEW`, `§/A`, `§/THIS`, `§/BASE`, `§/INIT`, `§/LIST`, `§/DICT`, `§/HSET`, `§/ARR`, `§/LAM`, …) are intentionally NOT rejected.
- **`calor --input … --output … --allow-legacy-closers`** — Escape hatch on the CLI compile path for users mid-migration. By default the CLI compile path is now strict (`RejectLegacyClosers = true` on the options the CLI builds) and any closer-form input source produces `Calor0830` errors. `calor format` rewrites a file in canonical indent form. Other API surfaces (`Program.Compile(source, path, options)` callers, the MSBuild `<CompileCalor>` task, MCP tools, LSP, lint/format/convert/migration tooling) keep the lax default (`RejectLegacyClosers = false`) so existing callers see no behavior change while the cross-surface migration completes.
- **`CompilationOptions.RejectLegacyClosers`** — New opt-in property on `CompilationOptions` that the CLI compile handler sets to `!allowLegacyClosers`. Defaults to `false` to preserve backward compatibility for API consumers.
- **`tests/Calor.Compiler.Tests/ParserLegacyCloserRejectionTests.cs`** — 4 new tests covering: strict mode + indent form = clean, strict mode + legacy closers = `Calor0830`, lax mode + legacy closers = silent, strict mode + retained `§/DO` payload closer = silent.

### Changed
- **`tests/Calor.Compiler.Tests/CliMultiFileTests.cs`** — The three CLI fixtures (`MultiFile_CrossModuleEffect_Violation_Errors`, `MultiFile_CrossModuleEffect_Declared_Succeeds`, `MultiFile_OutputFlag_RejectedForMultipleInputs`) now use indent form so they continue to compile cleanly through the strict CLI compile path. Function bodies were re-indented (`§F` now sits at column 2 inside its parent `§M` at column 0, and child statements at column 4) and the trailing `§/F{…}` / `§/M{…}` closer lines were removed.

### Phase 4c PR-3 (continued)

### Changed
- **Phase 4c PR-3 — Benchmark metric calculators score indent form, not closer tags.** The four heuristic calculators under `tests/Calor.Evaluation/Metrics/` used to award credit for the presence of paired structural closing tags (`§/F{…}`, `§/M{…}`, …) as a proxy for "scope boundaries are explicit". With indent form now canonical, the dedent IS the scope boundary signal, so those bonuses now award credit for the presence of at least one indented body line per structural opener instead. Net score magnitude is preserved (`+0.05 closing-tag + 0.05 matched-pair + 0.05 completeness` → `+0.10 indented-body proportional + 0.05 indent-form completeness` in `ComprehensionCalculator`; equivalent rewrites in `EditPrecisionCalculator.EstimateCollateralRisk` / `CalculateCalorEditPrecision`, in all four boundary checks in `RefactoringStabilityCalculator.CalculateStructuralClarityScore`, and in the `InformationDensityCalculator` documentation comment). Detail keys reported in `GetCalorClarityFactors` were also renamed (`closingTagCount` → `indentedBodyLineCount`, `hasMatchedPairs` → `hasIndentedFunctionBody`) so dashboards reflect the new scoring substrate. Benchmark methodology documents under `docs/benchmarking/` (and the mirror MDX docs under `website/content/benchmarking/`) were updated to match. Closes the "scoring debt" entry logged from Phase 5. All 6,919 tests still green.

### Added
- **Phase 4b PR-2 — Inline-`§NEW` arguments preserved in calls.** `Migration/CalorEmitter.Visit(CallStatementNode)` previously hoisted every argument that contained any `§` marker into a temporary `§B{~_hoist000}` binding before the call, on the (overly defensive) assumption that nested section markers would confuse the parser. The Calor parser already balances nested `§NEW{…}§/NEW` and `§C{…}§/C` correctly (`Parser.HasEndNewBeforeEndCall`), so the only case that genuinely cannot be inlined is the multi-line object-initializer form (`§NEW{T}\n  Prop = val\n§/NEW`). The hoist condition is now narrowed to "argument string contains a newline" — inline forms like `§A §NEW{StringBuilder} §/NEW` and `§A §C{Foo.Bar} §/C` are emitted directly. The held-out `tests/E2E/scenarios/09_codegen_bugfixes/input.calr` fixture (which pinned the `Console.WriteLine(new StringBuilder(), new StringBuilder())` codegen path) has been re-migrated to indent form; its `output.g.cs` golden is byte-identical to the prior emission (modulo line endings).

### Phase 4b PR-1 (continued)

### Added
- **Phase 4b — `CalorFormatter` collapsed into a thin adapter over `Migration/CalorEmitter`.** The 1,004-line hand-written formatter at `src/Calor.Compiler/Formatting/CalorFormatter.cs` (which still emitted closer form, legacy visibility shorthand `pri`, legacy range operator `..`, and the legacy `with { … }` expression syntax) has been replaced by a ~110-line adapter that delegates to `new CalorEmitter().Emit(module)` and then post-processes the result to abbreviate IDs in tags (`m001 → m1`, `for1 → l1`, `if1 → i1`, `while1 → w1`, `do1 → d1`) so `calor format` matches the canonical migration emitter byte-for-byte except for IDs. As a result the `calor format` command, the `calor lint --fix` flow, and the `format` / `check` MCP tools all now produce indent form with consistent visibility (`priv`), range (`§RANGE start end`), with-expression (`§WITH target … §/WITH`), and class declaration order (visibility before modifiers).
- **`Calor0830 LegacyCloserForm`** — Opt-in lint that flags legacy structural closing tags (`§/M`, `§/F`, `§/AF`, `§/MT`, `§/AMT`, `§/CL`, `§/IFACE`, `§/EN`, `§/L`, `§/WH`, `§/I`, `§/TR`, `§/EACH`, `§/EACHKV`, `§/USE`, `§/UNSAFE`, `§/CHECKED`, `§/UNCHECKED`, `§/PROP`, `§/CTOR`, `§/OP`, `§/IXER`, `§/W`, `§/SW`) in source that has otherwise adopted indent form. The recommended machine fix is to run `calor format`. Closers that still carry payload (`§/DO` condition, `§/PP` condition, `§/K` case delimiter) and inline expression closers (`§/C`, `§/T`, `§/NEW`, collection-literal closers, etc.) are intentionally not flagged. Source-level scanner under `Analysis/LegacyCloserFormLint.cs`; tests at `tests/Calor.Compiler.Tests/Analysis/LegacyCloserFormLintTests.cs`.

### Fixed
- **`Migration/CalorEmitter.Visit(CatchClauseNode)`** — Catch filters now emit `§WHEN` (matching the token form the parser produces, and matching the `§WHEN` already emitted by `Visit(MatchCaseNode)` for match-arm guards). Previously emitted a bare `WHEN` keyword that, while accepted by the parser as a lowercase legacy converter quirk, did not round-trip cleanly when the input already used `§WHEN`.

### Phase 4 (continued)

### Added
- **Phase 4 — Bulk fixture migration to indent form.** All Calor `.calr` fixtures across the repository (samples, scripts, `tests/TestData/`, `tests/E2E/scenarios/`, `tests/Calor.Enforcement.Tests/Scenarios/`, and the embedded `src/Calor.Compiler/Resources/SelfTest/` self-test resources) have been rewritten in indent form. 408 tracked `.calr` files migrated via a new one-off harness (`tools/Calor.IndentMigrator/`) that round-trips each file through `Migration/CalorEmitter`. Inline closers that still carry semantic payload (`§/C` on call expressions, `§/NEW` on object creation, `§/T` / `§/THIS` / `§/BASE` / `§/INIT` on initializer chains, `§/DO` / `§/PP` / `§/K` for closer-form items deferred to Phase 4b) are retained where they appear inline. The `09_codegen_bugfixes` self-test scenario is deliberately kept in closer form because it pins the inline-`§NEW`-as-call-argument codegen path that the migration emitter currently lowers via a temporary `§B{~_hoist000}` binding.
- **Migration `CalorEmitter` fixes uncovered by the Phase 4 sweep:**
  - `§Q` / `§S` contract messages are now emitted in brace form (`§Q{"msg"} (cond)` / `§S{"msg"} (cond)`) so they round-trip cleanly through `Parser.ParseRequires` / `ParseEnsures`, which read the message from the `_pos0` attribute. The previous trailing-string form was non-parsable.
  - `INT[bits=N][signed=B]` types now compact back to the short aliases (`u8`/`u16`/`u32`/`u64`/`i8`/`i16`/`i32`/`i64`) before being emitted in the compact `(TYPE:name)` parameter syntax, via a new `CompactCanonicalIntAliases` regex pass in `TypeMapper.CSharpToCalor`. The bracketed canonical form cannot be re-parsed in that position, so emitting it produced unparsable output.
  - **`MatchExpressionNode` as a `§B` binding initializer** now emits the `§W{id:expr} target` header inline with `§B{name}` and writes case arms via `AppendLine` + `Indent` / `Dedent` so they respect the binding's current indent. Previously `Visit(MatchExpressionNode)` returned a multi-line string with hardcoded 2/4-space indents that got jammed onto the `§B` line, so the §K arms below ended up at absolute columns 2/4 — not relative to the enclosing block — triggering a Calor0099 dedent error on the very next arm whenever the binding lived inside a function body indented 5+ spaces. The fix adds a dedicated `MatchExpressionNode` branch to `Visit(BindStatementNode)` (mirroring the existing collection-initializer special cases) and a shared `EmitMatchExpressionAsBindingInitializer` helper. `samples/PatternMatching/matching.calr` (5 distinct match-expression bindings, including `§PREL` arms, literal arms, `§VAR` + `§WHEN` guards, and deep alternation) re-migrated cleanly as a result.
- **`tools/Calor.IndentMigrator/`** — One-off in-place migration harness used to bulk-rewrite `.calr` fixtures during Phase 4. Walks a directory **or a single file**, round-trips each `.calr` through the migration `CalorEmitter`, and writes the result back atomically. Skips files with lex/parse errors, normalizes line endings for comparison, and is idempotent under repeat sweeps. Supports `--dry-run`, `--verbose` / `-v`, and `--exclude <path>` (repeatable) so files known to pin closer-form codegen paths can be carved out. README at `tools/Calor.IndentMigrator/README.md`.

### Changed
- **Lint no longer flags leading indentation or blank lines.** With indent form now canonical, the two formatting lint rules introduced for the closer-form "agent-optimized" surface — "Line has leading whitespace (indentation not allowed)" and "Blank lines not allowed in agent-optimized format" — have been removed from both `Commands/LintCommand.cs` and `Mcp/Tools/CheckTool.cs`. The corresponding `LintRegressionTests.cs` cases have been inverted to assert that indentation and blank lines are accepted, and `Lint_IdAbbreviation_DetectsExpectedIssues` counts were halved to reflect that each block ID now appears once (on the opener) rather than twice (opener + closer).

### Added
- **Phase 3 — `CalorEmitter` emits indent form.** The C#→Calor migration emitter no longer emits structural closing tags (`§/M{…}`, `§/F{…}`, `§/CL{…}`, `§/MT{…}`, `§/L{…}`, `§/IF{…}`, `§/TR{…}`, `§/USE{…}`, `§/EACH{…}`, `§/EACHKV{…}`, `§/WH{…}`, `§/W{…}`, `§/ARR{…}`, `§/ARR2D{…}`, `§/UNSAFE{…}`, `§/SYNC{…}`, `§/FIXED{…}`, `§/EN{…}`, `§/EEXT{…}`, `§/DEL{…}`, `§/EVT{…}`, `§/EADD`, `§/EREM`, `§/GET`, `§/SET`, `§/INIT`, `§/CTOR{…}`, `§/IFACE{…}`, `§/PROP{…}`, `§/IXER{…}`, `§/OP{…}`, `§/DECISION{…}`, block-form `§/LIST{…}` / `§/DICT{…}` / `§/HSET{…}`) when converting C# to Calor. Block ends are now expressed purely through dedent, matching the canonical indent-only surface taught in [Phase 5 docs](/docs/) and accepted by the parser since [Phase 1](/docs/syntax-reference/structure-tags/). 165 conversion snapshots regenerated. Three closer forms are intentionally retained for follow-up design work: `§/DO{id} condition` (do-while carries the loop condition on its closer), `§/PP{COND}` (preprocessor blocks echo the condition for chained `#if/#else` readability), and `§/K` (match-case body delimiter).
- **Phase 3 parser hardening** — Class / interface members with empty bodies (constructors, methods, async methods, interface method signatures, properties, indexers, operator overloads, events) now terminate cleanly in indent form via a new `IsClassMemberOpener` / `TryExpectMemberBlockEnd(hasBodyContent)` helper pair. The `hasBodyContent` flag prevents the empty-body member from greedily consuming a dedent that actually belongs to the enclosing class / interface. `ParsePreprocessorDirective` now calls `ConsumeDedentBeforeChain(§PPE, §/PP)` so chained `#if / #else / #endif` blocks parse correctly when the if-branch had indented body content. `TestHelpers.CompileCalorToCSharp` in `Calor.Conversion.Tests` was migrated to `Lexer.TokenizeAllForParser()` to match the production CLI path; 118 previously-failing round-trip tests now pass (Conversion.Tests 280/280).
- **`Optional closing-tag IDs`** — Structural closing tags (`§/M`, `§/F`, `§/AF`, `§/L`, `§/I`, `§/TR`, `§/CL`, `§/IN`, `§/PR`, `§/MT`) may now omit the trailing `{id}` block. Both forms are accepted side-by-side; the parser pairs closers with their nearest matching opener by structural nesting. Openers continue to carry IDs as before.
- **`calor fix --drop-structural-ids <root>`** — Bulk, mechanical, byte-reversible source rewriter that strips `{id}` from structural closing tags (and the leading `{id:…}` from openers when the rest can be preserved). Records every removal in a `migration.log.json` and supports `--revert --log <file>` to restore the original bytes exactly. Only touches values that look like production IDs (`prefix_payload` with a 12-char compact or 26-char ULID payload); short test IDs like `m001` are left alone. See [`docs/cli/fix.md`](docs/cli/fix.md).
- **`Calor0820 LegacyStructuralId`** — Opt-in lint that flags closing tags still carrying a production-ID payload, with a `fix` patch that points at `calor fix --drop-structural-ids`.
- **`BytePreservationVerifier`** — Migration utility that verifies a rewrite plus its revert reproduces the original file byte-for-byte. Used by the integration tests for `calor fix`.

### Changed
- **`CalorEmitter` block-end emission flows through `EmitBlockEnd(legacyCloser)` helper** — single chokepoint for the closer-vs-indent decision. The `legacyCloser` parameter is preserved at every call site so a future opt-in flag (or migration-mode emitter) can restore explicit closers without re-touching every visit method.

### Documentation
- New: `docs/cli/fix.md`.
- Updated: `docs/syntax-reference/structure-tags.md`, `docs/syntax-reference/index.md`, `docs/ids.md`, `docs/cli/index.md` reflect the optional closing-tag ID and the new `calor fix` command.
- **Phase 5 — Product docs migrated to indent-only syntax.** README, `docs/`, and `website/content/` now teach indent-form Calor as the canonical surface; closer-form (`§/F{id}`, `§/M{id}`, etc.) is mentioned only in legacy callouts that point at `calor fix` for migration. Touched 87 markdown/MDX files via `scripts/phase5_migrate_docs.py` (962 fenced code blocks scanned, 452 transformed, 46 MDX brace-corruption sites repaired) plus surgical hand-edits of prose sections (Quick Reference tables, Closing-Tag rows in control-flow / structure-tags, "Use closing tags" agent guidance in Claude / Codex / Gemini integration pages, Principles tables in philosophy docs). The 6 `tests/E2E/agent-tasks/fixtures/refactor-*-calor/CLAUDE.md` agent-prompt fixtures were also rewritten so the safe-refactoring benchmark teaches indent form when it next runs in CI.

### Known scoring debt (follow-up after Phase 4)
- The static heuristic metric calculators in `tests/Calor.Evaluation/Metrics/` (`ComprehensionCalculator`, `EditPrecisionCalculator`, `InformationDensityCalculator`, `RefactoringStabilityCalculator`) still reward closer-tag presence directly (e.g., `source.Contains("§/F{")` ⇒ +0.05). After Phase 4 subtractively removes closer-form support, these calculators (and their methodology / metric docs in `docs/benchmarking/` and `website/content/benchmarking/`) must be updated to score indent-form structure instead. The **agent-refactoring** benchmark is unaffected — it is pure compile-or-Z3 pass/fail and does not invoke the heuristic calculators.

## [0.5.0] - 2026-04-22

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Roslyn 5.3.0 upgrade** — Migration pipeline now uses Roslyn 5.3.0 (C# 14 support), enabling conversion of modern C# files using lambda parameter modifiers, `out` in lambda parameters, and other C# 13/14 features. Previously failed on files like Avalonia's `IFramebufferPlatformSurface.cs`.
- **`LanguageVersion.Preview` parse option** — The C# parser now accepts the broadest possible C# syntax, eliminating parse errors on cutting-edge C# code.

### Changed
- **Non-exhaustive match on `Option<T>` / `Result<T,E>` is now an error** (`Calor0500 NonExhaustiveMatch`, severity upgraded from Warning to Error for match statements). This is the TIER1C commitment from `docs/design/calor-direction.md` — exhaustive match on known sum types is mandatory syntax. The checker already identified these cases; this release makes them fail the build rather than pass with a warning. No repository `.calr` files were non-exhaustive on known sum types, so this upgrade is backward-compatible for existing code.
- **Microsoft.CodeAnalysis.CSharp** upgraded from 4.8.0 to 5.3.0 across all projects

## [0.4.9] - 2026-04-21

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins)
  - ErrorDetection: 1.83x (Calor wins)
  - RefactoringStability: 1.52x (Calor wins)
  - EditPrecision: 1.39x (Calor wins)
  - Correctness: 1.30x (Calor wins)
- **Programs Tested**: 207

### Added
- **Cross-assembly IL analysis** — Opt-in compile-time analysis that traces method calls through referenced .NET assemblies to discover effects not covered by manifests. Enabled via `<CalorEnableILAnalysis>true</CalorEnableILAnalysis>`. Handles async state machines, iterator methods, delegate creation (`ldftn`), and virtual dispatch. Three-state resolution ensures incomplete traces never report false purity. Benchmark: 2.8ms median for 8 call sites across 2 assemblies, 100% resolution rate on concrete call chains (6 resolved with effects, 2 pure, 0 incomplete). See [Cross-Assembly IL Analysis guide](/guides/il-analysis/).
- **IL analysis validation benchmark** — `bench/ILAnalysisBench/` measures assembly index construction, full analysis time, and per-call-site resolution results
- **28 IL analysis tests** covering assembly loading, call graph extraction, async/iterator state machines, virtual dispatch, delegate edges, method identity, soundness guarantees, and end-to-end integration
- **Cross-assembly IL analysis guide** — New website page documenting when to enable IL analysis, what it finds and doesn't, performance characteristics, and relationship to manifests
- **Cross-module effect propagation** — Multi-file Calor projects now enforce effect contracts across file boundaries. When a caller invokes a public function defined in another module (bare-name `§C{SaveOrder}` or qualified `§C{OrderService.SaveOrder}`), the caller's `§E{...}` must cover the callee's declared effects. Violations emit `Calor0410` with cross-module context; public functions without `§E` emit the new `Calor0417` warning.
- **Multi-file CLI** — `calor --input a.calr --input b.calr` compiles multiple files and runs the cross-module pass. Single-file usage is unchanged. `--output` is rejected when multiple inputs are passed (outputs are written alongside each input).
- **MSBuild cross-module enforcement** — `CompileCalor` task automatically runs the cross-module pass over every `.calr` file in the project. No new configuration required.
- **Persistent effect summary cache** — Each module's public function declarations, internal name table, and per-caller call-site listings are persisted in the build cache (`BuildState` format bumped to v2.0). Warm builds retain complete cross-module enforcement by combining fresh summaries (recompiled files) with cached summaries (incrementally-skipped files) — no re-parsing needed.
- **`CrossModuleEffectRegistry`** and **`CrossModuleEffectEnforcementPass`** — New enforcement components with AST-based and summary-based overloads. Declared-effects-as-contract model, one-hop-per-boundary enforcement, registry priority over supplemental manifests.
- **`ExternalCallCollector.CollectPerFunctionWithBareNames`** — New per-function mode retains bare-name call targets (previously dropped) for cross-module resolution.
- **34 new cross-module enforcement tests** — 24 unit tests (registry/pass behavior + null-guard + 500-module stress test) + 5 MSBuild integration tests + 3 CLI subprocess tests + 2 cache round-trip/migration tests.
- **[Cross-Module Effect Propagation guide](/guides/cross-module-effect-propagation/)** — Contract model, bare-name vs. qualified calls, ambiguity handling, warm-build semantics, CLI + MSBuild integration, troubleshooting.

### Changed
- **`--input` option** in the `calor` CLI now accepts multiple values (`Option<FileInfo[]>` with `ArgumentArity.OneOrMore`).
- **Build state cache format** bumped from `1.0` to `2.0` — existing caches are automatically invalidated on first build after upgrade.
- **Options hash includes `EffectKind` enum shape** — any future addition, removal, or rename of an `EffectKind` value automatically invalidates the build cache on the next build. Prevents stale summaries from silently dropping effects that a compiler upgrade re-categorized.

## [0.4.8] - 2026-04-20

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Incremental compilation** — `CompileCalor` MSBuild task now owns all incremental logic with a two-level cache gate: (mtime, size) stat check then SHA256 content hash. Global invalidation on compiler DLL, options, effect manifest, or output directory changes. Compile failures delete prior `.g.cs` and skip caching to ensure correctness.
- **`calor effects suggest` CLI command** — Analyzes Calor source files and generates a `.calor-effects.suggested.json` manifest template for unresolved external calls. Supports `--json` for agent consumption, `--merge` for additive updates to existing manifests. Uses AST-based collection (not diagnostic parsing) with internal function filtering, variable type resolution, and call kind tagging.
- **Shared `ExternalCallCollector`** — Extracted from `InteropEffectCoverageCalculator`, extended to walk class methods and constructors (was functions only). Resolves variable types via `§NEW` initializer scanning.
- **Incremental build benchmark** — `bench/IncrementalBuildBench/` measures cold, warm (no changes), and warm (1 file changed) build times
- **Effect manifests .NET ecosystem guide** — New website page documenting ~170 covered types, resolution mechanics, custom manifest authoring, and CLI tools
- **Website changelog page** and **WhatsNewBanner** component for landing page release highlights
- 15 new suggest tests + BuildStateCache and CompileCalor integration tests

## [0.4.7] - 2026-04-20

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Static analysis for class members** — The `--analyze` flag now examines methods, constructors, property accessors, operators, indexers, and event accessors (previously only top-level functions were analyzed)
- **Verification-gated reporting** — `--analyze` only reports proven findings by default (Z3-confirmed or constant analysis); use `--all-findings` for lower-confidence results
- **Taint hop-count tracking** — Taint analysis tracks propagation steps; single-hop parameter-to-sink flows filtered by default to reduce false positives
- **Bug pattern detection in class members** — Division by zero, null dereference, integer overflow, off-by-one, path traversal, command injection, and SQL injection detection now covers all class member bodies
- **ScopeRestorer RAII pattern** — Eliminates scope corruption risk from 14+ manual try/finally blocks in the Binder
- **Arity-aware overload resolution** — `Scope.LookupByArity` resolves correct overload by argument count, preventing wrong return types from flowing into Z3
- **Static context enforcement** — `this` expression not bindable in static methods and operators
- **Nested class scope isolation** — Inner classes don't inherit outer class fields
- **Constructor initializer binding** — `: base()`/`: this()` arguments visible to bug pattern checkers
- **BoundConditionalExpression** — Ternary expressions preserve all three branches for analysis (was returning only the true branch)
- **33 new unit tests** for class member binding, scope, overloads, dataflow, and end-to-end analysis
- **New `--all-findings` CLI flag** for showing all analysis findings including inconclusive results
- **New documentation page** (`/cli/static-analysis/`) documenting the analysis pipeline, finding types, and real-world results

### Fixed
- **False positive elimination** — Unhandled expression types (cast, array length, indexer, etc.) return opaque expressions instead of `BoundIntLiteral(0)`, eliminating the entire class of false division-by-zero reports
- **DEC literal misparse** — Decimal literals (`DEC:100`) now bind to `BoundFloatLiteral` instead of falling to zero-literal fallback
- **Assignment LHS not counted as use** — `x = 1` no longer reports `x` as "used before write" in dataflow analysis
- **Multi-statement sync blocks** — Lock bodies now preserved for analysis (was dropping all statements)
- **this.field shadowing** — `this.field` resolves from class scope, not method scope (prevents parameter shadowing field)
- **Throw-to-catch CFG edges** — Throw statements inside try blocks now flow to catch blocks instead of function exit
- **Using exception path** — Using statements modeled as try/finally with dispose on exception path
- **DeclaredEffects pass-through** — `VerificationAnalysisPass` now passes function effects to `TaintAnalysis` (was missing)

### Validated
- **47 open-source projects scanned** — 23 verified findings across 8 projects, 27 projects clean (zero findings), ~90% true positive rate
- **Real findings**: ILSpy null dereferences, FluentFTP path traversal, ASP.NET Core path traversal, Mapster unsafe unwraps, Avalonia nullable unwraps

## [0.4.6] - 2026-04-18

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Effect system: .NET framework manifests** — Tier B effect manifests for 30+ common .NET framework interfaces (ILogger, DbContext, IConfiguration, HttpClient, ControllerBase, etc.) enabling the compiler to enforce correct effect declarations when Calor code calls framework types
- **Effect system: ecosystem library manifests** — Manifests for Serilog, Newtonsoft.Json, Dapper, MediatR, AutoMapper, FluentValidation, Polly
- **Effect system: BCL manifest expansion** — New manifests for System.Text.Json, Regex, Concurrent collections, Crypto types
- **Effect system: variable type resolution** — Enforcement pass resolves instance method calls via §NEW initializer tracking (e.g., §B{r} §NEW{Random} → r.Next resolves to rand)
- **Effect system: structured type info** — BoundCallExpression now carries ResolvedTypeName and ResolvedMethodName from the binder
- **Effect system: centralized type mapping** — MapShortTypeNameToFullName with 65+ type name mappings across BCL, framework, and ecosystem types
- 95 new enforcement tests (210 total)

### Fixed
- **Effect system: unified resolver** — Consolidated three parallel effect systems (BuiltInEffects, EffectsCatalog, EffectChecker.KnownEffects) into a single manifest-based resolver
- **Parser: compound effect codes** — Fixed §E{db:r,cw,env:r} silently mis-parsing the third compound code when colon-delimited effects are chained with commas
- **EffectCodes.ToCompact: missing mappings** — Added environment_read→env:r, database_write→db:w, heap_write→mut and other internal-to-surface code conversions
- **Enforcement: collection mutations** — Added CollectionPushNode, DictionaryPutNode, CollectionRemoveNode, etc. to the enforcement pass (→ mut effect)
- **Converter: effect declaration format** — Fixed converter emitting internal values (environment_read) instead of surface codes (env:r) in §E declarations

### Removed
- `BuiltInEffects.cs` — ~204 hardcoded entries migrated to manifest JSON files
- `EffectsCatalog.cs` — Intermediate layer removed; EffectResolver handles all resolution
- `EffectChecker` class — Legacy checker replaced by EffectEnforcementPass; shared types moved to EffectTypes.cs

## [0.4.5] - 2026-04-14

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Phase 11-12 — 49→0 conversion failures** — Achieved 100% compilation success across 38,932 .calr files (13,831 roslyn + 25,101 dotnet). Key fixes: iterative or-pattern parsing for stack overflow prevention, lambda multi-line format for FallbackCommentNode, §CS{} raw C# fallback for unconvertible call targets, HasEndNewBeforeEndCall nesting depth tracking, missing Lisp expression tokens, PLIST REST attribute consumption, TypeMapper array bracket normalization, hex→decimal integer emission, literal keyword escaping, empty array conversion, tuple support in Lisp arguments, PascalCase operator recovery, positional type patterns, bracket depth tracking in ParseValue, dotted reference raw call handling

## [0.4.4] - 2026-04-10

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Phase 6A — WHERE constraints** — Normalize multiline Roslyn type names in WHERE constraints; add `?`, `*`, `[]`, `.` handling in ParseValue generic loops; strip `@` from ANON/NEW property names; strip `!` null-forgiving from target strings; sanitize backtick from module names
- **Phase 6B+C — WHERE dot-nested** — Handle `Type<T>.NestedType` in WHERE constraint parser; fix ANON implicit property names; strip `global::` from enum values; add HSET hoisting
- **Phase 6D — ulong literals** — Add ulong fallback for integers > long.MaxValue; fix `§VAR{}` detection in tuple pattern arms
- **Phase 6E — array ID mismatch** — Empty arrays emit with explicit size 0; fix match expression multi-line indentation; simplify `delegate*` types to `nint` in attribute blocks

## [0.4.3] - 2026-04-08

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **51-project conversion campaign** — Phase 3-5 fixes addressing ~80 additional conversion failures across array ID mismatches, dictionary hoisting, enum parsing, empty method bodies, generic calls in statement position, and §ARR2D dimension hoisting
- **Enum cast/paren ambiguity** — Parenthesized hex enum values like `(0x0001)` no longer misinterpreted as type casts
- **Collection nodes in match arms** — List, dictionary, and set creation in switch expression arms now use block syntax
- **Call statement argument hoisting** — Complex arguments with section markers are hoisted in statement-level calls

## [0.4.2] - 2026-04-02

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Changes

## [0.4.1] - 2026-03-15

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **List pattern conversion** — Full C# → Calor round-trip support for list/slice patterns (`[var a, ..var rest, var b]`) with correct slice position tracking (start/middle/end) via `SliceIndex` on `ListPatternNode`
- **UTF-8 string literals** — Round-trip support for `"hello"u8` via `IsUtf8` property on `StringLiteralNode`, with lexer/parser/emitter changes

### Fixed
- **Slice position correctness** — `[var first, .., var last]` now correctly preserves the slice position instead of always appending at end; bare `..` emits without spurious `var _` binding
- **Unknown feature default** — `FeatureSupport.GetSupportLevel` now returns `NotSupported` for unregistered features, preventing silent suppression of blockers in `MigrationAnalyzer`
- **PostConversionFixer CRLF handling** — Orphaned closing tag regex now handles Windows `\r\n` line endings correctly
- **SelfTest span offset consistency** — Input line endings normalized before compilation so span offsets match golden files across platforms
- **ClaudeInitializer test isolation** — All test instances now use `ClaudeJsonPathOverride` to prevent race conditions writing to `~/.claude.json`
- **Parser u8 stripping** — Defensive stripping of `u8` suffix from string literal values if lexer includes it

## [0.4.0] - 2026-03-09

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Copilot instructions** — Ported Claude `/confidence` and `/create-release` skills to `.github/instructions/` for GitHub Copilot compatibility
- **MCP cancellation token propagation** — All MCP tool `ExecuteAsync` methods now accept and propagate `CancellationToken` for proper request cancellation

### Fixed
- **§ERR fallback emission** — Unsupported C# constructs now emit parseable `§ERR "TODO: ..."` tokens instead of unparseable `§ERR{...}` brace format
- **Named argument round-trip** — Named arguments in converter output now use correct `name: value` syntax that parses back cleanly
- **Unicode escape sequences** — `\Uxxxxxxxx` 8-digit Unicode escapes now handled correctly in string literals
- **Ternary decomposition** — Ternary expressions (`a ? b : c`) now decompose to `§IF` expression form instead of statement form, fixing 26+ Calor0104 errors across real-world codebases
- **Doc comment carriage return leaks** — `\r` characters stripped from XML doc comments during conversion, preventing broken `//` comment prefixes
- **§ markers in Lisp expressions** — Binary/unary operations with §-containing operands (calls, ternaries) now hoist to temp vars, preventing Calor0114 parse errors inside `(op arg1 arg2)` expressions
- **Empty §ASSIGN for collections** — Collection creation (List, Dict, Set, Array) as assignment RHS now emits the collection block with the target name directly, instead of empty `§ASSIGN` statements

### Converter Quality Improvements
- **Newtonsoft.Json**: 54.0% → **100%** compile rate (240 files)
- **Humanizer**: 86.1% → **99%** compile rate (100-file sample)
- **PowerShell**: All 14 reported blockers resolved; 200-file sample at **100%** clean conversion

## [0.3.8] - 2026-03-05

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **§SYNC lock statement** — Native `§SYNC{id} (expr) ... §/SYNC{id}` syntax compiling to `lock(expr) { body }` with full round-trip support; follows complete AST node checklist (token, lexer, AST, 5 visitors, parser, converter)
- **Verbatim identifier mapping** — C# `@keyword` identifiers mapped to backtick syntax (`` `keyword` ``) in Calor via `EscapeCalorIdentifier` helper at 8+ emission points; round-trips correctly to `@keyword` in C# output
- **Conditional usings in §PP** — `§U` directives inside `§PP` preprocessor blocks at module level; `TypePreprocessorBlockNode` extended with `Usings` property for both active and disabled preprocessor branches
- **MCP tool consolidation** — 34 MCP tools consolidated to 13 focused tools (`calor_help`, `calor_navigate`, `calor_structure`, `calor_check`, `calor_fix`, `calor_migrate`, `calor_refine`, `calor_batch`); improves discoverability and reduces tool selection confusion
- **`calor_fix` auto-repair tool** — New MCP tool that diagnoses and auto-applies fixes for common Calor compilation errors
- **`calor_migrate` tool** — Unified migration workflow combining convert, validate, and fix in a single tool
- **Primary constructor synthesis** — Primary constructor parameters converted to fields with proper constructor initialization
- **Tuple type and expression parsing** — Full support for C# tuple types `(int, string)` and tuple literals in converter
- **Event accessor bodies** — `add`/`remove` accessor bodies in event definitions now converted and emitted correctly
- **Nested delegate support** — `§DEL` delegate definitions inside class bodies
- **Goto case/default** — `goto case` and `goto default` converted to `§GOTO{CASE:value}` / `§GOTO{DEFAULT}` with documentation in MCP

### Fixed
- **String interpolation lexing** — Brace-depth tracking prevents premature close on `{` inside interpolated strings
- **Null coalescing operator** — `??` operator properly supported in converter and emitter
- **Null-conditional access** — `?.` chains correctly decomposed during conversion
- **Nullable lambda parameters** — `Func<int?>` and nullable types in lambda signatures emit correctly
- **Unsigned numeric literals** — `0u`, `0UL` etc. parsed and emitted correctly
- **Operator precedence** — Fixed parenthesization in complex expressions during conversion
- **Target-typed new** — `new()` infers type from context instead of emitting `NEW{object}`
- **MCP memory pressure** — Wait-and-retry with backoff instead of immediate rejection; concurrency scaled with CPU count
- **Feature discoverability** — MCP tool output now includes feature support status and workarounds inline

## [0.3.7] - 2026-03-02

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **String interpolation with embedded calls** — CalorEmitter now uses function-call syntax inside `${...}` instead of `§C` tags which were treated as literal text by the parser; fixes 29-file Humanizer conversion blocker
- **OperatorOverloadNode parameter emission** — CSharpEmitter now uses `Visit(p)` for operator overload parameters, preserving ref/out/in/params modifiers
- **Interpolation-safe expression emission** — `NewExpression`, `AwaitExpression`, and `ArrayAccess` now emit C#-style syntax inside `${...}` interpolation contexts instead of `§`-prefixed section markers

### Added
- **Batch conversion validation** — `calor_batch_convert` MCP tool now supports `validate` parameter that parses and compiles each converted file, catching false-positive successes
- **C#-to-Calor conversion guide** — Skills documentation now includes common conversion patterns (interpolation, ternary, ref/out, chained calls) for agent guidance
- **Ternary expression syntax entry** — `calor-syntax-documentation.json` now includes `(? condition trueValue falseValue)` with examples
- **3 new conversion test snapshots** — InterpolationWithMethodCall (12-01, round-trip verified), RefOutParameters (12-02), OperatorOverloadWithModifiers (12-03, round-trip verified)

## [0.3.6] - 2026-03-01

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Fixed
- **Complete C# keyword sanitization** — `SanitizeIdentifier()` expanded from 27 to 50+ reserved keywords (`as`, `is`, `in`, `event`, `lock`, `delegate`, `checked`, `yield`, `out`, `ref`, `volatile`, `abstract`, `override`, `sealed`, `virtual`, `async`, `await`, `typeof`, `sizeof`, `unchecked`, `unsafe`, `fixed`, `foreach`, `goto`, `throw`, `try`, `catch`, `finally`, `explicit`, `implicit`, `extern`, `operator`, `params`, `readonly`, `stackalloc`, `const`, `var`, `dynamic`, `nameof`, `when`); prevents invalid C# in 5–15% of converted files
- **Call expression leading dot** — `§C{.Method}` now correctly emits `this.Method()` instead of invalid `.Method()`
- **Converter module ID consistency** — Module ID is always `m001` instead of inconsistent IDs like `m044` caused by shared counter increment during child node conversion
- **Interop block namespace duplication** — Use `ToString()` instead of `ToFullString()` for nodes inside namespaces to prevent namespace trivia bleeding into interop blocks
- **Switch enum value prefix** — Heuristic to detect enum type from qualified case labels and qualify bare identifiers (from `using static`) in switch expressions and statements

### Added
- **Batch convert chunking** — `calor_batch_convert` MCP tool now supports `maxFiles`, `offset`, `directoryFilter`, and `skipConverted` parameters for converting large projects in manageable chunks
- **Compile tool batch mode** — `calor_compile` MCP tool now accepts `files` (string array) and `projectPath` (directory) for batch compilation in a single call instead of 200+ individual calls
- **Diagnose tool auto-apply** — `calor_diagnose` MCP tool now supports `apply` parameter to automatically apply fix edits and return `fixedSource` alongside diagnostics, eliminating one round-trip per diagnostic cycle
- **CSharp minimize tool** — New `calor_csharp_minimize` MCP tool analyzes `§CSHARP` interop blocks and suggests which constructs could be native Calor, using Roslyn parsing and FeatureSupport registry cross-reference
- **Volatile keyword support** — `volatile` modifier is now fully supported for fields: `MethodModifiers.Volatile` flag, converter detection, parser recognition (`volatile`/`vol`), emitter output; `FeatureSupport` updated from `NotSupported` to `Full`

## [0.3.5] - 2026-02-27

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Relational/compound `is` patterns** — `x is > 5 or < 3` now converts to equivalent boolean expressions `(|| (> x 5) (< x 3))` instead of falling back to §CSHARP; supports `and`, `or`, `not`, parenthesized, and nested pattern combinations
- **Type-level preprocessor blocks** — `#if`-wrapped entire type declarations (class, interface, enum) at module level are now converted to `§PP` blocks; handles disabled branches where Roslyn excludes types from the syntax tree
- **Enum visibility modifiers** — Enums now support `public`, `internal`, `private`, `protected` visibility via `§EN{id:Name:vis}` syntax instead of hardcoded `public`
- **Nested type declarations** — Classes, structs, records, interfaces, and enums defined inside other types are now parsed, converted, and emitted correctly
- **Extended dictionary initializer support** — `SortedDictionary`, `ConcurrentDictionary`, `FrozenDictionary`, `ImmutableDictionary`, and `ImmutableSortedDictionary` now use the same initializer conversion as `Dictionary`
- **5 new conversion snapshot tests** — Relational patterns, internal enums, nested types, preprocessor-wrapped types, and dictionary initializers
- **Feature registry entries** — Added `dictionary-initializer`, `list-initializer`, `hashset-initializer`, `nested-type` to FeatureSupport; updated `relational-pattern` and `compound-pattern` from NotSupported to Full

## [0.3.4] - 2026-02-26

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **Preprocessor directive conversion** — C# `#if`/`#elif`/`#else`/`#endif` directives are now converted to Calor `§PP` blocks by extracting Roslyn trivia; handles nested `#if` and `#elif` chains as nested `§PP` nodes
- **MCP preprocessor discoverability** — `FeatureSupport` entry, `SyntaxHelpTool` aliases, `AGENTS.md` template row, and `calor-language-skills.md` section for `§PP`
- **Refinement types** — `§RTYPE{id:Name:base} (predicate)` for named refinement types, inline `§I{type:param} | (predicate)` on parameters, `§PROOF{id:desc} (expr)` for proof obligations, and `#` self-reference in predicates
- **Obligation engine** — Z3-powered verification pipeline: obligation generation, assume-negate-check solving, guard discovery, and configurable policies (default, strict, permissive)
- **5 MCP agent guidance tools** — `calor_obligations` (verify obligations), `calor_suggest_types` (detect parameters needing refinements), `calor_discover_guards` (Z3-validated fix suggestions), `calor_suggest_fixes` (ranked fix strategies), `calor_diagnose_refinement` (all-in-one repair loop)
- **Obligation policy** — Configurable per-status actions (Ignore, WarnOnly, WarnAndGuard, AlwaysGuard, Error) with three built-in policies
- **101 new tests** — Refinement type parsing, obligation solving, guard discovery, MCP tool integration, and Z3 self-reference resolution

### Fixed
- **Lock/checked body ordering** — Comment annotations now correctly appear before body statements instead of after
- **Non-standard for-loop fallback** — Multi-variable declarations and expression initializers now emit in correct order; multi-incrementor patterns detected as non-standard

## [0.3.3] - 2026-02-25

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.34x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins, large effect d=2.36)
  - ErrorDetection: 1.83x (Calor wins, large effect d=2.02)
  - RefactoringStability: 1.52x (Calor wins, large effect d=10.09)
  - EditPrecision: 1.39x (Calor wins, large effect d=4.91)
  - Correctness: 1.30x (Calor wins, large effect d=1.38)
- **Programs Tested**: 207

### Added
- **PostConversionFixer** — Auto-fix 6 known invalid converter output patterns: orphaned closing tags, unmatched parentheses, comma leaks, generic `<T>` in Lisp position, inline `§ERR`/`§LAM` extraction, missing IF `→` arrow (#474)
- **`calor_convert_validated` MCP tool** — Single-call pipeline chaining convert → auto-fix → diagnose → compat-check with stage-based error reporting (#474)
- **Blocker classification** — `calor_analyze_convertibility` now classifies blockers as `language_unsupported` vs `converter_not_implemented` with summary counts (#474)
- **Complex composed examples** — 5 real-world examples in calor-language-skills.md (3 generated by the converter from real C# input, all parser-validated) (#474)

### Fixed
- **CommaLeaks false-positive** — Fix regex that was stripping commas from inline signatures, breaking valid converter output (#474)
- **Converter auto-fix integration** — ConvertTool now attempts PostConversionFixer before reporting parse errors, recovering from known converter bugs (#474)

## [0.3.2] - 2026-02-24

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.31x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 2.22x (Calor wins)
  - ErrorDetection: 1.83x (Calor wins)
  - EditPrecision: 1.39x (Calor wins)
  - RefactoringStability: 1.30x (Calor wins)
  - Correctness: 1.30x (Calor wins)
- **Programs Tested**: 207

### Added
- **Compact syntax Phase 1** — Auto-props, optional IDs, and inline signatures reduce Calor boilerplate (#445)
- **Default parameter values** — Emit and parse default parameter values in Calor syntax (#460)
- **6 language gap features** — Address 6 C# constructs from tracking issue #325: unsafe/fixed/stackalloc blocks, tuple types, multi-dimensional arrays, Parallel LINQ, COM interop fallback, Span<T> (#457)
- **Bitwise attribute expressions** — Full support for bitwise OR (`|`), AND (`&`), complement (`~`), and parenthesized expressions in attribute arguments (#449, #453)
- **Expanded benchmark suite** — Grow from 40 to 207 programs across 14 categories (#452)
- **Return type inference for `new()`** — Infer target type for `new()` in local functions and async methods (#466)
- **EdgeCaseCoverageAnalyzer** — New analyzer for edge case coverage and correctness estimation (#442)
- **MCP tools for edit precision** — Add call graph analysis tools for refactoring impact (#446)
- **2.0x comprehension ratio** — Proportional metrics and LLM evaluation reach 2.0x AI comprehension ratio (#447)

### Fixed
- **Ternary throw hoisting** — Hoist ternary throw expressions to guard statements (#459)
- **Option<T>/Result<T,E> converter** — Per-member fallback for `ConvertStruct` and `InferTargetType` for return context (#458)
- **Null-coalescing throw** — Convert `?? throw` to if-null-throw guard instead of `§ERR` (#451)
- **Non-throwable literal wrapping** — Wrap non-throwable literals in `System.Exception` for `§TH` codegen (#450)
- **CalorFormatter coverage** — Handle all 23 missing expression types in `FormatExpression` (#464)
- **Self-referential runtime reference** — Prevent `Calor.Runtime` from referencing itself; document dotted module names (#463)
- **Dotted-name round-trip** — Document and test dotted-name round-trip behavior (#462)
- **Constructor overloading** — Close Challenge 8; constructor overloading was already supported (#456)
- **Benchmark structure scoring** — Remove artificial parameters dependency in `CalculateCalorStructureScore` (#454)
- **License attribution** — Fix website footer to show Apache 2.0 instead of MIT (#443)

## [0.3.1] - 2026-02-23

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.27x (Calor leads)
- **Metrics**: Calor wins 7, C# wins 1
- **Highlights**:
  - Comprehension: 1.70x (Calor wins, large effect d=1.39)
  - ErrorDetection: 1.65x (Calor wins, large effect d=1.62)
  - EditPrecision: 1.37x (Calor wins, large effect d=11.80)
  - RefactoringStability: 1.37x (Calor wins, large effect d=4.36)
  - Correctness: 1.26x (Calor wins, large effect d=1.06)
- **Programs Tested**: 40

### Added
- **Proportional comprehension scoring** — Replace boolean presence checks with log2 diminishing returns formula; files with more contracts/effects now score proportionally higher
- **Contract-depth and effect-specificity scoring** — Bonus for pre+post contract completeness, effect specificity (comma-separated effects), and matched open/close ID pairs
- **LLM-based comprehension evaluation** — Claude API integration with LLM-as-judge scoring via `--llm` flag; loads curated questions, falls back to structural generation
- **`calor_explain_error` MCP tool** — Matches compiler errors to 10 common mistake patterns with fix examples and correct syntax
- **DiagnoseTool error guidance** — Enriches diagnostics with `commonMistake` field when compiler has no specific fix suggestion
- **Expanded question bank** — 105 comprehension questions across all 36 benchmark programs (up from 13 across 4)
- **Pre-compiled regexes** — All comprehension scoring regexes compiled at class load time for 250+ program scalability
- **CI LLM comprehension workflow** — GitHub Actions step runs LLM evaluation with `ANTHROPIC_API_KEY` secret

## [0.3.0] - 2026-02-22

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.20x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - ErrorDetection: 1.65x (Calor wins, large effect)
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **C# interop blocks** — `§CSHARP{...}§/CSHARP` syntax for embedding raw C# at module/class scope, enabling incremental migration of unsupported constructs
- **Interop conversion mode** — Converter wraps unsupported members in `§CSHARP` blocks instead of TODO comments, producing `.calr` files that round-trip to valid C#
- **Convertibility analysis tool** — `calor_analyze_convertibility` MCP tool and `calor analyze-convertibility` CLI command for assessing C# file migration readiness
- **Round-trip test harness** — Automated C# → Calor → C# pipeline with test result comparison for validating conversion fidelity
- **Bug detection improvements** — Off-by-one checker and precondition suggester for enhanced static analysis
- **Contract inference pass** — Automatic inference of contracts from code patterns
- **Migrate workflow enhancements** — Analyze and verify phases added to `calor migrate` command
- **Syntax help telemetry** — Track which syntax features agents query most to prioritize documentation

### Fixed
- **Agent benchmark docs** — Improved CLAUDE.md syntax reference fixing 12 failing benchmark tasks across 8 categories (86.5% → 100% pass rate): while loops, switch/pattern matching, events, implication operator (`->` not `implies`), async return types, StringBuilder operators, block lambdas, multi-effect declarations
- **async-004 task prompt** — Fixed misleading "network read effect" to "network effect" (HttpClient needs `net:rw`)

## [0.2.9] - 2026-02-21

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Unsupported feature telemetry** — Track unsupported C# constructs (goto, unsafe, etc.) in Application Insights during conversion, enabling data-driven prioritization of converter improvements
- **Pattern combinators** — `not`, `or`, and `and` pattern combinators and negated type patterns in C# converter
- **Collection spread-only conversion** — Spread expressions and fluent chain-on-new hoisting in converter
- **Required modifier and partial methods** — Support for `required` property modifier and partial method declarations
- **Delegate emission** — Delegate types, parameter attributes, and generic interface overloads in converter
- **Named arguments and tuple literals** — Named arguments, tuple literals, getter-only properties, and verbatim strings
- **Primary constructor parameters** — C# 12 primary constructors converted to readonly fields
- **`notnull` generic constraint** — Support for `notnull` constraint and static lambda conversion
- **Permissive effect inference** — New mode for converted code to avoid strict effect enforcement on generated output

### Fixed
- **Converter**: null-coalescing `??` → conditional (not arithmetic), declaration pattern variable binding, `out var` support, method groups, explicit interface implementations, target-typed new inference, cast-then-call chains, `protected internal`, `unchecked` blocks, default parameters, chained assignments, `typeof`, `lock`, lambda assignment, expression-bodied constructors, `int.MaxValue`, `ValueTask`, empty `[]`, static properties
- **Diagnostics**: Broke monolithic `Calor0100` (UnexpectedToken) into 6 specific error codes for clearer error messages
- **Parser**: `§HAS`/`§IDX`/`§LEN`/`§CNT` inside lisp expressions, tuple deconstruction, generic static access, variance modifiers, interface type params
- **Converter hoisting**: Chain bindings hoisted before `if` conditions, `§NEW` args hoisted to temp vars

## [0.2.8] - 2026-02-21

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **C# to Calor Conversion Campaign** — Converted 30 C# sample projects, producing 54 recommendations and 18 merged fixes
- **Cross-class method call effect inference** — Dotted call targets like `_calculator.Add` now resolve to internal functions for effect propagation, with name collision detection via multi-map
- **Local function support in converter** — C# local functions are hoisted to module-level `§F` functions during conversion
- **`§HAS`/`§IDX`/`§CNT`/`§LEN` inside lisp expressions** — Collection operations can now appear as arguments in prefix expressions like `(+ val §IDX arr 1)`
- **LINQ extension method effect recognition** — Common LINQ methods (Where, Select, OrderBy, ToList, etc.) recognized as pure in effect system
- **Async I/O and Math functions in effect catalog** — `TextWriter.WriteLineAsync`, `StreamReader.ReadLineAsync`, `Math.Floor/Clamp/Sin/Round/Log` added to known effects
- **`§PROP` inside `§IFACE`** — Interface properties now emit correctly instead of being treated as methods
- **Tuple deconstruction conversion** — `(_a, _b) = (x, y)` converts to individual `§ASSIGN` statements
- **Line comment and char literal support in lexer** — `//` comments and single-quoted char literals no longer crash the lexer

### Fixed
- **Emitter**: `default:` instead of `case _:` for wildcard switch, read-only properties emit `{ get; }`, `@` prefix removed from `this`/`base`/keywords, namespace dots preserved in type names, decimal type bind attribute parsing
- **Converter**: `nameof()` → string literal, `string.Empty` → `""`, postfix/prefix increment → `§ASSIGN (+ var 1)`, `§MT` instead of `§SIG` for interface methods, `§FLD` instead of `§DICT`/`§LIST` for collection fields, `@`-prefixed C# identifiers stripped

## [0.2.7] - 2026-02-19

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Class-level visibility preservation** — `internal class Program` no longer round-trips to `public class Program`; visibility flows through the full AST→converter→parser→emitter pipeline
- **Effect inference in converter** — The C#→Calor converter now auto-infers side effects from method bodies (e.g., `Console.WriteLine` → `§E{cw}`, `throw` → `§E{throw}`) instead of requiring manual annotation
- **Shared EffectCodes utility** — `EffectCodes.ToCompact()` centralizes effect category/value → compact code mapping
- **LINQ query syntax support** — `from`/`where`/`select`/`orderby`/`group by`/`join` expressions
- **LINQ method chain decomposition** — Chains like `.Where().Select().ToList()` are decomposed into sequential Calor statements
- **Type operators** — `is`, `as`, `cast` type checking and conversion operators
- **7 missing language features** — decimal literals, array/object initializers, anonymous types, extension methods, yield return, partial classes, operator overloads
- **`§USE` syntax** — New using directive format with `--validate-codegen` flag
- **`CalorCompilerOverride` MSBuild property** — Override compiler path in build
- **`calor self-test` CLI command** — Automated compiler self-test via CLI and MCP tool

### Fixed
- **Converter fidelity** — const arrays, built-in method chains, mutable binding `~` prefix, bare array initializers, multi-element `§ARR` arrays, float literal decimal points, complex string interpolation expressions
- **Effect enforcement** — Resolved `§F` vs `§MT` inconsistency for LINQ calls and method-level effect checking
- **Code generation** — struct support, static fields, global namespace, increment/decrement operators, class inheritance, static class modifier, readonly struct identity, operator overloads, `§IDX` codegen, generics in inheritance, attribute unquoting, `#nullable enable`
- **Parser/emitter** — `§EACH` index support, `§CAST` error improvements, partial class modifier emission, stale static class comment, double-slash error message, `§EACH` syntax docs
- **Init/tooling** — `.proj` file support, git root resolution for MCP, atomic writes for `~/.claude.json`, MCP tools in agent templates

## [0.2.6] - 2026-02-18

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **MCP server documentation** - Comprehensive documentation for `calor mcp` command with all 19 tools
- **LSP-style MCP navigation tools** - `calor_goto_definition`, `calor_find_references`, `calor_symbol_info`, `calor_document_outline`, `calor_find_symbol`
- **Semantic analysis MCP tools** - `calor_typecheck` for type checking with error categorization, `calor_verify_contracts` for Z3 contract verification

### Fixed
- MCP server now writes configuration to `~/.claude.json` per-project section instead of `.mcp.json`
- MCP server uses newline-delimited JSON (NDJSON) instead of Content-Length framing

## [0.2.5] - 2026-02-17

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.18x (Calor leads)
- **Metrics**: Calor wins 6, C# wins 2
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
- **Programs Tested**: 40

### Added
- **Ask Calor GPT integration** - Custom GPT link added to website header, footer, and dedicated homepage section with analytics tracking
- **MCP Server tools** - New `calor_assess` tool for C# migration analysis, plus `lint`, `format`, `diagnose`, and `ids` tools for AI agent integration
- **Hero section update** - New video and messaging on website homepage

### Fixed
- CI workflow: removed weekly schedule trigger, now runs all benchmarks on release with human-readable metric names
- Website: tied benchmark results now display with salmon color for clarity
- Evaluation: removed bias in effect discipline and correctness benchmarks
- Evaluation: consolidated benchmark metrics and integrated Safety/EffectDiscipline

## [0.2.4] - 2026-02-16

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 1.07x (Calor leads)
- **Metrics**: Calor wins 8, C# wins 4
- **Highlights**:
  - Comprehension: 1.55x (Calor wins, large effect)
  - EditPrecision: 1.37x (Calor wins, large effect)
  - RefactoringStability: 1.37x (Calor wins, large effect)
  - ErrorDetection: 1.24x (Calor wins, large effect)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-only features (C# has no equivalent)
- **Programs Tested**: 40

### Added
- Effect Discipline benchmark measuring side effect management (40 tasks across 4 categories)
- Safety benchmark measuring contract enforcement quality

### Fixed
- Fixed array type conversion in benchmark test harness (JSON deserializes as `object[]` but methods need typed arrays)
- Fixed format string interpolation in compiler (`"${0}"` no longer incorrectly treated as C# interpolation)
- Added documentation that `abs`, `max`, `min`, `sqrt`, `pow` operators don't exist in Calor
- Fixed 21 benchmark test files that had invalid syntax (recursive functions, data structures, design patterns)
- Fixed InformationDensity calculator using outdated square bracket patterns instead of curly braces
- All 40 benchmark files now compile successfully

## [0.2.3] - 2026-02-12

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.84x (C# leads on token economics)
- **Metrics**: Calor wins 7, C# wins 4
- **Highlights**:
  - Comprehension: 1.57x (Calor wins, large effect)
  - ErrorDetection: 1.51x (Calor wins, large effect)
  - RefactoringStability: 1.50x (Calor wins, large effect)
  - EditPrecision: 1.38x (Calor wins, large effect)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-only features (C# has no equivalent)
- **Programs Tested**: 36

### Added
- **Platform-specific VS Code extension bundles** - Each platform (Windows x64/ARM64, macOS x64/ARM64, Linux x64/ARM64) gets its own VSIX with bundled language server binary (~40 MB each)
- **Bundled language server discovery** - Extension automatically uses bundled `calor-lsp` binary, no separate installation needed
- **Enum extension methods** - `§EEXT{id:EnumName}` for defining extension methods on enums
- **Shorter enum syntax** - `§EN` as shorthand for `§ENUM` (legacy syntax still supported)

### Changed
- Enum definitions now use `§EN{id:name}` instead of `§ENUM{id:name}` (both are accepted for backwards compatibility)
- CI workflow now builds 6 platform-specific VSIX packages in parallel

### Fixed
- Benchmark framework now correctly counts Calor-only metrics (ContractVerification, EffectSoundness, InteropEffectCoverage) as Calor wins instead of ties

## [0.2.2] - 2026-02-10

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.84x (C# leads on token economics)
- **Metrics**: Calor wins 7, C# wins 4
- **Highlights**:
  - Comprehension: 1.57x (Calor wins)
  - ErrorDetection: 1.51x (Calor wins)
  - RefactoringStability: 1.50x (Calor wins)
  - EditPrecision: 1.38x (Calor wins)
  - ContractVerification, EffectSoundness, InteropEffectCoverage: Calor-exclusive features (not available in C#)
- **Programs Tested**: 36

### Added
- **Collection operations with semantic type checking** - `§LIST`, `§DICT`, `§HSET` literals with `§PUSH`, `§PUT`, `§SETIDX`, `§HAS`, `§CNT` operations
- **Pattern matching with arrow syntax** - `§W`/`§K` switch expressions with relational patterns (`§PREL`), variable patterns (`§VAR`), guards (`§WHEN`)
- **Async/await support** - `§AF`/`§AMT` for async functions/methods, `§AWAIT` expression with ConfigureAwait support
- **Lambda expressions** - Inline `(x) → expr` and block `§LAM`/`§/LAM` syntax with async support
- **Delegate definitions** - `§DEL`/`§/DEL` for custom delegate types with effect tracking
- **Event support** - `§EVT` for event definitions, `§SUB`/`§UNSUB` for subscribe/unsubscribe
- **Dictionary iteration** - `§EACHKV` for iterating key-value pairs

### Fixed
- Z3 SMT solver contract inheritance verification gaps
- Type checker for angle bracket generic syntax

## [0.2.1] - 2026-02-08

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Added
- **Z3 static contract verification** - Prove contracts at compile time with `--verify` flag; proven contracts can have runtime checks elided
- **Manifest-based effect resolution for .NET interop** - Layered resolution from built-in BCL manifests, user manifests, and namespace defaults
- **Granular effect taxonomy** - `fs:r/fs:w`, `net:r/net:w`, `db:r/db:w`, `env:r/env:w` with subtyping (`rw` encompasses `r` and `w`)
- **New CLI commands**: `calor effects resolve`, `calor effects validate`, `calor effects list`
- New CatchBugs component on homepage showing interprocedural effect analysis with compiler error demo

### Changed
- Homepage restructured from 9 to 7 sections for better focus
- Hero updated with value-oriented messaging ("When AI writes your code, the language should catch the bugs")
- CodeComparison updated with ULID-based stable identifiers
- FeatureGrid updated with impact statements and "Learn more" links for all cards
- BenchmarkChart reframed as "Where Explicit Semantics Pay Off"
- QuickStart now includes descriptions under each command
- ProjectStatus now compact with chip-based milestones

### Removed
- Story section from homepage
- CompetitivePositioning section from homepage
- VSCodeExtension section from homepage

## [0.2.0] - 2026-02-07

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Fixed
- Benchmark calculators now use correct curly brace syntax `{` for Calor patterns instead of square brackets `[`
- This fix enables proper detection of Calor language constructs in RefactoringStability, Comprehension, ErrorDetection, and EditPrecision metrics

## [0.1.9] - 2026-02-06

### Changed
- Documentation updated to remove v1/v2 version references
- Fixed invalid tokens in documentation to match current lexer (§SM, §NN, §CL, §MT, §IV, §TH)

## [0.1.8] - 2026-02-05

### Added
- New documentation page: "The Verification Opportunity" explaining why effects and contracts enforcement is a key value proposition
- "Learn more" links on landing page feature cards for Contracts and Effects

## [0.1.7] - 2026-02-05

### Added
- `calor lint` command for formatting and linting Calor files
- Comprehensive linter regression test suite

### Changed
- **Project renamed from OPAL to Calor**
  - Language name: Calor (was OPAL)
  - CLI tool: `calor` (was `opalc`)
  - File extension: `.calr` (was `.opal`)
  - NuGet packages: `calor`, `Calor.Tasks`, `Calor.Sdk`
- New tagline: "Coding Agent Language for Optimized Reasoning"
- Added project logo
- Enhanced warning messages for non-Claude AI agents (Codex, GitHub Copilot) to clearly indicate they cannot enforce Calor-first development

### Fixed
- Claude skills directory structure now uses correct `SKILL.md` format

## [0.1.4] - 2025-02-03

### Added
- **Multi-AI support**: Added support for GitHub Copilot, OpenAI Codex, and Google Gemini CLI
  - `calor init --ai github` for GitHub Copilot
  - `calor init --ai codex` for OpenAI Codex
  - `calor init --ai gemini` for Google Gemini
- **Solution-level initialization**: `calor init` now works on solution folders, initializing all projects
- Enum support for C# to Calor conversion
- Support for explicit enum values and underlying types
- Calor syntax: `§ENUM{id:Name}` and `§ENUM{id:Name:underlyingType}`
- Type mappings for DateTime, Guid, and read-only collections (ReadList, ReadDict)
- Comprehensive NuGet package metadata (authors, tags, repository URL, license)
- CHANGELOG.md for tracking version history

### Changed
- Renamed to "Coding Agent Language for Optimized Reasoning"
- Documentation links now point to https://juanrivera.github.io/calor
- Updated documentation to reflect current feature support status
- Fixed Claude skills directory structure to match Codex/Gemini pattern

### Fixed
- Clarified that `calor init` should be run in a folder with a C# project or solution

## [0.1.3] - Previous Release
- Claude Code hooks for Calor-first enforcement
- Initial AI integration with Claude

## [0.1.0] - Initial Release
- Initial public release of Calor compiler
