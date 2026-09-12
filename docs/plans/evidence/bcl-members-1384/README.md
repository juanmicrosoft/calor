# N5 scalar BCL member annotations (#1384)

Base: `3b513a632149290481161b195a71aa6cb0e251a8` (accepted N1 #1451).
Production: `a6a87f3975f85cead21d89d3d129690413309e78`.
Initial production checkpoint: `60ad2e08cebee26a8e4188fc76a9bba32912ab56`.
Capacity-only checkpoint: `2c8376ba`.
Measured2026-09-12, macOS ARM64, SDK10.0.400, runtime10.0.11, Roslyn5.3.0.
Product version remains0.21.0; these are unreleased0.22 observations.

The [live accepted capacity](https://github.com/juanmicrosoft/calor/issues/1384#issuecomment-5644205238)
is one L-band bounded AI slot, not engineer-day equivalence or a delivery
guarantee. Provisional implementation/review checkpoint:2026-09-12. Two
separately initiated non-author final-head review contexts, normal CI and
parent adjudication are required. This record does not authorize merge,
issue closure, activation, release, nominal Oblivious widening or research.

## Production change

`Binder` routes both packed dotted references and explicit `FieldAccessNode`
expressions through one metadata member-read path. It only admits a resolved,
non-generic scalar reference receiver/result. The existing native `this`/`base`
branch remains authoritative; N2 owns its annotation transfer and inferred locals.

`MetadataBinder.ResolveMemberRead` asks Roslyn to resolve the actual static or
instance access. Roslyn owns inherited lookup, accessibility and getter
availability. The probe must resolve its receiver to the supplied type symbol,
not a same-named type from another assembly. A resolved bound member carries
the actual property/field symbol and its declared result symbol/annotation.
Probe flow state is not substituted for declaration nullability.

No new public constructor, AST node, type-display/equality policy, implicit
Option conversion, predicate widening, diagnostic activation or runtime/proof
guard is introduced. The receiver does not become known merely because its
spelling looks like a type. Native declarations and value receivers shadow
short BCL names; unknown short names are not resolved by globally unique tails.
The existing explicit BCL alias table supplies the tested short forms.

Unsupported resolved-receiver member reads have `UnresolvedBoundType` and
Calor0270 Info. Packed references additionally retain their existing Calor0200
resolution findings; neither a missing finding nor an OBJECT fallback is
counted as safety. The initial60ad implementation dropped those legacy findings;
the exact corpus audit exposed that and a6a restored them before final review.

## Acceptance matrix

Each positive matrix row asserts the real property/field symbol, its kind,
staticness where applicable, declared type, nullable annotation and reference
assembly membership. Nullable negatives assert diagnostic span and the actual
resolved method-result symbol at input consumers, not merely a diagnostic
count. All three boundaries mean explicit non-null initialization, native
return, and a resolved ordinary method input.

| Source | Actual resolution and coverage |
|---|---|
| BCL static nullable/non-null STRING properties | `System.Environment.ProcessPath` / `CurrentDirectory`; all three boundaries. Both fully qualified and mapped-short `Environment` forms. |
| BCL instance nullable/non-null STRING properties | Declared `System.Exception` parameter, `HelpLink` / `Message`; all three boundaries. Mapped-short `Exception` parameter also covered. Explicit spaced-dot and packed-dot syntax share the path. |
| BCL inherited property | `ArgumentException.HelpLink` resolves the actual property declared on `System.Exception`; all three boundaries. |
| BCL static reference field | Actual `System.String.Empty`, NotAnnotated STRING, all three boundaries. It is not claimed as a nullable field pair. |
| BCL nominal property pair | Declared fully qualified `System.IO.DirectoryInfo` receiver, nullable `Parent` / non-null `Root`; all three raw-binder boundaries. Resolved input is `FileSystemAclExtensions.GetAccessControl`. |
| Controlled annotated reference fixture, **not BCL** | An in-memory emitted `CalorN5AnnotatedFixture` assembly supplies nullable/non-null STRING and `DirectoryInfo` property and field pairs, static and instance, inherited through `N5Fixture.Derived`. All three consumers:48 cases. The fixture is inserted into the test Binder's actual host using the existing internal context, not added to production reference manifests. |
| Controlled missing annotations | `#nullable disable` fields/properties preserve Roslyn None as Oblivious. STRING retains its existing conservative receiving checks; nominal Oblivious is not widened. |
| Invalid/unsupported external reads | Instance member through a type, static member through a value, private field/getter, write-only property, missing member, value result, array result, generic result: explicit unresolved result and retained diagnostics. No nullability pass claim. |
| Unresolved/short names | Missing receiver, missing receiver type and unmapped short `Members` names remain unresolved with Calor0200. A real variable named `Environment` is not replaced by the BCL static type. Same-named foreign-assembly receiver fails identity validation. |

The STRING consumer is actual `System.Int32.Parse(string)`. No filesystem or
environment operation is executed by metadata resolution. Constructor inputs,
member writes, indexers, arbitrary/nested generics, nullable-receiver flow
proof, flow attributes, chained-call receiver expansion and project-wide
external reference discovery are excluded. N2 inferred-local behavior is not
claimed by these direct member controls.

## Production, editor and activation ownership

There are36 actual-BCL raw-binder cases and their36 production counterparts.
Default scalar production succeeds without0272/0273/0274; these diagnostics
remain analysis-only. Three editor cases assert the scalar receiving shape,
exact source span, `calor (analysis only)` label and clearing on a non-null
property edit.

Four nominal default-API counterparts still fail the existing effect gate:
the three `DirectoryInfo.Root` boundaries and the `Parent` input to
`GetAccessControl` have an unmodeled effect and Calor0410. Those failures are
asserted and retained, not described as nullability rejection or safe default
compilation. A separately labeled `EnforceEffects=false` invocation validates
generated C# for every counterpart; it is not default-mode evidence. Scalar
default cases do not need this effect opt-out.

The actual scalar STRING source forms above are supplied for Stage A, rather
than excluded merely because metadata-only tests passed. #1385/#1386 still own
activation/adjudication. #1402 must replay the nominal source counterparts,
default-effect limitations, modes/caches and corpus gates after its separate
policy prerequisites. Neither stage is activated here.

## Reference provenance

[metadata-profile.json](metadata-profile.json) lists all168 reference filenames
and SHA-256 values from the actual production Binder's metadata host.
`System.Environment`, `System.Exception`, `System.String` and `DirectoryInfo`
resolve in `System.Private.CoreLib, Version=10.0.0.0`, public key token
`7cec85d7bea7798e`. This is manifest-filtered runtime TPA metadata, not an SDK
reference pack or a claim about other hosts. The controlled fixture adds one
test-only emitted reference and is not mixed into that168-reference profile.

## Corpus reconciliation

The existing `CALOR_UPDATE_BINDER_BASELINE=1` writer regenerated the actual
candidate, without replacing another owner's baseline or adding independent
counts. [corpus-delta.json](corpus-delta.json) compares every coordinate over
all364 source keys. Only raw binding error count/hash pairs change in9 rows.
All source/converted-source identities, opacity, conversion success,
propagated-error hashes/counts and denominators are unchanged.

The18 removed raw analysis findings are17 previously undefined references now
resolved to non-null BCL members (`Task.CompletedTask`, `Environment.NewLine`,
`Exception.Message`), plus one dependent273 on the native
`ValidationResult.ToString(Environment.NewLine)` call. Its real STRING argument
now selects the STRING overload; the original source declares a STRING return.
No claim is made that these previously partially converted files are now safe
or fully converted. Four lowered expression spans were already1:1 in the
converter output; the evidence retains those coordinates rather than inventing
accurate source mappings.

Unsupported value/generic member rows retain their old raw error coordinates.
They additionally report explicit incomplete metadata status; that editor-noise
delta is separately measured by the existing0270 diagnostic-volume runner.
The existing0425 effect runner also measures the legacy raw-bag denominator,
which changes when `INotificationHandler`'s `Task.CompletedTask` stops being an
undefined reference. It is separate from the production propagated-error gate.
The regenerated0270 volume is313 diagnostics in37 of364 modules, previously
298 in35. Its15 added Infos are12 MediatR,2 Serilog and1 FluentValidation:
unsupported value/generic member results, not nullable-reference findings.
The existing packed-reference errors at these sites remain.

The regenerated0425 record retains117 diagnostics in47 of326 enforced modules.
MediatR's historical raw-bag denominator changes28 to29, with raw-bag rejects8
to7; the actual propagated-error production denominator stays34. Its CLI
cross-check was rerun by the existing writer and is byte-identical.
FluentValidation's0411 count changes7932 to7931: the exact comparison for
`ValidationResult.ToString(Environment.NewLine)` removes an unresolved-target
0411 and its dependent unknown-effect0410, replacing them with0419 **assumed,
not verified** effects for the resolved native call. This is recorded in the
delta JSON; it is not a claim of proven purity or a widened waiver.

For exact raw comparison, a session-only xUnit source injection uses the same
conversion options as `BinderIncompleteRatchetTests.MeasureNative` against both
pinned production sources. The three changed compiler files are substituted
from the base commit for the base run. Both sides are explicitly rebuilt, and
the audit asserts presence/absence of the new member-symbol API to prevent
stale-assembly reuse. Every measured raw count/hash is checked against the
existing ratchet record, and converted Calor hashes match between sources.
The audit source/targets hashes are in the JSON. Standard builds omit this
injection.

The committed `N5CorpusAudit.cs.txt` and `N5Audit.targets` are evidence-only
inputs for the existing xUnit runner. Copy them to a scratch directory,
renaming the text artifact to `N5CorpusAudit.cs` there, then retrieve the
base versions of `Binding/Binder.cs`, `Binding/BoundNodes.cs` and
`Binding/Metadata/MetadataBinder.cs` with `git show` into
`n5-base-Binder.cs`, `n5-base-BoundNodes.cs`, `n5-base-MetadataBinder.cs` there,
and retrieve the base coverage JSON into `n5-base-coverage.json`.
Set `N5_REPO` to this worktree, `N5_SCRATCH` to that scratch directory and
`N5_OUTPUT` to the desired JSON output. Use
`dotnet build tests/Calor.Compiler.Tests -t:Rebuild
-p:CustomBeforeMicrosoftCommonTargets=SCRATCH/N5Audit.targets
-p:N5Baseline=true`, then
`N5_EXPECT_MEMBER_API=0 dotnet test tests/Calor.Compiler.Tests --no-build
--filter FullyQualifiedName~N5CorpusAudit` for the base. Rebuild with
`-p:N5Baseline=false` and use `N5_EXPECT_MEMBER_API=1` for the candidate.
Finally rebuild without the import. The injection is never committed to a
project or used in CI.

## Executed observations and retained attempts

The first build stopped at the actual missing-Z3-asset error. Only then was the
existing download script run. A test-import ambiguity was corrected before
the first executable baseline: all24 initial actual-BCL cases failed on
unchanged N1 production. The first member-node-only fix still failed those24
packed-reference cases. Routing packed references through the same binder
fixed them; expanded cases then exposed test-fixture effect expectations and
private-metadata import details, which were corrected without changing
production effect policy.

The final source has144 new compiler cases and3 new editor cases. Actual full
inventories are9000 compiler and501 editor, not sums borrowed from N2.
The full compiler run reported8995 passed,3 registered skips and2 exact
diagnostic-ledger mismatches; the full editor run passed501. The affected
ledgers were regenerated through their existing writers, including the real
CLI cross-check. The final combined member/corpus/ledger/catalog selection
passed181 cases with0 skipped, without hiding the initial red attempt.
The13-case structured error-emission catalog passes unchanged:
no Error-capable sink or activation route was added.

An initial paired audit reused the baseline assembly incrementally and was
rejected as evidence. Explicit rebuilds and the assembly-API assertion replaced
that attempt. No .NET8 runtime/roll-forward, all-project runtime harness result,
research collection or zero-unknown claim is inferred from these observations.
Final-head review records and actual normal CI belong to the issue-scoped PR.

The first final-head integration review blocked the evidence source's `.cs`
extension under `docs/`: the repository's Calor-first guard correctly rejects
new C# files outside its approved roots. It is now a `.cs.txt` evidence
artifact, never compiled from the repository, with unchanged content/hash.
The first CI compiler job separately caught a stale0425 entry in the
commit-stamp index after the actual ledger regeneration. Only that product
ledger's entry is updated to its measured a6a source; other entries are
unchanged. Both first-head failures remain recorded on the PR and require
fresh final-head reviews and CI, not an approval carried across the repair.
