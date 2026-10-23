# N2: inferred-local and native-member reference annotations

Issue #1381, under #1082. **Implementation/evidence checkpoint, not review
approval, production activation, or a release.**

- Accepted dependency/base: `3b513a632149290481161b195a71aa6cb0e251a8`
  (#1380 / PR #1451).
- Current measured source: `3f2712449152361d230c71c23b75f0570d356e0a`.
  Initial implementation `1b60a8dc0273af04ffbf8fe649a4b400c57ca825` and
  blocked review head `4bb47d54f826a88e16a026c811a7efd4e71e4127` remain
  ancestors. The STRING review fix is `37581f6c6459ccdea6393a8629029a4c7c1966ab`.
- Capacity: [issue record](https://github.com/juanmicrosoft/calor/issues/1381#issuecomment-5644059937)
  and [N0 record](https://github.com/juanmicrosoft/calor/issues/1379#issuecomment-5644060010).
- [Machine-readable observations and actual metadata profile](observations.json).
- [Measured pre-existing native-input distinction](native-input-comparison.json).

## Mechanism and compatibility

Inferred locals now retain the initializer's established annotation and actual
native declaration or Roslyn type identity. `VariableSymbol.InferredReferenceType`
is internal; `IsTypeInferred` distinguishes an unresolved inference from an
explicit declaration. This prevents a callee's short display name from being
re-resolved as a different type in the caller's namespace, and prevents an
unmodeled expression from acquiring a reference identity merely from its display.

Explicit declared annotations still win over initializer annotations. Existing
constructor-result NotAnnotated is retained, not reimplemented: the inference
helper recovers its actual type identity and copies that existing annotation.
Known primitive/value results and unresolved/generic sources do not acquire a
reference identity from this mechanism.

Scalar STRING inference uses the existing declared-nullability shape parser,
including nullable aliases, and copies the initializer's actual annotation.
It does not turn nullable spelling into a NotAnnotated guarantee or reinterpret
runtime Option. This closes the independently reported member-to-local gap.

Already-resolved native field/property reads through `this` and `base` now carry
the same declared annotation and underlying identity as bare reads. The existing
`BoundFieldAccessExpression.resolvedType` parameter supplies this information.
Member identity is resolved in the declaring type's context, not a shadowing
generic method's lexical context. Ambiguous/unresolved member selections do not
select an arbitrary reference identity.

Existing public constructor signatures, display projection, annotation-sensitive
type equality/hash and symbol IDs remain unchanged. Field access keeps its
original declared display spelling. No AST, emitter, diagnostic route, overload
applicability, effect row, runtime Option representation, or proof/runtime guard
is changed. The annotation channel intentionally changes at the repaired reads.

## Coverage and explicit limits

| Surface | Current observation / disposition |
|---|---|
| Native constructor and native return sources | Direct, one-local and two-local paths to explicit initialization, native return and resolved ordinary expression-call inputs preserve NotAnnotated / Annotated as appropriate. Actual native declarations are asserted. |
| BCL nominal return sources | Actual `Directory.CreateDirectory` and `Directory.GetParent` symbols survive direct/one-local/two-local receiving initialization/return and resolved `FileSystemAclExtensions.GetAccessControl` input. Additional native-input controls characterize direct/one-local/two-local nullable and non-null behavior separately below. These filesystem APIs are bound, **not executed**. |
| BCL STRING transfer | Nullable `Environment.GetEnvironmentVariable` and non-null `Directory.GetCurrentDirectory` retain their exact annotations through two inferred locals into return. The former is the N0 `bind-inferred-local` shape, no longer degraded to Oblivious. |
| Native field/property reads | Nullable/non-null nominal and STRING declarations have matching annotations through bare/this/base reads; native member symbol IDs agree. Two-local STRING member chains, native parameters and returns cover both aliases and prefix/suffix nullable forms. These are already-resolved native members, not arbitrary receiver/member chains. |
| Namespace identity | Two inferred locals keep producer `A.Foo` even inside caller `B.Foo`. Matching versus unrelated receiving types are distinguished. These combine explicitly namespace-tagged ASTs; they do not establish converter namespace round-trip correctness or unrelated-type assignment acceptance. |
| Declared targets | Explicit `Foo` / `?Foo` annotations remain authoritative when initialized from a non-null constructor; subsequent inferred locals preserve that declaration. No representation unwrap is introduced. |
| Corpus-derived native shape | Reduced `_top` / `_current` nullable-interface field reads from Serilog `Context/EnricherStack.cs` are exercised through native `this`, two locals and a receiving return, with nullable/non-null controls. A module-local interface declaration intentionally establishes identity in the reduced fixture. The unmodified original per-file corpus still lacks that external interface; it is **not** relabeled resolved or safe. |
| Plain nominal ternary conditional | **Unsupported transfer**, explicitly retained under #1381's allowed conditional disposition. Both known non-null arms and a nullable/non-null pair currently produce Oblivious inference; neither becomes NotAnnotated or a fabricated resolved identity. This is not a nullability/safety classification. #1402 must retain this source-shape limit rather than count missing findings as coverage. Coalesce/throw/typed-pattern work remains owned by #1398. |
| Native input applicability / staging | Direct nullable BCL `DirectoryInfo` returned into a native `DirectoryInfo` parameter retains existing Calor0208. Through one/two inferred locals, the call **already resolved and compiled at the base**; N2 preserves Annotated and adds raw analysis-only Calor0274 without changing public compilation acceptance. Non-null controls pass at every depth. This is not a universal native-input rejection or production-enforcement claim. Expanded native-argument limits from N1 likewise remain. |
| Other exclusions | Unresolved external receivers, arbitrary generic payloads, value/enum/struct references, native receiver chains not already resolved, indexers, member writes, constructor inputs, general assignments/rebinding and global flow narrowing are not added. BCL member reads belong to #1384. |

These are declaration/inference annotations, **not** a proof that a mutable
variable stays non-null after later assignments. Nominal Oblivious policy is
unchanged and still separately gated by #1400. Production nominal rejection
remains gated by #1402; scalar activation/acceptance remain #1385/#1386.

Six real LSP/API controls cover a nullable parameter and native field through
two locals into return for Foo, string and str. The editor reports Calor0273
with the actual nominal/scalar shape, exact Annotated provenance, the
`calor (analysis only)` source and actual range; an edit to a nullable receiving
return clears it. Default compilation remains accepted and does not propagate
that binder diagnostic. This does not claim production enforcement.

## Actual corpus and metadata observations

The existing `BinderIncompleteRatchetTests` ran with all three pinned corpus
submodules initialized, **8 passed / 0 skipped** in the initial corpus run and
again within the final compiler selection. Both
`binder-incomplete-baseline.json` and `binder-source-coverage.json` are
byte-identical to the accepted base; their SHA-256 values are in
`observations.json`. All 364 source keys and recorded diagnostic/source/opacity
coordinates remain unchanged. No baseline regeneration or injected corpus
instrumentation was used for N2. There is no new corpus-success or safety claim.

The final compiler selection also executes N1's actual private MetadataBinder
profile capture: 168 selected references, with their actual file hashes recorded
in `observations.json`. This is the current macOS ARM64 / SDK 10.0.400 /
runtime 10.0.11 / Roslyn 5.3.0 context, not a reference-pack guess or a claim
that every CLI/MSBuild/platform host has an identical profile.

## Reproduction and scoped outcomes

Commands below ran in the isolated `local-annotations-1381-review-fix` issue
worktree at source `3f271244`; the original `local-annotations-1381` worktree
was held at4bb while the independent original-head reviews completed.
Standard compiler and editor outputs had already been built at this code state;
their final scoped runs used `--no-build`. The remaining selected projects were
built by their commands. The final source/outputs do not contain the temporary
native-input comparison injection. No full-project local inventory or platform-consumer
success is inferred from these scoped selections; final-head CI is required.

```bash
dotnet test tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj --no-build --filter 'FullyQualifiedName~LocalReferenceAnnotationTests|FullyQualifiedName~NominalReferenceIdentityTests|FullyQualifiedName~Nullability|FullyQualifiedName~NullableReferenceTyping|FullyQualifiedName~BoundTypeArchitectureTests|FullyQualifiedName~ClassMemberBindingTests|FullyQualifiedName~SymbolAndOverloadBindingTests|FullyQualifiedName~BinderIncompleteRatchetTests|FullyQualifiedName~BinderErrorEmissionCatalogTests|FullyQualifiedName~BindingDiagnosticPolicyTests'
dotnet test tests/Calor.LanguageServer.Tests/Calor.LanguageServer.Tests.csproj --no-build --filter 'FullyQualifiedName~NullableReferenceTypingTests|FullyQualifiedName~DocumentStateTests|FullyQualifiedName~BindingDiagnostic|FullyQualifiedName~RenameHandlerTests|FullyQualifiedName~DefinitionHandlerTests'
dotnet test tests/Calor.Conversion.Tests/Calor.Conversion.Tests.csproj --filter 'FullyQualifiedName~SnapshotConversionTests|FullyQualifiedName~RoundTripTests'
dotnet test tests/Calor.Ids.Tests/Calor.Ids.Tests.csproj --filter 'FullyQualifiedName~IdScannerTests'
dotnet test tests/Calor.Enforcement.Tests/Calor.Enforcement.Tests.csproj --filter 'FullyQualifiedName~EffectResolverTests|FullyQualifiedName~EffectRowLatticeTests'
```

| Selection | Passed | Failed / skipped |
|---|---|---|
| Compiler, typing/identity/overloads, routing/catalog and actual corpus ratchets | 434 | 0 / 0 |
| LSP analysis/routing, document state, rename and definition | 103 | 0 / 0 |
| Conversion snapshots and round trips | 280 | 0 / 0 |
| ID scanner | 9 | 0 / 0 |
| Effect resolver and row lattice | 204 | 0 / 0 |

Selections overlap; do not sum them as a unique denominator. The change adds
101 compiler cases and 6 editor cases. Expected inventory becomes compiler
8957 and LSP504, with all other inventory/skip pins unchanged.

Development history is retained in session evidence:

- The fresh worktree first failed on missing managed/native Z3 assets. Existing
  assets were copied from the accepted N1 worktree only after those failures,
  then checked against the repository's committed size/hash pins.
- Initial 53-case matrix at the accepted base: 39 failed / 14 passed. Thirty
  failures were inferred-local annotation loss, eight native-member annotation
  loss, and one the pre-existing unresolved BCL-to-native input above. The final
  input matrix uses a resolved BCL consumer and pins the unresolved native case
  separately; that fixture adjustment is not an overload repair.
- First repair selection: 249 passed. An expanded selection then had
  296 passed / 1 failed: an explicitly nullable nominal declaration lost its
  annotation through the legacy expanded display projection. The known-reference
  branch now copies the variable's declared annotation without changing display
  or applicability. The repaired broader selection passed373 before the final
  pinned measurements above.

## Independent review findings and source-attributed disposition

The original head4bb is **blocked historical evidence**, despite its36 successful
CI checks. Those checks do not accept the repaired source.

Integration reviewer `765b467c-613b-4a9e-b599-5c0381009d69` found a real
acceptance gap: native nullable STRING members were Annotated directly, but
became Oblivious after inferred-local transfer. The alias fallback only
recognized bare STRING. The author reproduced25 targeted cases at unchanged
4bb production:16 failed /9 passed. Fix37581 uses the existing scalar shape
parser and copies the actual initializer annotation. It adds native
field/property bare/this/base, parameter and return alias controls, plus a
runtime Option exclusion. Four additional editor rows check exact Annotated
provenance. These are included in the current101 compiler /6 editor additions.

Adversarial reviewer `ae2c48f6-760e-425d-99ee-803df392e6ea` separately
observed that a nullable BCL nominal input rejects directly but resolves and
compiles through locals. The reviewer had not executed the pre-N2 base.
The author then ran an identical three-depth observation source at actual
base3b5 and head4bb, through the existing xUnit runner in separate worktrees.
Both runs asserted the actual nullable DirectoryInfo metadata symbol and used
default `Program.Compile` options with explicit effect rows.

| Depth | Base3b5 raw binding | Head4bb raw binding | Public compiler API / generated C# |
|---|---|---|---|
| 0 | Unresolved native call; blocking0208; Annotated argument | Identical | Both reject; no output |
| 1 | Resolved native call; Oblivious argument; no raw nullable diagnostic | Same resolution/display; Annotated argument; analysis-only0274 | Both accept; generated C# byte-identical |
| 2 | Resolved native call; Oblivious argument; no raw nullable diagnostic | Same resolution/display; Annotated argument; analysis-only0274 | Both accept; generated C# byte-identical |

[Full sources, hashes, diagnostics and compared outcomes](native-input-comparison.json)
make the distinction source-attributed: N2 does **not** relax an existing
through-local blocking gate. Native overload selection reads
`argument.Type.DisplayString`; the pre-existing variable projection already
stripped the nullable decorator. N2's change is retained annotation and an
additional raw diagnostic, not new applicability or new public acceptance.
The direct-only rejection must not be described as a blanket rejection across
all depths. Current six-row native-input controls cover both nullable and
non-null sources at depths0/1/2 and retain the actual analysis-only stage.

This was a **three-case controlled comparison, not a corpus audit**. Its
session-only source was injected into the existing xUnit runner with
`CustomAfterMicrosoftCommonTargets` at both exact commits; it adds no committed
hook or dependency. The current final measurements in the repair worktree are
standard tests without that injection. The pre-existing production distinction
remains a Stage B coverage/acceptance concern, not a non-null guarantee; it does
not justify premature overload rewriting or diagnostic activation in N2.

Two separate non-author final-head re-reviews, exact review provenance, material
feedback disposition, final CI and parent-delegated adjudication are still
required in the issue PR. AI contexts are not human/statistically independent.
The full research pause and all guard/activation/release boundaries remain.
