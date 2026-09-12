# D1 current-candidate measurement and source-artifact design

**Design checkpoint:** 2026-09-12. **Execution status:** blocked on
parent-provided accepted N2 #1381 and N3 #1382 merge pins.
**Owner:** Copilot / GPT-6 Astra, `nominal-policy-1400`,
`ada009ab-5eb3-41ac-82cc-68fa9b4e158a`, existing #1400 / #1450.
**Capacity:** one bounded M continuation, accepted
2026-09-12T03:36:50-04:00; recorded before behavior in
[the issue](https://github.com/juanmicrosoft/calor/issues/1400#issuecomment-5644506295)
and N0. Provisional checkpoint/two-review target 2026-09-12 depends on those
merges. No new behavioral prototype, candidate binary or measurement exists.

## 1. Question and bounded scope

Measure what a conservative nominal-reference policy would actually affect
on repaired, merged source, and whether a narrow faithful migration can preserve
unannotated declarations. Do not predetermine adopt, revise or defer.
Genuinely Oblivious, resolved, in-scope nominal references are possibly null,
not safe. Exact identity and annotation provenance are different requirements.

Use the five existing E1 product subjects: Synthetic, Synthetic2, MediatR,
Serilog and FluentValidation. Keep their configured exclusions, source pins,
thresholds, target frameworks and complete inventory visible. The scoped
boundaries are explicit initialization, native returns and supported selected
method inputs, including statement calls. Record other encountered shapes as
out of scope or unresolved, not as safely accepted D1 obligations.

This is not Stage A/B activation, a new reference resolver, overload-policy
change, #1401 implementation, or repair of another owner's unsupported rows.
Arrays, generic payloads, mutation, ref/out/in, arbitrary flow transfer and
unresolved BCL members do not become supported by adding an observation.
#1444 is retained separately and remains a #1402 prerequisite.

## 2. Hard gates before implementation and measurement

1. Receive the actual accepted #1381 and #1382 merge SHAs from the parent.
   Fetch and normal-merge actual main into this existing branch. Assert both
   SHAs and all original D1 measurement commits are ancestors. Reconcile all
   other-owner capacity rows from that main verbatim; update only D1's row.
2. Pin the merged source, corpus gitlinks and diffs, SDK/runtime/host, actual
   environment, build options and available dependencies. Read the accepted
   APIs and N3's actually regenerated evidence/catalog; do not infer a combined
   baseline by adding prior owners' counts or assume draft APIs have landed.
3. Create only owned isolated scratch materializations for instrumentation
   and shadow builds. Keep compiler, converter, harness and routing source in
   the PR identical to accepted main. Preserve source patches and build inputs
   as non-shipping evidence archives, never solution projects or public flags.
4. First prove instrumentation neutrality on the controls: original versus
   instrumented-current diagnostics, acceptance and emitted bytes must agree.
   Missing observer hooks or a changed overload choice blocks that comparison;
   do not continue with display-name membership or substitute reference pools.

No corpus run, shadow compiler implementation or migration prototype happens
while these merge prerequisites remain pending. Dependency inspection and this
design do not count as current measurements.

## 3. Existing implementation anchors to reuse

These are observed at accepted main `3b513a63`, not promises about future APIs:

| Existing anchor | Intended reuse after reconciliation |
|---|---|
| `NullabilityChecker.IsPossiblyNullAssignedTo` / `CheckScalarStringTarget` | Real shared predicate and its nominal branch; no parallel display-name classifier |
| `NominalBoundType.IsKnownReferenceType` / `HasSameUnderlyingReferenceType` | Existing native declaration ID or Roslyn equality; matching short names never substitute |
| `Binder.TryBuildStringTarget(..., declaringFunction)` | Actual receiving shape and selected native callee context, not the observer's guessed caller namespace |
| BCL nominal parameter `RoslynSymbol` | Actual parameter/type/assembly identity, not textual type spelling |
| `NullabilityChecker.GetAnnotation` | Exhaustive kind disposition; unsupported null is not NotAnnotated or an invented Oblivious state |
| `NominalReferenceIdentityTests` private metadata capture | Read `Binder._metadataBinder.Context.HostCompilationForBinder`, hash its actual references and prove selected symbol assembly membership |
| `RoundTripPipeline`, `RunEvidence`, `CandidateEvidence`, `DiagnosticEvidence` | Existing inventory, conversion, diagnostics, recovery and per-attempt evidence |
| `ProjectParseContextResolver`, `FileContextDetail`, `ReportGenerator.CaptureDiagnostic`, `TrxParser` | Evaluated contexts, original sources, standard diagnostic capture and existing test reader |

Inspect N2/N3's accepted implementation before naming their map/transfer
members in executable artifacts. Consume the selected maps on actual bound
expression AND statement calls. Do not reconstruct selection with index zips,
repeat overload resolution, or treat public E1 reference pools as the private
metadata context. A lazy uncreated metadata binder is "not instantiated", not
an observed empty reference set.

`CaptureBindingAnalysis` currently reparses/rebinds independently and says
production reachability is not asserted. Retain that label. The new observer
must capture the relevant actual compiler invocation as well; attach an
invocation ID so an independently rebuilt Binder is never silently substituted.

## 4. Isolated candidate design and attribution

Use three explicitly named configurations on the same newly converted source
and immutable context; all source patches and binaries get distinct hashes:

| Configuration | Purpose |
|---|---|
| `current` | Shipping/default behavior from the accepted merged source, plus neutral observation only |
| `shadow-annotated` | Isolated enforcement of the existing resolved nominal Annotated obligations at the bounded receiving sites |
| `shadow-conservative` | Same isolated enforcement, with nominal Annotated OR Oblivious in the real identity-gated predicate |

The intermediate shadow separates enforcement of existing Annotated findings
from the incremental Oblivious-policy effect. Do not attribute both changes
to Oblivious widening. Preserve the non-nominal STRING/array/generic branches,
existing transitional rejection, overload selection and all actual shipping
routes. Both shadow binaries are disposable/non-shipping.

Prefer a minimal archived compiler patch applied only to scratch source.
Within that isolated compiler, the receiving-site instrumentation must observe
the real source expression, receiving BoundType, selected callee/map and the
predicate result. Its shadow rejection must travel through the real compiler
and E1 acceptance/fallback path, not be inferred from a hypothetical count.
Record default public outcomes separately from these prototype outcomes.
Never promote a whole diagnostic code regardless of shape: shadow enforcement
is restricted to the actual matched nominal site. No source patch ships in
`src/`, `tests/` or `tools/` on the issue branch.

The mechanical carrier-level widening is NOT automatically a faithful
implementation of "genuinely Oblivious". Retain every actual rejection it
causes, then partition by demonstrated origin. An origin-unknown or known
transfer-loss row must not be dropped to improve counts. If the existing
information cannot distinguish the contracted scope, report the prototype's
scope mismatch and concrete missing prerequisite; that blocks adoption of
that implementation rather than licensing unknown-to-safe filtering.

## 5. Boundary census, identity and annotation origins

Collect a census beyond positive diagnostics: all encountered initialization,
return and input boundaries, target-builder declines, calls with no selected
map and values with unsupported types. A silent predicate false is not a safe
classification. Keep a per-invocation coverage check between the bound-tree
census and instrumented receiving sites; unexplained missing sites block
denominators for the affected scope.

Each stable row joins original file/hash and evaluated context to converted
source/hash, span, enclosing declaration ID, boundary kind, argument ordinal
and run/invocation ID. Preserve both source and receiver identities, namespace/
declaring context, native IDs, Roslyn symbol/assembly identity, actual selected
overload, map entry and mapping form. Ordinals identify an already selected
map entry; they never infer a positional mapping.

Snapshot the actual private reference profile used by each relevant Binder:
reference order, aliases/properties, path or artifact locator, byte SHA256,
assembly identity, and selected-symbol membership proof. Dedupe identical
profiles by hash while retaining each invocation's profile ID. Missing files,
failed hashes and unavailable membership remain explicit errors/limits.
An in-memory reference requires captured bytes/identity or an unavailable
hash, not a fabricated filesystem path.

Record evaluated project Nullable settings and effective per-file directives,
compile inputs including generated sources where available, reference/package
provenance and original Roslyn declaration annotation. Separate declaring
definition from instantiated signature and observed BoundType annotation.
No Roslyn flow-state or null-forgiving operator becomes a guarantee.

Use orthogonal fields for identity, origin, receiving shape, predicate result
and compilation outcome. The origin classification is:

| Origin | Required evidence / treatment |
|---|---|
| `GenuinelyOblivious` | Resolved reference plus actual unannotated declaration/metadata context; distinguish source-disabled declarations, absent external annotations and instantiated signatures |
| `AnnotationTransferLoss` | Known original/producer annotation or expression guarantee changes during conversion/binding/transfer; identify the first observed loss edge |
| `ExplicitAnnotated` | Explicit nullable declaration or resolved nullable metadata producer; not Oblivious fallout |
| `KnownNonNullExpression` | Supported direct constructor/literal guarantee on actual resolved type; do not extend automatically to unsupported composites |
| `DeclaredNotAnnotated` | Actual annotation declaration; not a universal runtime non-null guarantee |
| `OriginUnobservable` | Identity may resolve but origin cannot be established; not assumed genuinely Oblivious or safe |
| `UnsupportedOrUnresolved` | Missing/ambiguous identity, absent map, unsupported kind/transfer or missing member; state which proof is unavailable |

Cross-reference exact newly observed rows to #1398 (safe consumption), #1384
(BCL members), #1444 (arrays), or #1401 (converter context) only when the row
actually demonstrates that dependency. Old pre-T1 failures are not evidence
that a repaired component still fails.

## 6. Denominators, E1 execution and all-attempt accounting

Predeclare two complete subject-test attempts per E1 leg for each configuration,
using existing `--capture-binding-analysis --test-attempts 2`. E1's legs remain
original C# `baseline` and converted `candidate`; add the configuration axis
explicitly rather than renaming those legs. Five subjects times three
configurations times two legs times two declared attempts is a maximum of
60 declared suite slots, NOT 60 distinct tests or a promised completion count.
Record not-reached slots and the exact blocking failure; no retry-to-green.

Preserve MediatR collection isolation identically on all legs/configurations.
Use original source pins and the same conversion options and converted bytes
for cross-configuration comparisons. Assert hashes; classify conversion drift
before making a policy attribution. Record every build, recovery/reversion,
crash, skipped test, failed restore, exit and full per-test identity/result.
Recovered original C# does not count as native converted coverage.

Keep per-attempt raw TRX/stdout/stderr bytes before harness cleanup. If an
additional archival hook is necessary, place it only in the common neutral
scratch instrumentation and prove it does not change tests or results. Reuse
E1 records/`TrxParser`; do not introduce another runner or waive cleaned TRXs.

| Denominator / delta | Meaning |
|---|---|
| Inventory / excluded / attempted / not reached | Every configured original input, with reasons; never only survivors |
| Conversion / Calor compilation / C# validation / kept-native / interop / recovery | Separate stage outcomes, not disjoint totals to be summed |
| Encountered / resolved-in-scope / unsupported / origin-unobservable boundaries | Actual census and coverage proof, separately for each receiving surface |
| Predicate-affected boundaries and distinct files | Actual current-to-shadow observations, classified by origin and source/target identity |
| Current accepted and paired shadow evaluated files | Denominator for newly rejected files; a missing shadow result is unavailable, not accepted |
| Shadow rejected / newly rejected / already rejected | Actual compiler outcomes; retain independent causes and all deltas |
| Annotated activation versus incremental Oblivious delta | Compare current to shadow-annotated and shadow-annotated to shadow-conservative separately |
| Faithful migration / raw interop fallback / deferred | Tested migration attempts, with real native-coverage losses |

Persist sets/row IDs behind every count. For the paired-file rejection rate,
the denominator is the exact set accepted by current and evaluated by the
specified shadow; also report all current-accepted files missing a shadow
result. A partial observed rate is not a whole-corpus rate. Keep pre-rejected
files in the full inventory and diagnostic analysis, not this acceptance-rate
denominator. A boundary ratio is never substituted for a file rejection rate.

Record unknown numerators/denominators as unavailable with a cause and affected
row IDs, not 0/0. Missing package transport, generator outputs or private/map
proof can block a subject/row without silently removing it. No empty diagnostic
set is evidence of zero unknowns.

Re-record the execution environment. If it remains SDK 10.0.400 / runtime
10.0.11 / macOS ARM64 without .NET 8, retain explicit
`DOTNET_ROLL_FORWARD=Major` symmetrically; never call it default/.NET 8 evidence.
Install/restore dependencies only after a relevant missing-dependency failure;
use the existing Z3 download script. Do not disable restore verification or
borrow unrelated credentials to bypass Serilog transport failures.

## 7. Controls and bounded migration feasibility

Run source-defined controls through original C#, current Calor and the isolated
shadows, recording compile rejection separately from executable behavior.
Controls include genuine unannotated source and metadata declarations, direct
constructors/literals, explicit nullable producers, unknown identities,
same-short-name/different-symbol negatives, local depths 0/1/2, supported native
members, unsupported BCL members and statement/expression selected calls.
Exercise accepted normal/named/optional/params mappings where supported without
assuming #1444's array semantics.

The N2 [three-case comparison](https://github.com/juanmicrosoft/calor/blob/3bafd2c9a21eb7f31de7409e439f2d122d53c236/docs/plans/evidence/local-annotations-1381/native-input-comparison.json)
measured `3b513a63` versus `4bb47d54`, not its artifact-host commit `3bafd2c9`.
It reports direct nullable native input rejection0208 on both sides but
pre-existing acceptance through locals, with annotation loss repaired to raw
analysis-only274. Rerun comparable controls at the new pins; never generalize
direct0208 to all locals or label pre-existing acceptance an N2 regression.
Matching hashes of absent output do not establish executable equivalence.

For a small declared fixture matrix, include an unannotated reference
parameter/return that passes null through, returns a present object, constructs
a fresh object and invokes a downstream receiving contract. Use known native/
Roslyn reference identity and nullable-disabled source context. Classify the
current converter output before attempting any migration.

In isolated artifacts only, test an identity/context-backed nullable-reference
representation on the exact declaration sites whose source contract allows
null. Compare null/present return values, object identity where relevant,
exceptions and observable side effects using the existing xUnit/Roslyn
execution facilities. Keep original/current/prototype source and output hashes.
Do not claim whole-corpus migration from these fixture results.

If faithful representation cannot be justified, exercise existing explicit
unsupported/raw C# interop preservation and measure lost native statements/
members/files and kept test behavior. If neither route is justified, record
deferral and the precise reason. Retain every failed attempted migration.
No blanket question marks, None-to-nullable conversion, injected default/
throw/unwrap, implicit Option conversion, suppression or behavior-changing
coalesce is allowed. Existing user-authored coalesces remain source choices.

## 8. Proposed artifact layout and reproducibility

Only this plan exists yet. Future paths below are a design, not empty result
files or a zero-filled manifest:

| Planned artifact | Content |
|---|---|
| `runs/<run-id>/manifest.json` | Accepted merge pins, clean source hashes, patch order/hash, binaries, environment, options, corpus references, all artifact hashes and explicit unavailable items |
| `source/<source-id>/` | Non-shipping `.patch.txt`, `.cs.txt`, `.csproj.txt` as needed, existing dependency locks, exact materialization/build commands and source pin guards |
| `runs/<run-id>/e1/` | Unmodified E1 reports per subject/configuration/run identity |
| `runs/<run-id>/boundaries.json.gz` | Full census, real target/selection/identity/provenance, current/shadow results and diagnostic joins |
| `runs/<run-id>/references.json.gz` | Actual per-invocation private profile and selected-symbol membership, distinct from public/project pools |
| `runs/<run-id>/attempts/` | Every original report, exit, build log, TRX and runtime-control result |
| `runs/<run-id>/migration.json.gz` | Every attempted faithful/fallback/deferred case, source identities, null/present behavior and native-coverage loss |
| `runs/<run-id>/summary.json` | Derived counts and contributing row sets; no guessed denominator |
| Final decision proposal | Exact measured matrix, limitations, conditional recommendation, review/CI provenance and parent decision status |

Archive original bytes before reduction and keep failed development attempts
distinct from corpus outcomes. Never overwrite a completed run or normalize
its raw diagnostics to make a replay match. Commit source artifacts before
measurements; later commits add evidence without rewriting those ancestors.
Reproduction must reject the wrong source/patch hash or unexpected tracked
diff. Do not reuse the old pre-T1 observer as a current candidate.

Historical files have moved byte-for-byte to `../historical/pre-t1/`; original
paths remain accessible at preserved commits. Their manifests and scripts are
unchanged, including capture-time unknowns and missing original TRXs.

## 9. Stop conditions, review and parent decision

The immediate blocker is missing parent-provided accepted N2/N3 merge pins.
After those arrive, independently unavailable private profiles, actual selected
maps, source origins, generator context or faithful migration can block exact
rows or the recommendation. Report the concrete affected set and dependency;
do not absorb another implementation slot or declare Stage B permanently
deferred because an earlier checkpoint was incomplete.

After measured artifacts and a recommendation are committed, launch two fresh
non-author final-head contexts: compiler integration and adversarial
compatibility. Provide exact head/base, the live contracts, all three
configurations, source patches, raw reports, denominator sets and migration
outcomes. Record prompts, actual context/name/model provenance, findings,
fixes and final dispositions. Any material final-head change needs refreshed
reviews; previous `a0ed611e` reviews do not transfer.

Run normal CI on that exact final head and record its actual checkout source
separately from local measurement pins. Parent alone decides adopt, revise or
defer and merge/closure. #1402 must still rerun its implemented candidate.
Stage A stays independent; #1444, Calor0710/old-major fail-closed behavior,
D3/D12/D14 and runtime guards remain required. No research, release,
publication, activation or suppression follows from this continuation.
