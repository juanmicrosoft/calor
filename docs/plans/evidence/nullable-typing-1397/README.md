# T1 nullable-reference typing evidence (#1397)

The current integrated candidate is recorded under
[Post-publication continuation](#post-publication-continuation-merged-main-not-rewritten-history).
Earlier sections retain their original measured source, version and review status.

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

## Rebased remediation candidate and product corpus

Production remediation candidate: `6b9f4eeda8199fa8c8f1840066f0b27cbf262f1b`,
rebased onto E1 merge `be488238d3aac374995174aa47526c7f3d09fd51`.
The earlier measurements and review SHAs above remain historical; they were not
relabelled after rebase. No E1 harness file or capacity row was edited.

The existing product golden generators were run at this production commit, after
reproducing their failures. The DisplayString distribution changes only one typed
None from `OPTION[inner=INT]` to `Option<INT>`. The Calor0425 ledger retains117
diagnostics across47 of326 enforced modules,38 propagated-bind exclusions, and
all cause/coverage/module counts. Only0411 site volumes change:

| Corpus | Old0411 sites | Current0411 sites | Modules with0411, unchanged |
|---|---:|---:|---:|
| MediatR | 279 | 303 | 12 |
| Serilog | 543 | 551 | 61 |
| FluentValidation | 7420 | 7939 | 115 |

The increase exposes unmodeled receiver/member paths that formerly borrowed the
pure Option manifest through the unknown `?` sentinel, including conditional
member accesses now charged by Effects. This is not improved resolution or
nullable-state activation. The extra sites are inside the already-unknown module
sets; no zero-Unknown safety claim is made. The ledger pins the actual production
commit and unchanged corpus revisions:
MediatR `fb309026775ef953a64fb5339d074426c1ad2c37`,
Serilog `0597ddfbd4ec594d9c42edd745fe728a2198bad9`,
FluentValidation `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c`.

The formatter's `11-05.approved.calr` was **already rejected** by the base CLI:
Calor1002/CS0246 for untranslated `str`/`i32` in the generic static receiver.
Current CLI rejects earlier with0411/0410 for `?.get_Empty`. Its single baseline
entry moves from generated-C# failure to semantic failure; the942-file denominator,
702 successful transformations, parse failures and total fallback count stay fixed.
Neither the input nor the formatter's safety checks were changed. Arbitrary
generic static-member resolution is not claimed repaired by T1.

Three additional receiver cases bring the compiler manifest to8788, from the
initial T1 count8785. The direct getter regressions exercise declared allocation,
generated C# and actual return values (base3, conditional0/null); unknown receivers
remain rejected. An initial getter fixture accidentally nested a following class
after a direct `return new` expression. That fixture was corrected to bind then
return the value, not counted as a product flake or a green run.

### Remediation validation

At the rebased production candidate above, the expanded original compiler
selection plus all initially failing product classes passes538/538, no skips.
The additional selectors are `PrimaryReceivers_PreserveBaseAndConditionalChains`,
`BoundTypeArchitectureTests`, `Calor0425CorpusLedgerTests`,
`LosslessFormattingTests` and `BinderIncompleteRatchetTests`.
Actual TRX SHA-256:
`c91b3445d5464f173fbcd71aac29f28a64166a8d4840a4099b15de1a4b2935af`.
LSP11/11, MSBuild/cache105/105, conversion156/156, and product enforcement275/275
also pass without skips. The enforcement selection adds `EffectEnforcementTests`,
`Issue1176AccessorContractTests` and `Issue785ClosureTests` to the original197.
These overlapping selections are not a summed unique-test denominator.

The external-artifacts build, with the same explicitly copied metadata manifest,
passes45 T1 plus18 metadata-context cases (63/63). Without that prerequisite the
profile remains unsupported; the failure was not hidden by a skip or annotation
fallback. Documentation self-check reports no drift. Actual final-head CI and two
fresh independent reviews remain separate from these local measurements.

### Final-head provenance-index reconciliation

Two fresh independent contexts accepted `687c036850d1f8c6259cf1cc60eb7a42fbff88c8`
for the bounded contract. Its CI then exposed a metadata inconsistency:
the separate commit-stamp index still named the old Calor0425 measurement. Only
that product ledger's index entry is synchronized to `6b9f4eed`; no source code,
measurement result, test assertion, frozen ledger or other index entry changes.
The three existing stamp tests plus the actual corpus recomputation pass4/4.
Both reviewers must acknowledge the resulting exact head, and actual CI must pass.

The integration reviewer also called out suffix `str?` locals as a transitional
rough edge. An actual CLI replay with preserved pre-T1 binaries and current
binaries confirms that both accept `str? x = null; str y = x`; the old checker
additionally warned0200. This is a previously accepted spelling, not a removed
rejection. Expanded-prefix locals retain0202 as measured earlier. #1385 still
owns a consistent activated receiving policy.

This was the remaining failure exposed at that checkpoint, not proof that every
later internal CI test leg had run. The next section records subsequent failures.

## Dictionary compatibility remediation

Production candidate: `b2c435610c3aa34accc0f28f66d5eca834d7a129`, still based on
E1 merge `be488238d3aac374995174aa47526c7f3d09fd51`. Version remains0.20.0.
The historical approvals below do **not** approve this material source change:

| Exact reviewed head | Integration context (GPT-5.5) | Compatibility context (Claude Opus4.8) | Disposition |
|---|---|---|---|
| `687c036850d1f8c6259cf1cc60eb7a42fbff88c8` | `41351e45-0b3f-4fa5-bd67-1eacc0f73e64` | `2e3e8b0f-f9a3-44db-942b-9d28505e49d1` | Both accepted; CI subsequently exposed the product stamp-index inconsistency. |
| `25161a3cfdeabcca841f0d229b6d1fbb53f4d380` | `a88258d2-c108-4261-a033-0cda65649195` | `0bcdf341-35fc-4e03-8229-8e72ea98708c` | Both accepted; actual CI subsequently failed four dictionary runtime cases and the string-origin key ledger. |

Run34652725411 finished with34 successful checks and two failed jobs:
compiler103438331298 and quality103438331022. The five distinct failures were
reproduced locally (29 passed,5 failed). They were not flakes, concurrent-main
changes or reasons to bypass CI. Earlier review and local selections did not
establish full compatibility.

Preserved C# dictionary initializers assigned to `var` lost the receiver type in
their Calor binding. Once unknown `?` stopped borrowing Option purity, subsequent
`Count`/`Keys` access exposed that loss. The converter now retains a spellable
semantic type only for actual metadata-declared BCL Dictionary, SortedDictionary
and ConcurrentDictionary results with two supported type arguments. Source
lookalikes, error/dynamic/anonymous/tuple/nullable arguments and non-dictionary
results are excluded. This is not arbitrary generic or nullable member inference.

Receiver aliases reuse the existing ConcurrentDictionary manifest. Dictionary
and SortedDictionary getter entries describe Count as pure and Keys/Values as
allocating views. SortedDictionary receives no blanket purity/default or method
coverage. Seven resolver controls cover getter charges and unknown members; four
converter controls cover spellability and the non-dictionary-result boundary.
The existing dictionary runtime regressions execute the emitted C#.

### Product data audit at b2c43561

The existing generators ran at that exact production commit; assertions were
then rerun without regeneration. The two changed product stamp-index entries now
pin b2c43561. Other index entries, frozen research and prior N0/R1 evidence remain
unchanged.

- Calor0425 stays117 diagnostics/47 modules/326 enforced and38 propagated-bind
  exclusions. Calor0411 site totals are297/551/7932 for MediatR/Serilog/
  FluentValidation, versus the earlier remediation's303/551/7939. The same
  12/61/115 unknown-module sets and every other ledger coordinate remain.
- The effect-key ledger records265 bound-origin and826 string-origin lookups,
  versus265/822 previously. Only the bench subject changes620 to624. The200
  floor,380 bench files, full scope/denominators and notMeasured identities stay
  fixed. Lookup provenance is not resolved-signature or nullability coverage.
- Three additional binder rows change only raw diagnostic hashes:
  AccessorCache (10 errors), LanguageManager (82), and Mediator (17).
  The existing diagnostic probe was run in separate processes with preserved
  pre-dictionary binaries and b2c43561 binaries. Ordered codes, messages, span
  lengths and columns are identical. Offsets move by0-32 characters and lines
  by0-2 as emitted effect rows acquire `mut` from the now-resolved existing
  ConcurrentDictionary manifest. Converted-source diffs contain only those
  effect-row changes. All364 source keys, propagated identities and non-binding
  coverage coordinates remain unchanged.

The raw probe output and converted-source pairs are retained in session
`files/t1-dictionary-repair`, together with the original failure logs. Corpus
revisions remain the three exact commits stated above.

### Executed b2c43561 validation

Compiler579/579, LSP11/11, MSBuild/cache105/105, product enforcement282/282 and
conversion156/156 pass, with no skips. These overlapping selections are not a
summed unique-test denominator. All projects were rebuilt against the current
source; old copied compiler binaries were not used for these results.

The compiler selection adds `DictionaryInitializerSemanticsTests`,
`EffectResolverKeyLedgerTests` and `LedgerCommitStampTests` to the earlier538.
Enforcement uses the same five class selectors as the275-case remediation run,
plus its seven new cases. The original LSP/tasks/conversion selectors are
unchanged. Ten affected generator/binder-ratchet cases also passed during
regeneration; the579-case run separately covers normal assertions.
The actual compiler TRX SHA-256 is
`7c7d59b3cc517ac2ab93bb0a44f80f786a31cc499a3aacc61fbf7ffb27e943a1`.
Documentation self-check reports no drift.

Manifest totals now compiler8792, LSP497, tasks130 and enforcement691, with skips
unchanged. The initial42 T1 cases grew to45 through receiver controls; the four
dictionary cases are separate. The earlier external-profile63-case result stays
dated to its own source candidate; it is not relabeled as a b2c43561 run.

Fresh final-head compiler-integration and adversarial-compatibility contexts must
review this complete diff and remediation, followed by all actual final-head CI
and delegated parent adjudication. Exact prompts, model/context IDs and outcomes
will be posted on the PR. Separate AI contexts can share errors through training,
prompts and evidence; they are not human reviews or statistically independent
evidence.

**Merge constraint:** use a normal merge commit, not squash/rebase merge, so the
indexed production measurement commit `b2c43561` remains an ancestor and resolves
in a fresh main checkout. The repository permits merge commits. This is a
provenance requirement, not authorization to merge before delegated adjudication.

## Alias review finding and final source checkpoint

The actual reviews of `3bdb8e40c6a3a2eca9bcfde7e98a01a2e43c0cfa` disagreed:
integration context `03177048-087f-4db5-9712-3c46ab9246f0` (GPT-5.5) **blocked**
on `using D = System.Collections.Generic.Dictionary<int,int>; var d = new D ...;
return d.Count.ToString();`. Compatibility context
`c14ac50d-191b-49b0-8615-e59134c40f95` (Claude Opus4.8) accepted the bounded
contract. All36 CI checks subsequently passed at that head, but the reproduced
review finding still blocked acceptance; green CI did not override it.

Source remediation is `1edc709e25028175be970d704c16eba3af8a5ce1`.
The syntactic dictionary-name filter prevented aliases from reaching the
semantic BCL/result-shape guard. Removing only that name filter retains the
initializer-operation and semantic/spellability gates. Five actual runtime
alias cases and three type-identity cases reproduced the defect (eight failed
of46 dictionary cases). Two additional nullable-value/dynamic argument controls
continued to exclude those shapes.

After that fix, the ConcurrentDictionary alias-only control exposed a second
coupled defect: a minimally qualified emitted type requires an ordinary namespace
import that an alias does not supply. The bounded helper now emits namespace-
qualified dictionary and argument types using Roslyn's display format, while
retaining nullable modifiers for exclusion checks. The test does not add the
missing import to hide the failure. No general alias resolver, generic-inference
policy, new manifest entry or nullable activation was added.

At1edc709e the expanded compiler selection passes589/589, including all46
dictionary cases and unchanged product corpus/stamp assertions. LSP11/11,
MSBuild/cache105/105, product enforcement282/282 and conversion156/156 pass after
rebuild, no skips. Compiler TRX SHA-256:
`29d3772c0834f6a9fa96591246437877c89533eb3292c1f029ec943d8dc803cf`.
Compiler manifest is now8802; the other totals and all skip pins are unchanged.
The product data measured atb2c43561 remain unchanged under the normal assertions;
they are not restamped or relabeled as newly generated at1edc709e.

The compatibility reviewer also reproduced an unresolved generic null-conditional
receiver (`d?.Keys` with a `Dict` receiver), which reports0411 instead of resolving
the allocating getter. This fails closed, is not counted resolved/safe, and
remains a disclosed broader receiver/member limitation. The alias repair does
not claim to fix it. Dictionary's pre-existing blanket default manifest coverage
also remains unchanged; no such default was added for SortedDictionary.

Fresh reviews must cover this final source and its exact evidence head. Prior
approvals and the36-green3bdb8e40 CI result do not approve or validate the later
head. The b2c43561 ancestry/normal-merge requirement still applies.

## Post-publication continuation: merged main, not rewritten history

The user authorized continued product0.22 work at2026-09-11T23:34:44-04:00.
This resumes the same bounded T1 context and PR, with no new delivery promise,
release or research scope. The capacity row records that continuation.

Integrated measurement candidate:
`6ce32e261ec21df4d307a9a9288a5854e8b90a74`.
It merges published main `88b5d38df97fd7e438882c956b9f9dcdc7a6cef5` into the
existing `07dd0158a36c7375a2c92bacf76c3bf02f910011` branch. No squash/rebase or
conflict resolution was needed. Measurement ancestorb2c43561 remains reachable.
The entire `src/` tree is unchanged from07dd0158; inherited package version is
now0.21.0. All earlier0.20.0 measurements and review approvals remain historical,
not relabeled as this integrated build.

Published release/website/workflow files are unchanged relative to88b5d38d.
Recovery #1449's MediatR `ExtraBuildProperties` isolation and two tests are
inherited unchanged; its harness manifest291 is preserved alongside T1's
compiler8802/LSP497/tasks130/enforcement691. N3 is not integrated or adjudicated
by this merge. All0272/0273/0274 routes remain AnalysisOnly; no guard or
transitional-rejection policy changed.

On2026-09-12 UTC, macOS ARM64, SDK10.0.400/runtime10.0.11, the rebuilt candidate
reports CLI0.21.0. Existing selections pass589 compiler,11 LSP,105 MSBuild/cache,
282 product enforcement and156 conversion cases, no skips. The four
`RunConfigOverrideTests` also pass, including both #1449 controls. These are
overlapping scoped selections, not a full local suite or summed denominator.
The compiler selection is the exact earlier589-case selection. Product corpus,
catalog, stamp-index and cache assertions run normally, without regeneration or
restamping; all product ledger files remain byte-identical to07dd0158.

| Integrated measurement artifact | SHA-256 |
|---|---|
| `t1-published-main-compiler.trx` | `39df8aff815538ac8205c2aa29e09b071efc18d853e288fc38ea1090d93d8e02` |
| Debug `calor.dll` | `b78dbab7434d123aa2e1212abcd5ce0854714a036584948c56c1e446e811d790` |
| Debug `Calor.Runtime.dll` | `725934220c10b522da3a6c38ab1726b50909538c12863b649953e302bde0e47c` |

These binaries were built at the integrated source candidate above; subsequent
capacity/evidence-only commits do not create a new claimed binary measurement.
Documentation self-check reports no drift. Existing reference-profile limits,
unknown member paths and conditional generic getter failures remain disclosed.

**Known local baseline failure, not hidden:** the parent reports
`Evidence_ActualBuildRecoveryPreservesOriginalCandidateDiagnostics` line167
failing identically on macOS at bothb1d23d7d and88b5d38d because the recovery
diagnostic list is empty; release Linux CI passed without a waiver. This
continuation did not independently reproduce that baseline result and does not
claim the full291-case harness passed locally. The affected config selection
above is explicitly four cases. No E1 assertion, recovery behavior or skip was
changed to hide the failure; full final-head CI remains a separate required gate.

The prior07dd0158 review pair and36-green CI are not final approval of the
integrated head. Two fresh non-author contexts, actual final-head CI and parent
adjudication are required. No self-merge or issue closure is authorized. The
normal-merge constraint preservingb2c43561 still applies.
