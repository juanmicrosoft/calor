# N5 after accepted N2/N3 integration

Issue #1384 / draft PR #1454. This is source/evidence, not parent acceptance,
activation, a release, or authorization for the implementation author to merge.

| Pin | Commit |
|---|---|
| Accepted combined main | `8f9891a1a07f786a76a293017959c3edf28412bf` |
| Accepted N2 ancestor | `63220eab30c7c8af038efaf06d4cebeae64d3f33` |
| Historical held N5 head | `9eba641032b3ba7b45d37af21fc3c7742a411b3e` |
| One normal integration merge | `8fc73aad5069979d1ec7c936273503c9dd907f47` |
| Immutable measured production | `4aaad31b8b93bc449e04ebda6b184435057a4f96` |

The integration merge's exact parents are held9eba and accepted8f. No draft
source was cherry-picked and no history was rewritten. The continuation
gate was recorded before integration in [#1384](https://github.com/juanmicrosoft/calor/issues/1384#issuecomment-5645156072)
and [N0](https://github.com/juanmicrosoft/calor/issues/1379#issuecomment-5645156181).
The same parent-accepted L-band AI slot and provisional2026-09-12
implementation/review checkpoint apply, not engineer-day equivalence or a
delivery guarantee. Only this owner's capacity row was edited.

## Source reconciliation

The automatic merge retained N2's authoritative native single-member branch,
inferred reference identity carrier, unknown-inference exclusion and nullable
STRING transfer. It retained N3's selected ordinary-call maps, effective
receiving shapes, statement parity and taint consumers. The automatic merge
did not compile: N2's `field` pattern overlapped N5's metadata `field` pattern
(CS0136). Only the N5 pattern was renamed.

Joint controls exposed a second real interface defect: member reads used the
private MetadataBinder context, but `TryResolveBclCall` independently created a
default context to resolve its receiving call. Four annotated-reference fixture
calls therefore lacked actual selected maps. Reusing `binder.Context` fixes this
without changing overload policy, selected mapping, taint analysis, or native
inference. The new fixture consumer lives in an injected, test-only assembly;
it is not new production reference-manifest or whole-program external coverage.

Relative to accepted8f, production changes are confined to Binder,
BoundFieldAccessExpression's internal member-symbol property, and
MetadataBinder's member-read resolver. Scope, MetadataContext and TaintAnalysis
are byte-identical to accepted8f. The machine record pins their hashes.

## Joint controls and retained limitations

The original144 compiler and3 editor N5 cases remain. The52 new compiler
controls exercise interfaces, rather than adding new language-policy scope:

| Cases | Source-level observation |
|---|---|
| 32 | Nullable/non-null actual BCL Environment and DirectoryInfo properties, plus explicitly test-only scalar and nominal field pairs, through one/two inferred locals to explicit initialization and native return. Every local and receiving expression retains the actual Roslyn identity and exact annotation. Fixture fields include inherited instance STRING and inherited static DirectoryInfo cases. |
| 4 | Actual DirectoryInfo.Parent/Root through two locals into a named/reordered native input, in statement and expression forms. Assertions identify the selected native declaration, supplied-to-formal map, source span and analysis-only finding. |
| 8 | Actual Environment.ProcessPath or String.Empty through two locals into named/reordered or expanded Path.Combine, both call forms. The selected metadata indices and scalar STRING effective receiving targets are asserted. Runtime10.0.11 selects a ReadOnlySpan-based params overload for the expanded form; this is not normal-array transfer or #1444 completion. |
| 4 | Test-only inherited DirectoryInfo field pair into a test-only named/reordered metadata consumer, with an omitted optional third parameter. Member and selected method use the same actual private context; the symbol belongs to CalorN5AnnotatedFixture. |
| 4 | Actual Encoding.UTF8 through two locals into named/reordered File.ReadAllText. Both call forms retain formal roles and distinguish tainted versus constant path inputs. No filesystem API is executed. |

Actual BCL nullable/non-null field pairs are still **not** claimed: String.Empty
is the actual non-null field control; annotated nullable/non-null field pairs
come from the explicitly emitted reference fixture. Actual BCL property pairs,
static/instance/inherited forms and qualified/mapped-short/unresolved receivers
remain covered by the original symbol-asserting controls.

Existing unresolved receiver/member, wrong access form, inaccessible/getter-less,
value/array/generic result, foreign identity and missing-annotation controls run
unchanged. Missing metadata remains Oblivious; unresolved reads keep resolution
diagnostics and are not counted safe. No global unique-short-name fallback,
Option unwrap, nominal Oblivious widening, native STRING applicability repair,
constructor-input/general-write/global-flow feature or policy activation is
included. N2's unknown inference, unsupported nominal ternary and pre-existing
direct-BCL-to-native0208 distinction remain governed by accepted N2's evidence.

The16 actual-BCL local-chain API counterparts and4 native-input API
counterparts explicitly use `EnforceEffects=false` and `StatusWriter=TextWriter.Null`.
They are **not default production-admissibility evidence**. Fixture-only rows
exercise the injected binder, not Program.Compile's default reference set.
The original36 default API controls still run: all scalar counterparts remain
analysis-only, while four nominal counterparts retain their Calor0410 failures.
Their separate effects-disabled emitted-code compilation remains separately
labeled. Stage A receives actual scalar source coverage, not activation;
Stage B acceptance remains #1402.

## Actual integrated observations

[observations.json](observations.json) contains exact coordinates, member
symbols, annotations, source/profile hashes, test counters and retained red
failures. These measurements were taken after freezing4aaa, not obtained by
adding previously reported counts.

| Observation | Actual result |
|---|---|
| Full compiler project | 9245 total:9242 passed,3 registered skips,0 failed |
| Full editor project | 509 passed,0 skipped/failed |
| Scoped member/N1/N2/N3/nullability/taint/catalog selection | 595 passed |
| Existing corpus/volume/effect/catalog regeneration | 37 passed |
| Ordinary post-audit member/corpus/volume/effect/catalog/stamp selection | 236 passed |
| Paired audit captures | One base and one candidate case, each passed |
| Structured error-emission catalog | 33 routes, byte-identical to accepted8f;275 retained |
| Corpus | All364 keys and source/opacity/outcome/propagation coordinates retained |
| Calor0270 | 313 diagnostics in37 of364 bound modules |
| Calor0425 | 117 diagnostics,47 affected,326 enforced modules |

Selections overlap and must not be summed. The harness291 pin is unchanged;
no local .NET8 execution is claimed. Normal final-head CI supplies platform and
consumer evidence separately.

The existing corpus writers regenerated the actual combined tree. Nine raw
error count/hash pairs differ from accepted8f; eighteen analysis findings are
removed. Seventeen previously undefined actual non-null BCL reads resolve;
one dependent273 disappears when ToString(Environment.NewLine) selects the
native STRING overload. Exact sorted diagnostic identities reproduce both
the accepted-base and candidate hashes. No propagated error is removed.
Four inherited1:1 lowered spans remain honestly represented in the record.

N3's JsonValueFormatter55-error count/hash and its scalar statement findings
remain unchanged relative to accepted8f. Its unresolved nominal `any` case is
not reclassified safe. Unsupported value/generic reads still preserve their
old errors while adding the original15 explicit0270 Infos.

The effect writer reran its real CLI cross-check. Compared with historical
N5a6a/9eba, **only the measured commit stamp changes**. Compared with accepted8f,
MediatR's historical raw-bag denominator changes28 to29 and raw rejects8 to7;
the actual production denominator remains34. FluentValidation0411 sites change
7932 to7931. The resolved native call removes unknown0410/unresolved0411 and
adds0419 **Assumed, not Verified**. All other numerical coordinates, exclusions
and the actual CLI block remain unchanged. The existing product ledger's index
entry is updated to4aaa; other owners' ledger entries are untouched.

The actual private host profile has168 manifest-filtered runtime TPA references,
with the same file hashes as the retained [historical profile](../metadata-profile.json).
This is macOS ARM64, SDK10.0.400, runtime10.0.11, Roslyn5.3.0, not an SDK
reference-pack or test-fixture profile.

## Reproduction and retained attempts

Use existing .NET/xUnit tooling, the initialized pinned corpus submodules, and
ordinary external temporary-directory layout. Do not set TMPDIR inside the repo.
Native Z3 assets were already installed after the initial missing-asset failure.

The full runs used `dotnet test tests/Calor.Compiler.Tests --no-build` and
`dotnet test tests/Calor.LanguageServer.Tests`, with TRX loggers and separate
results files. Compiler discovery listed9125 rows before adding joint tests;
deferred theory discovery is not an executed inventory. The manifest uses
the actual full-run9245/509 totals, retaining existing skips and other projects.

Regeneration used one filtered compiler invocation with
`CALOR_UPDATE_BINDER_BASELINE=1`,
`CALOR_REGENERATE_CALOR0270_LEDGER=1`,
`CALOR_REGENERATE_CALOR0425_LEDGER=1` and
`CALOR_UPDATE_BINDER_ERROR_EMISSION_CATALOG=1`, selecting
BinderIncompleteRatchetTests, Calor0270CorpusVolumeTests,
Calor0425CorpusLedgerTests and BinderErrorEmissionCatalogTests.
Only the0425 measured stamp changed from the automatic combined baselines.

For the diagnostic-identity audit, reuse the byte-identical
[N5CorpusAudit.cs.txt](../N5CorpusAudit.cs.txt) in a separate scratch directory
as `N5CorpusAudit.cs`. Copy this directory's [N5Audit.targets](N5Audit.targets)
there. Export accepted8f versions of Binder, BoundNodes and MetadataBinder as
`n5-base-Binder.cs`, `n5-base-BoundNodes.cs`, `n5-base-MetadataBinder.cs`, and
its coverage ledger as `n5-base-coverage.json`. These are exactly the three
production files differing from accepted8f. No tracked file is swapped.

Set N5_REPO, N5_SCRATCH and N5_OUTPUT. Run `dotnet build
tests/Calor.Compiler.Tests -t:Rebuild
-p:CustomBeforeMicrosoftCommonTargets=SCRATCH/N5Audit.targets -p:N5Baseline=true`,
then `N5_EXPECT_MEMBER_API=0 dotnet test tests/Calor.Compiler.Tests --no-build
--filter FullyQualifiedName~N5CorpusAudit`. Rebuild with N5Baseline=false and
capture with N5_EXPECT_MEMBER_API=1. Both legs assert their expected API identity.
Finally rebuild without the import; the ordinary236-case selection above ran
after that cleanup. No project import or injected test participates in CI.

Retained attempts: the automatic merge CS0136 failure;52 joint controls with
44 passes and8 failures before repair; four fixture corrections for actual
ReadOnlySpan params selection and four production shared-context repairs; and
an audit command rejected before execution because `dotnet test -t:Rebuild`
parses `-t` as list-tests. The corrected audit uses `dotnet build -t:Rebuild`
before the no-build test. Neither the rejected command nor discovery counts
is presented as passing execution evidence.

Two separately initiated non-author integration/adversarial reviews must read
the final evidence head and actual combined base. Exact prompts, tool/model
provenance, commands, limitations, findings/dispositions and final CI results
are recorded on PR1454 without changing reviewed source. Historical9eba reviews
are retained but are not reused as combined approval. Parent adjudicates.
