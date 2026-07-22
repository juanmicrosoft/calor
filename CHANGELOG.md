# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- **Array-to-collection check extended to return and reassignment positions (#724).** `Calor0254` (the #722 array-vs-collection trap) now also fires when a function/method declared `-> List<T>` (or another concrete generic collection) returns an array (`§R §C{File.ReadAllLines}`), and when `§ASSIGN` reassigns an array into a collection-typed mutable variable — not just in binding position. Same rule (interfaces still accepted), same shared array-source recognition. Argument position (an array passed to a `List<T>` parameter), which needs call-site type flow, is tracked in #725.
- **Array-to-collection type error at the language level (#722).** `calor -i` now rejects a **binding** declared as a concrete generic collection (`List<T>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, …) whose initializer is an array — e.g. `§B{lines:List<str>} §C{File.ReadAllLines}` — with a dedicated diagnostic (`Calor0254`) pointing at the binding, instead of emitting `List<string> x = File.ReadAllLines(...)` that fails a downstream `dotnet build` with CS0029. Mirrors C#'s rule: an array satisfies the collection *interfaces* (`IList<T>`, `IEnumerable<T>`, …) but not the concrete classes, so interface-typed bindings are still accepted. The array source is recognized for known array-returning BCL methods (a table shared with #712's docs guard, so the two cannot drift) and any user function declared `-> [T]`. This is the language-level counterpart to #712's docs guard — it protects agents who write the mistake independently, not just those copying the exemplar. Scope: this covers **binding** position; the same trap in reassignment (`§ASSIGN`), return, and argument positions is tracked in #724.
- **Exemplar compile-checking (#712).** `calor self-check docs` now compiles every complete `§M` program in the agent syntax exemplar (`Resources/agent-syntax-exemplar.md`, served to agents as `calor://primer`) all the way to C# and runs the **generated C# through Roslyn's full semantic model** (`Calor1330`) — the only layer that catches type errors the Calor pipeline itself emits without complaint, such as binding `File.ReadAllLines` (an array) to `List<str>` (CS0029). The copyable fragment reference lines, which intermix prose and free identifiers and cannot be compiled standalone, get a targeted lint for that same array-vs-collection trap (`Calor1331`). Mutation-tested: reintroducing the `List<str>` `ReadAllLines` bug fails self-check whether it lands in a complete program or a fragment line. Backed by `ExemplarCompilesTests` (runs in every CI environment). Scope note: this guards the exemplar *document*; the language-level fix that rejects the mistake in any source is #722 (above).

### Changed
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
