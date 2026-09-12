# Binder diagnostic routing contract (#1396)

This is infrastructure, **not nullability activation**. The accepted N0 baseline
and its measured candidate identities remain unchanged in
[the scoping record](../plans/v0.22-nullability-enforcement-scoping.md).
The implementation slot was accepted after N0 by the user-delegated coordinator.
Its owner is the existing Copilot/GPT-6 Astra compiler context; the provisional
checkpoint/review target is2026-09-12, not a human staffing or release commitment.
Two separate final-head reviewer contexts and coordinator adjudication are required.
AI review contexts can be correlated; they are not statistically independent or
substitutes for a claimed human review.

## Executable inventory

After #1397, `BindingDiagnosticPolicy.Catalog` classifies19 Error-capable codes:
9 `CompilationError` codes and10 `AnalysisOnly` codes. The #1396 baseline was18
codes (8 active); its frozen evidence is not relabeled. Every entry has a reason and
owning issue. The policy stays in its original `Binding/Scope.cs` maintenance
surface; no new product C# path or Calor-first allowlist exception is introduced.
The source-site golden in
`tests/TestData/Binding/BinderErrorEmissionCatalog.golden.json` records34 routes:
19 compilation-error leaves,14 analysis-only leaves and one filtered forwarder.
These are **emission-site counts**, not fixture counts or distinct-code counts.
The metadata helper `MetadataBinderResult.ToDiagnostics` is included because its
caller-selected severity can be Error even though its default is Info. It is not
currently a production propagation route.

`BinderErrorEmissionCatalogTests` scans all `Binding/**/*.cs` with Roslyn,
including constructors, target-typed construction, dynamic severity and
`DiagnosticBag` helper expansion. A new site using an already known code still
changes the inventory. Unknown codes/helpers/forwarders fail, as do escaped
reporter delegates and inactive binding source that needs another scan
configuration. Dynamic code expressions that cannot be resolved to a reviewed
constant fail closed; they are not inferred from message text. Primitive reporter
syntax is separately pinned because the scanner summarizes those storage methods.
Their implementation changes require review as well as updating their fingerprints.
This is a source ratchet over the current C# mechanisms, not a proof about arbitrary
reflection, generated code or future reporting mechanisms. New mechanisms must
extend the scanner and its mutation canaries before being admitted.

The one forwarding record represents `PropagateCompilationErrors`, not a new
Error emission. Functional tests include all analysis-only codes in its input
and prove filtering and deduplication. Analysis bags keep their original severity,
message, span and `HasErrors` behavior.

## Structured receiving policy and pass ownership

`BindingDiagnosticContext` records binder origin, receiving boundary and structural
target shape. The four existing nullable report sites attach initializer,
native-return or method-argument context. STRING aliases use `TypeIdentity`,
never diagnostic prose. Arrays, generic instantiations, nominal types and
unsupported types are distinct. None of these labels proves reference resolution,
supported rank/payload, null state or safety.

All18 receiving rules (three codes times six shape values) remain `AnalysisOnly`.
0272/0273/0274 are not build blockers. Scalar STRING activation belongs to #1385;
other shape decisions belong to #1400/#1402 and their prerequisites. An absent or
unsupported context does not become a new safe row. No suppression flag exists.

`Program.Compile` still runs TypeChecker first and can return before binding.
The #1396/N0 nullable-literal0202 example is historical: #1397 repairs supported
nullable-reference literals and inline type names. An expanded nullable local
assigned to non-nullable `str` still reports transitional TypeChecker0202 before
binding. A raw inline nullable parameter did not have that rejection at the
measured baseline; normalization does not silently activate it. Both spellings
now denote the same reference type, but this temporary compatibility distinction
retains the previously shipped gates until #1385 can take ownership.

`Calor0275` is an active **representation mismatch**, not a nullable-state finding:
supported STRING/declared reference values and explicit runtime `Option<T>` values
cannot stand in for one another at binding initializations or native returns.
Default typing can reject the initializer earlier with genuine0202; otherwise
Binder owns0275, including no-type-check/transpile and editor paths. Ordinary
object boxing preserves the complete Option value and is not an unwrap. Unknown
source types are not classified as proven mismatches or counted as safe.

#1398 still owns coalesce/throw/pattern transfer; #1382/#1383 still own selected
call mapping and native nullable STRING inputs. Failed nullable native overloads
retain0208 instead of disappearing into an "unresolved argument" fallback when
the canonical nullable spelling changes. General mutable assignment and
unchecked mutation remain outside these new representation checks.

