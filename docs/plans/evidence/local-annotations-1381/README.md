# N2: inferred-local and native-member reference annotations

Issue #1381, under #1082. **Implementation/evidence checkpoint, not review
approval, production activation, or a release.**

- Accepted dependency/base: `3b513a632149290481161b195a71aa6cb0e251a8`
  (#1380 / PR #1451).
- Measured implementation: `1b60a8dc0273af04ffbf8fe649a4b400c57ca825`.
- Capacity: [issue record](https://github.com/juanmicrosoft/calor/issues/1381#issuecomment-5644059937)
  and [N0 record](https://github.com/juanmicrosoft/calor/issues/1379#issuecomment-5644060010).
- [Machine-readable observations and actual metadata profile](observations.json).

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
| BCL nominal return sources | Actual `Directory.CreateDirectory` and `Directory.GetParent` symbols survive direct/one-local/two-local receiving initialization/return and resolved `FileSystemAclExtensions.GetAccessControl` input. These filesystem APIs are bound, **not executed**. |
| BCL STRING transfer | Nullable `Environment.GetEnvironmentVariable` and non-null `Directory.GetCurrentDirectory` retain their exact annotations through two inferred locals into return. The former is the N0 `bind-inferred-local` shape, no longer degraded to Oblivious. |
| Native field/property reads | Nullable/non-null nominal and STRING declarations have matching annotations through bare/this/base reads; native member symbol IDs agree. These are already-resolved native members, not arbitrary receiver/member chains. |
| Namespace identity | Two inferred locals keep producer `A.Foo` even inside caller `B.Foo`. Matching versus unrelated receiving types are distinguished. These combine explicitly namespace-tagged ASTs; they do not establish converter namespace round-trip correctness or unrelated-type assignment acceptance. |
| Declared targets | Explicit `Foo` / `?Foo` annotations remain authoritative when initialized from a non-null constructor; subsequent inferred locals preserve that declaration. No representation unwrap is introduced. |
| Corpus-derived native shape | Reduced `_top` / `_current` nullable-interface field reads from Serilog `Context/EnricherStack.cs` are exercised through native `this`, two locals and a receiving return, with nullable/non-null controls. A module-local interface declaration intentionally establishes identity in the reduced fixture. The unmodified original per-file corpus still lacks that external interface; it is **not** relabeled resolved or safe. |
| Plain nominal ternary conditional | **Unsupported transfer**, explicitly retained under #1381's allowed conditional disposition. Both known non-null arms and a nullable/non-null pair currently produce Oblivious inference; neither becomes NotAnnotated or a fabricated resolved identity. This is not a nullability/safety classification. #1402 must retain this source-shape limit rather than count missing findings as coverage. Coalesce/throw/typed-pattern work remains owned by #1398. |
| Native input applicability limit | Direct nullable BCL `DirectoryInfo` returned into a native `DirectoryInfo` parameter retains existing Calor0208 and no resolved Calor0274. The final BCL input matrix instead asserts a genuinely resolved BCL consumer; the native limitation has its own explicit regression. Expanded native-argument limits from N1 likewise remain. |
| Other exclusions | Unresolved external receivers, arbitrary generic payloads, value/enum/struct references, native receiver chains not already resolved, indexers, member writes, constructor inputs, general assignments/rebinding and global flow narrowing are not added. BCL member reads belong to #1384. |

These are declaration/inference annotations, **not** a proof that a mutable
variable stays non-null after later assignments. Nominal Oblivious policy is
unchanged and still separately gated by #1400. Production nominal rejection
remains gated by #1402; scalar activation/acceptance remain #1385/#1386.

Two real LSP/API controls cover a nullable parameter and native field through
two locals into return. The editor reports nominal Calor0273 with the
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

Commands below run from this issue worktree at the measured implementation.
Standard compiler and editor outputs had already been built at this code state;
their final scoped runs used `--no-build`. The remaining selected projects were
built by their commands. No full-project local inventory or platform-consumer
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
| Compiler, typing/identity/overloads, routing/catalog and actual corpus ratchets | 404 | 0 / 0 |
| LSP analysis/routing, document state, rename and definition | 99 | 0 / 0 |
| Conversion snapshots and round trips | 280 | 0 / 0 |
| ID scanner | 9 | 0 / 0 |
| Effect resolver and row lattice | 204 | 0 / 0 |

Selections overlap; do not sum them as a unique denominator. The change adds
71 compiler cases and 2 editor cases. Expected inventory becomes compiler
8927 and LSP500, with all other inventory/skip pins unchanged.

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

Two separate non-author final-head reviews, exact review provenance, material
feedback disposition, final CI and parent-delegated adjudication are still
required in the issue PR. AI contexts are not human/statistically independent.
The full research pause and all guard/activation/release boundaries remain.
