# T1 nullable-reference typing evidence (#1397)

Implementation candidate: **ef4227009f02ea13c3b2ab82719b625f4d10b197**.
Base: **6a0000bbad93944c55e44d608a65781a4c0df09a**.
Measurements: 2026-09-11, macOS ARM64, .NET SDK10.0.400,
Microsoft.NETCore.App10.0.11, compiler package version0.20.0, Roslyn5.3.0.
No version, semantics-major, release, nullable-activation or research change.
Later documentation/review commits must not be confused with this measurement
candidate; material source changes require new evidence and review.

The actual AI implementation slot and provisional2026-09-12 checkpoint are
recorded on [#1397](https://github.com/juanmicrosoft/calor/issues/1397#issuecomment-5639779148)
and in the scoping table. Two separate final-head review contexts and delegated
parent adjudication remain required. This file does not claim those approvals.

## Representation and ownership

`AttributeHelper` distinguishes nullable annotations from explicit runtime Option.
The historical parser spelling `OPTION[inner=T]` remains accepted as a **nullable
annotation encoding**, not a runtime Option identity. `TypeIdentity` canonicalizes
it to `T?`, keeping nullable array containers distinct from nullable elements.
Some and typed None use runtime `Option<T>` identities.

TypeChecker now has a separate `NullableReferenceType` for STRING/object and
registered reference declarations. Its declaration scan excludes structs, enums,
indexed/refinement types and names with conflicting reference/value declarations.
It does not resolve arbitrary external names, constructed nominal generics or
project reference context. Its existing permissive external/member boundaries
remain explicitly unmodeled; successful generated-C# compilation is separate
evidence, not a claim of complete TypeChecker inference.

All18 receiving rules for0272/0273/0274 remain analysis-only. Expanded nullable
locals retain their old0202 rejection when assigned to a nonnullable receiver;
raw inline nullable parameters were previously unmodeled and are not newly
activated. The temporary origin flag is excluded from type equality. #1385 owns
the eventual handoff, not this merge.

Binder's new active0275 identifies known reference/Option representation mismatches
at binding initialization and native return. Early TypeChecker0202 can still own
a genuine initializer type mismatch. Object boxing preserves the entire Option;
it is not an unwrap. Unknown source shapes do not become proven mismatches or
safe rows. This is not a general assignment, lambda-contextual-conversion,
constructor-input, field-write or arbitrary generic-payload checker.

The source catalog is now19 codes/34 routes:19 active leaves,14 analysis-only
leaves and one filtered forwarder. Primitive reporter fingerprints are unchanged.
The explicit local catalog regeneration command is:

```sh
CALOR_UPDATE_BINDER_ERROR_EMISSION_CATALOG=1 dotnet test tests/Calor.Compiler.Tests/ \
  --filter FullyQualifiedName~BinderErrorEmissionCatalogTests
```

The opt-in does not disable unknown-route, source-count or canary assertions.

## Measured controls and limits

| Case | Actual observation |
|---|---|
| `?str`, `?string`, `str?`, `string?`, `?System.String` literals and inline parameters | Default/type-off API and Calor AST round-trip compile; emitted CLR parameters/returns are `System.String`, not Option. Runtime returns `"value"`. |
| Nullable `Environment.GetEnvironmentVariable` return | Default/type-off API compiles; actual missing-key runtime result is null. With the repository-scoped metadata manifest discoverable, the bound BCL call retains Annotated. TypeChecker itself still does not infer BCL call results. |
| Declared `Foo` reference, nullable parameter/copy/constructor | Default/type-off and round-trip compile; emitted return/parameter type is `Foo`, and construction returns a non-null Foo instance. |
| Explicit `Option<str>` Some/None | Both modes and round-trip retain `Option<string>` at runtime; Some unwraps to `"value"`, None remains absent. |
| Reference/Option initialization and return mismatches | API rejects across typing/transpile combinations. Binder/LSP and type-off API retain active0275 and source spans. Default initializer typing may return0202 first. |
| Expanded nullable local to `str` | Default0202 remains. Type-off still emits C# that compiles with nullable warnings; raw binder0272 remains analysis-only. A literal `null` also retains the pre-existing internal undefined-reference finding. |
| Raw inline `?str` to `str` | Previously accepted with0200; still accepted without that spurious warning. Binder0272 remains analysis-only. This is explicitly not a new safety guarantee. |
| Nullable BCL result passed to native nonnullable STRING input | Existing0208 rejection remains; normalization does not hide it as an unresolved argument. #1382/#1383/#1385 still own the call repair/activation. |
| Nullable value and container controls | `?i32 = 42` remains rejected by the old incomplete TypeChecker behavior. Struct/enum nullable parameters retain unsupported-type warnings. C# mapping still distinguishes nullable values, nullable arrays and nullable elements; known value receivers map to `System.Nullable`, not Option. |
| Array/generic controls | Existing incompatible element/payload assignments still fail0202. `[?str]` and `Option<?str>` retain their different CLR containers. No arbitrary payload policy is activated. |
| Nullable concatenation and boxing | Two null strings concatenate to empty string; `"a"`/`"b"` produce `"ab"`. Boxing to `?object` returns the whole `Option<string>` value. |
| Nullable nominal member read | **Not an ordinary safe compile at this candidate:** member-effect resolution reports0411/0410. With the existing effect opt-out, typing no longer invents a record-operation error and emitted C# returns0. This row did not measure pre-T1 effect diagnostics; it must not be cited as proving the same rejection existed before T1. The unknown receiver is not counted resolved/safe. |
| Cache/mode boundaries | Root CLI covers explicit/environment type opt-outs and transpile on changed warm sources; MSBuild task covers three option combinations; editor diagnostics clear on a safe edit. Root/watch effective typing and elision keys have a direct regression independent of a temporary TypeChecker defect. |

The replacement cache fixture was reproduced with the base binaries, before
behavioral edits:

```calor
§M{m1:TypingBaseline}
  §F{f1:Probe:pub} () -> void
    §E{}
    §B{x:?str} null
    §B{y:str} x
```

At6a0000bb, default CLI reported0200 for `inner=STRING` and0202 at line5;
`--no-type-check` succeeded. A separate raw-inline `(?str:x)` to `str` fixture
succeeded with0200. These are dated base observations, not relabeled current
outputs. The N0/R1 evidence and scratch remain unchanged.

## Executed validation

All commands use the existing runners and normal restore sources. Z3 assets were
copied from the preserved routing worktree after the first missing-asset failure
and checked against the repository manifest. No research or model-study runner
was invoked.

| Existing runner / selection | Result |
|---|---|
| Compiler: nullable typing/integration/checker, routing/catalog, type identity, symbol/overload, mapper/defect/type-system/type-operation, Option/Result runtime, incremental CLI, type-default CLI, watch and versioning classes | 455 passed,0 failed/skipped |
| LSP: `NullableReferenceTypingTests` and `BindingDiagnosticRoutingTests` | 11 passed,0 failed/skipped |
| MSBuild: `CompileCalorIntegrationTests` and `BuildStateCacheTests` | 105 passed,0 failed/skipped |
| Enforcement: `EffectResolverTests` and `EffectRowLatticeTests` | 197 passed,0 failed/skipped |
| Conversion: `RoundTrip_RoslynCompileSucceeds` and `SnapshotConversionTests` | 156 passed,0 failed/skipped |
| `calor self-check docs --root . --no-telemetry` | No drift |
| Calor-first diff guard at committed ef422700 against6a0000bb | Passed;23 changed paths inspected |

Selections can overlap; these are not a summed unique-test or corpus denominator.
The new compiler cases were counted from the actual455-case TRX:47 passed
(42 nullable-reference cases,4 CLI cases,1 direct key case). LSP adds5; tasks add3.
Manifest totals therefore move8738→8785,492→497 and127→130, with skips unchanged.
Full final-head CI remains a separate merge requirement.

Exact scoped test selections (run from the worktree root):

```sh
dotnet test tests/Calor.Compiler.Tests/ --filter 'FullyQualifiedName~NullableReferenceTypingTests|FullyQualifiedName~NullabilityIntegrationTests|FullyQualifiedName~NullabilityCheckerTests|FullyQualifiedName~BindingDiagnosticPolicyTests|FullyQualifiedName~BinderErrorEmissionCatalogTests|FullyQualifiedName~TypeIdentityCanonicalizationPropertyTests|FullyQualifiedName~SymbolAndOverloadBindingTests|FullyQualifiedName~TypeMapperTests|FullyQualifiedName~TypeCheckerDefectTests|FullyQualifiedName~TypeSystemTests|FullyQualifiedName~TypeOperationTests|FullyQualifiedName~OptionContextRuntimeTests|FullyQualifiedName~ResultContextRuntimeTests|FullyQualifiedName~IncrementalCliBuildTests|FullyQualifiedName~CliTypeCheckDefaultTests|FullyQualifiedName~WatchSessionIntegrationTests|FullyQualifiedName~VersioningTests' --verbosity quiet
dotnet test tests/Calor.LanguageServer.Tests/ --filter 'FullyQualifiedName~NullableReferenceTypingTests|FullyQualifiedName~BindingDiagnosticRoutingTests' --verbosity quiet
dotnet test tests/Calor.Tasks.Tests/ --filter 'FullyQualifiedName~CompileCalorIntegrationTests|FullyQualifiedName~BuildStateCacheTests' --verbosity quiet
dotnet test tests/Calor.Enforcement.Tests/ --filter 'FullyQualifiedName~EffectResolverTests|FullyQualifiedName~EffectRowLatticeTests' --verbosity quiet
dotnet test tests/Calor.Conversion.Tests/ --filter 'FullyQualifiedName~RoundTrip_RoslynCompileSucceeds|FullyQualifiedName~SnapshotConversionTests' --verbosity quiet
```

Runtime compilation uses `GeneratedCSharpCompiler.References` (process trusted
platform assemblies plus Calor.Runtime), not a claimed project reference pack.
These hashes are provenance anchors, **not the complete reference closure**:

| Asset | SHA-256 |
|---|---|
| Runtime10.0.11 `System.Private.CoreLib.dll` | `b5f15d0eccd5abf24ca1b5a4f75b4ba72dc62074974064cbdf142e4f6160efda` |
| Runtime10.0.11 `System.Runtime.dll` | `eeb5bfc04db9002594764265acedac58474d04561183f5a21d2bd6c61f045c53` |
| Local Debug `Calor.Runtime.dll` | `212ca614abfb702d236202bb8f4eefe052610141371ae70b68d3d375da1562c0` |
| `t1-compiler-final.trx` | `b3757c75e11426e7acc1ace98bd0230b12b2ba2bba10a71f6c88b35b888605d8` |
| `t1-conversion.trx` | `ef3bb28bf26f677f30fd790d883e06b20e367a4ec1df179cd93fdbefbbbf0e6e` |

TRX/process scratch is retained in the session's `files/typing-1397-evidence`.
Development red runs exposed the old identity assertions, missing cross-mode
representation rejection and fixture grammar/effect assumptions; they are not
reported as flakes or erased into a success denominator.

#1398, #1380/#1381/#1384, #1382/#1383, #1400/#1401 and activation/adjudication
gates retain their assigned scope. No new whole-corpus result, D3/D12/D14
demotion lift, runtime-guard change or unchecked-mutation guarantee is claimed.

## Initial review and remediation boundary

The [initial independent reviews and actual CI failures](https://github.com/juanmicrosoft/calor/pull/1443#issuecomment-5640845446)
apply to `19d1827440af52f913ad96b947770e7c601593c5`, not a later repaired head.
Integration blocked; compatibility accepted subject to CI. Neither is a final
approval of remediation. The complete original briefs are retained alongside
this record; their invocation named that exact head and required the actual
checked SHA, ACCEPT/BLOCK, reproduced findings, commands and honest gaps.

An external `--artifacts-path` build lacked the repository-discovered metadata
manifest. Its two annotation assertions failed, as did existing metadata-context
tests with `FileNotFoundException`. Copying the original manifest into that
scratch root, without changing source or rebuilding, made all42 T1 plus18 existing
metadata-context cases pass. Both manifest copies hash to
`b82830666f4bbb1b2e3e01b35d3df0dab94c04c5a3c94a276daa18f3584e4b4d`.
This is an explicit reference-profile prerequisite, not evidence that arbitrary
installed/default contexts resolve metadata. The new test checks that prerequisite
directly; the Annotated assertion remains intact. No production metadata fallback
or Oblivious policy was changed.

The unknown receiver sentinel `?` must not map to the pure runtime Option manifest.
Removing that accidental mapping exposed two known primary-receiver paths in
Effects: `base` and null-conditional member chains. Remediation resolves those
known types, retains pure fields, and charges resolved property getters. Runtime
controls cover both paths, including null short-circuiting; an unresolved
coalesced receiver still emits0411/0410 rather than borrowing Option purity.
This does not implement general coalescing transfer, BCL static-member resolution,
nullable activation or arbitrary nested generic inference.

The product binder-corpus audit found three changed rows among364 unchanged
source keys. `MessageFormatter.cs` loses two raw0273 diagnostics from mistakenly
using the enclosing return contract inside lambdas (12 to10; propagated1
unchanged). `AggregateSink.cs` and `DisposingAggregateSink.cs` retain their raw
diagnostic sites/counts, with offsets shifted by four characters after converter
effect-row output adds `mut`. All non-binding coverage coordinates and every
propagated-error identity remain unchanged. The product source-coverage ratchet
was regenerated with its existing opt-in; its eight tests pass. These measurements
do not alter frozen research inputs, ledger464ace or N0/R1 evidence.