`DiagnosticBag` scopes provenance only during `Binder.Bind` and restores it after
the call. Parser/TypeChecker diagnostics using the same code remain distinguishable.
The original public four-argument `Report` signature is retained. Suggested-fix
and ordinary representations both carry provenance. Code-action diagnostics use
the same fix-aware converter rather than reconstructing a metadata-free diagnostic.

## Real callers, user-facing output and options

| Surface | Routing and supported controls |
|---|---|
| `Program.Compile` overloads | TypeChecker early return precedes the always-on binder; shared policy forwards existing active Errors once. Options expose typing, transpile, verification and effect controls. `CompilationContext` shares services, not successful results. |
| Root CLI | `-i FILE --format json`; `--verify`, `--no-type-check`, `--transpile-only`, `--permissive-effects`, `--enforce-effects false`, and `CALOR_NO_TYPE_CHECK`. The regression crosses all32 combinations of the five flags plus a separate environment case. |
| `run` / `test` | Use `Program.Compile`; support `--verify`, `--permissive`, `--enforce-effects false`, and environment type opt-out. Direct `--no-type-check`, `--transpile-only`, `--permissive-effects` and incremental-cache switches are N/A, not invented syntax. |
| `verify` | Uses `Program.Compile` with verification; supports environment typing and verification-cache controls. Separate type/transpile/permissive switches are N/A. `VerificationAnalysisPass` uses the same binder filtering policy. |
| `watch` | Uses `CompilationDriver` and `Program.Compile`, with cache, effect settings and environment typing. Verify/type/transpile flags are N/A. Session tests inject changes, explicitly not a full filesystem-watcher substitute. |
| LSP `DocumentState` | Binds directly without the API TypeChecker pass. Error-level binder analysis findings show source `calor (analysis only)`; active and other-pass diagnostics retain `calor`. Code, span, severity, fix data and messages are preserved. A URI-derived declaration identity in a message can differ from the API file-path identity. |
| `Calor.Sdk` / MSBuild | SDK props/targets invoke `CompileCalor`; there is no invented callable SDK API. `CalorTypeCheck`, `CalorTranspileOnly`, `CalorVerify`, `CalorPermissiveEffects`, effect settings and environment defaults map to real compilation options. Tests exercise task/cache and CLI agreement. |
| `ProjectSymbolIndex` | Keeps the internal `bindDiagnostics.HasErrors` completeness signal, including analysis-only errors. It does not apply build-output filtering merely to imitate the UI. |
| `CallGraphAnalysis` | Its internal bag identifies incompatible0207/0208 call spans for analysis. It is not user-facing diagnostic propagation. |
| `ExternalCallCollector` | Uses a separate binder bag for receiver resolution, not publication. Existing unresolved behavior is unchanged. |

Active binding remains enabled with all supported type/effect opt-outs, including
transpile-only. No nullable case is activated here, so future active nullable
code/span/severity parity is still mandatory at #1385/#1402. Existing0206 has
API/CLI/editor parity and remains rejecting after changed warm source.

## Cache identity correction

The N0 default-layout counterexample was real: a cached success with
`--no-type-check` could satisfy a subsequent default invocation that should fail0202.
It did **not** bypass an active0206 diagnostic.

Root and watch options now hash effective typing, including the environment
default, and guard-elision policy. The same captured effective value configures
compilation, avoiding a key/options mismatch. The options serializer changes from
`compile-inputs-v3` to `compile-inputs-v4`; this is cache invalidation, not a product
or semantics release. Compiler closure hashes additionally invalidate changed
compiler/routing code. Existing SDK reference/options/compiler/schema/semantics
fingerprints remain in force.

Root caching requires default output layout: explicit `-o` is intentionally
uncached, including with `--cache`. Transpile-only remains uncached. Regressions
cover warm opt-out to default typing, environment transitions, guard options,
changed invalid input, compiler/schema/semantics changes, and safe recovery.
After #1397 fixes nullable literal initialization, the live cache fixture uses a
real transitional nullable-to-nonnullable assignment instead of the old false
positive. Direct effective-options identity checks remain; the frozen N0/R1
process outputs still describe their original candidates and fixtures.
No released binary was swapped underneath a live process; version invalidation
is tested through the existing cache fixtures, not described as such a deployment.

[The process supplement](../plans/evidence/diagnostic-routing-1396/README.md)
adds actual SDK builds and real watch observations at the pinned implementation
candidate. Both inherited warnings-as-errors and neutral consumer configurations
are retained: independent generated-C# rejection is not nullable-binder activation.

No D3/D12/D14 demotion or runtime-guard change, annotation widening, automatic
converter adaptation, release or research collection is part of this work.
