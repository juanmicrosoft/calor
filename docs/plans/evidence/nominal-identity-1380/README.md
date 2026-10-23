# N1 nominal identity and return evidence (#1380)

Implementation source: `5fabf1ec4bf21c1d39737369d6d0a8a5364ca347`.
Initial implementation checkpoint: `828f30501f9fad3900dc3b61820e894416eea648`.
Base: merged T1 `e299f2462b813a0c7e7049eb3a66f40b9f95e1c9`.
Measured2026-09-12 on macOS ARM64, SDK10.0.400, runtime10.0.11,
Roslyn5.3.0. The source declares0.21.0; this is unreleased0.22 work,
not behavior in the already-published0.21 package.

The parent accepted this bounded N1 slot in
[#1380](https://github.com/juanmicrosoft/calor/issues/1380#issuecomment-5643434275)
and the [capacity table](../../v0.22-nullability-enforcement-scoping.md#8-dependency-and-accepted-capacity-gate).
Two non-author final-head review contexts, applicable CI and a recorded scope
adjudication are required. This implementation record does not itself grant
those approvals.

## What changed

`NominalBoundType.HasSameUnderlyingReferenceType` compares actual native symbol
IDs or Roslyn symbols, ignoring the reference annotation for this one predicate.
It does **not** change annotation-sensitive `Equals`/`GetHashCode`, display
strings, verifier/cache identities or public constructor signatures. A matching
short name, or absence from a primitive blacklist, is no longer a reference-type
identity.

Native class/interface/delegate registration records known reference kind.
Structs, enums, unresolved names and arbitrary nominal generic spellings are
not admitted by that classifier. Native return and receiving identities use
the declaring context; bare variable reads preserve their existing display and
annotation while adding identity where available. Inferred `OBJECT` fallback
does not acquire a fabricated resolved `System.Object` symbol.
The5fab follow-up excludes the general binder's global unique-short-name
fallback: a lone `A.Foo` does not resolve an out-of-scope `Foo` receiving name.
Its added namespace regression and refreshed measurements are distinct from
the initial828f checkpoint.

The declared-spelling reader reuses the nullable-annotation helper, distinct
from explicit runtime `Option<T>`. BCL returns keep their actual Roslyn symbol;
resolved nominal BCL parameters retain theirs instead of falling through the
STRING-only shape adapter. Nominal **Oblivious is still excluded**. All
0272/0273/0274 receiving rules remain analysis-only;275 and existing transitional
rejection remain active. No D3/D12/D14 guard or demotion is changed.

## Controls and boundaries

| Surface | Observed behavior / retained boundary |
|---|---|
| Native `?Foo` and `Foo?` return | A resolved static call reaches raw272 initialization,273 return and274 ordinary argument findings. Non-null counterparts remain accepted. Original return display strings are unchanged. |
| Native instance call | A resolved `this.Get` nullable return reaches272. This is not a claim about arbitrary dynamic receivers. |
| Expanded `OPTION[inner=Foo]` annotation spelling | Initialization/return retain that exact display and reach272/273. The existing native argument applicability rejection208 is retained; that unresolved call is not counted as a resolved274 control. |
| Qualified and short native names | Distinct same-short-name namespace declarations are tested. A return declared in `A.Foo` does not adopt the caller's `B.Foo` identity. A synthetic namespace fixture supplies AST namespace metadata explicitly; it is not a converter round-trip claim. |
| BCL nominal return | Actual `System.IO.Directory.GetParent` produces an Annotated `DirectoryInfo` symbol and reaches qualified receiving initialization/return findings. Actual `CreateDirectory` produces the non-null counterpart. |
| Resolved BCL nominal input | Binding `FileSystemAclExtensions.GetAccessControl(Directory.GetParent(...))` reaches274 at the supplied argument. The two returned Roslyn type symbols and diagnostic span are asserted. The filesystem operation is **not executed**. |
| Metadata receiver limits | Current enrichment still requires the existing fully-qualified BCL call path. A short `Directory.GetParent` or variable `directory.CreateSubdirectory` call is not relabeled resolved. Short BCL receiving aliases without actual resolution are not inferred from a unique textual tail. |
| Exclusions | Unresolved/ambiguous declarations, structs/enums, arbitrary generic nominal shapes, cross-assembly lookalikes and unrelated resolved references do not become matching references. The exact-identity predicate does not add a general subtype-conversion policy. |
| Constructor/literal controls | Existing NotAnnotated constructor/literal cases continue to pass. No Option unwrap, nullable-value conversion, default/throw insertion or flow-narrowing rule is introduced. |
| Production/editor ownership | Default compilation still does not propagate nominal273. Generated C# remains independently compilable in the scoped control. The editor marks273 `calor (analysis only)`, preserves its span/context and clears it when the receiving return becomes nullable. |

No absent finding in an unsupported row is a non-null or safety classification.
Local/member annotation propagation, project reference context, selected-call
mapping and Stage A/B activation retain their separate owners and gates.

## Exhaustive annotation-kind disposition

`NullabilityChecker.GetAnnotation` now returns a nullable enum: null means
**unsupported**, not `NotAnnotated` and not an invented `Oblivious` state.
The six concrete BoundType kinds are checked against reflection so an added
kind cannot silently escape this table.

| Kind | Disposition |
|---|---|
| Nominal | Read its carried annotation. This alone does not establish resolved reference identity; the nominal predicate separately requires the two actual matching reference symbols. |
| Array | Read the container annotation; this does not validate rank or element policy. |
| Generic instantiation | Read the container annotation; this does not resolve or approve arbitrary payloads. |
| Primitive | Known value names, including qualified and decorated numeric forms, are NotAnnotated. VOID/NEVER receive the same non-null-state disposition, not a successful-value claim. Unmodeled primitive references/unknown names and nullable-value/Option spellings are unsupported. |
| Function | Unsupported: the kind has no reference-nullability carrier. |
| Unresolved | Unsupported; never default to NotAnnotated. |

An unrecognized future subclass throws explicitly instead of receiving a
success-shaped fallback. Scalar STRING observes Annotated/Oblivious as possibly
null; unsupported kind rows remain unsupported, without cascading invented
Oblivious findings onto value/function types.

## Actual reference profile

[metadata-profile.json](metadata-profile.json) records **168** references read
from the actual `Binder._metadataBinder.Context.HostCompilationForBinder` after
the GetParent bind. The test asserts that the resolved return assembly belongs
to that exact compilation. Paths are reduced to filenames in the published
record; file hashes remain exact for the measured environment.

The return symbol belongs to `System.Private.CoreLib, Version=10.0.0.0`,
public key token `7cec85d7bea7798e`. This is the manifest-filtered **runtime TPA**
profile actually loaded, not a claim about an SDK reference pack, the public
reference pool, an installed package or other platform/runtime profiles.
Missing metadata cannot satisfy the symbol assertions.

## Corpus reconciliation

[corpus-delta.json](corpus-delta.json) records the complete coordinate comparison
and raw diagnostic differences. All364 source keys remain; only two rows'
raw error counts/hashes change. Conversion success, propagated errors, source
identities, opacity and all other recorded coordinates are unchanged.

| Source | Raw errors | Exact removed analysis findings |
|---|---|---|
| Serilog `Capturing/PropertyValueConverter.cs` |59 to58; propagated7 unchanged | One274 on `value` at converted90:28, receiving spelling `any`. The unresolved nominal alias is not classified as a known reference. |
| Serilog `Context/EnricherStack.cs` |10 to8; propagated1 unchanged | Two273 on `_top` / `_current` at converted16:14 /54:16. `ILogEventEnricher` is not a resolved declaration in this per-file binding context. |

These removals are **unsupported dispositions**, not proof the values are
non-null or the files are safe. Full converted Calor text is identical before
and after for both files. The initial ratchet failure is retained, then its
existing `CALOR_UPDATE_BINDER_BASELINE=1` writer regenerated the observation.
No allowance, denominator or test expectation was relaxed.

The raw comparison used the existing xUnit runner against e299 and5fab with an
identical session-only compile injection, the same two pinned corpus inputs and
the exact `MeasureNative` conversion options (Preview/regular/parse, empty
symbols, selected-active lossy, `Leg2`, graceful fallback, generated IDs).
The audit source hashes to
`7e7ea8e23a0f237d0b33f52854028a6eaaabc880c19544c00855fcb6850f04cf`.
It adds no committed test or production hook; subsequent standard builds omit
the injection.

## Executed scoped observations

The initial13 new controls against unmodified e299 production yielded11
failures: six native consumer bypasses, two BCL consumer bypasses and three
missing native identity controls. The existing comparison selection passed.
Development compile/fixture failures are not described as environmental flakes.

| Existing runner / selection | Result |
|---|---|
| Compiler:54 N1 cases, nullable typing/checker/integration, metadata annotations, bound-type architecture/equality, native overloads, diagnostic catalog and full binder/corpus ratchets |302 passed;0 failed/skipped at5fab after the audited reconciliation. Initial828f candidate:300 passed and the one expected corpus-ratchet failure, then301 passed after reconciliation. |
| LSP: nullable typing/routing, rename and definition handlers |64 passed;0 failed/skipped, including the new editor case. |
| Conversion: snapshot and Roslyn round-trip compile selections |156 passed;0 failed/skipped at5fab. |
| IDs: `IdScannerTests` |9 passed;0 failed/skipped at5fab. |
| Enforcement: resolver and row-lattice selections |204 passed;0 failed/skipped at5fab. |

Selections overlap and are not a summed unique-test or corpus denominator.
The actual new inventory is54 compiler cases and one LSP case:8802 to8856,
497 to498, skips unchanged. Final-head CI is still a separate requirement.
Z3 was restored only after an explicit missing-asset failure and verified
against the committed hashes. Research ledgers, frozen inputs and paused
collection were not touched.

Use a normal merge to preserve implementation/evidence ancestry. Historical
N0/T1 observations retain their original sources and versions.
