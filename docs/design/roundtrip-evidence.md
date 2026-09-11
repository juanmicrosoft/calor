# Structured round-trip evidence (E1, #1399)

This extends `tools/Calor.RoundTrip.Harness`, not a new benchmark framework.
It supplies observations for #1400 and Stage A/B adjudication under #1082.
**A legacy fidelity `pass` is not nullability acceptance.** The new evidence
and classification artifacts start `Unadjudicated`. Unknown is not safe.
No compiler routing, nullable policy, conversion repair or stage activation
is changed here.

## Run and compare

```bash
dotnet run --project tools/Calor.RoundTrip.Harness -- \
  run MediatR --dotnet dotnet --capture-binding-analysis --test-attempts 2 \
  --output ./conversion-reports/candidate
dotnet run --project tools/Calor.RoundTrip.Harness -- compare-evidence \
  --baseline ./conversion-reports/baseline/MediatR-roundtrip.json \
  --candidate ./conversion-reports/candidate/MediatR-roundtrip.json \
  --output ./conversion-reports/comparison.json
```

`--capture-binding-analysis` reparses converted Calor and runs a **separate**
binder, even if production stopped at an earlier pass. It does not route its
diagnostics into production, change compiler options, or claim production
reached binding. Calor0272/0273/0274 remain `AnalysisOnly`. A raw binder Error
on a converted nullability witness is not a production compile rejection.
An activated production negative remains a later-stage acceptance requirement.

The optional fixed attempt count is declared before baseline, from 1 to 3.
Default: one attempt, **zero retries**. A count of two runs each entire suite
twice on both legs, whether the first result passes or fails. There is no
retry-until-green, selected "best" attempt, averaging, zero imputation or
changed-candidate rerun presented as a same-candidate retry. Each candidate
attempt is compared with its same-number baseline attempt. All attempts,
per-test identities, errors, TRX parse failures and process output survive in
JSON. A missing or failed attempt is not a valid zero-test denominator.

Existing #948 flake allowances remain visible in the **legacy fidelity**
fields for reader compatibility. Strict evidence comparisons do not use that
allowlist. Any failed/incomplete attempt, including an allowlisted failure,
adds an evidence failure and blocks the CLI/JSON top-level gate, even if the
legacy first-attempt comparison says Pass. No later pass waives that failure.
An incomplete input inventory also blocks the gate. Original `baseline`/`round_trip` summaries
describe attempt **one**, not a pooled or best result.

Every invocation writes a uniquely named `<project>-<run-id>-attempt.json`
in addition to the compatible `<project>-roundtrip.json`/`.md` paths.
Keep failed/incomplete invocation artifacts when rerunning with a changed
runtime or NuGet environment. `DOTNET_ROLL_FORWARD=Major` is an explicit
environment variant, not a retroactive correction to an earlier missing-.NET8
attempt. No changes to global NuGet configuration are required or made.

## Schema and identity

Top-level `report_schema_version: 2` is additive: legacy counters, `errors`,
statuses and fidelity fields remain. New nested model fields use PascalCase,
as existing `validated_contexts`/loss-detail objects already do.

* `evidence.Inputs` is captured before snapshot, restore, build or conversion.
  Every selected library `.cs` file has a normalized relative `Path`, `FileId`,
  byte SHA-256 and exclusion flag. Build outputs under `bin`/`obj` are not
  candidates. Exclusions remain in the coverage denominator exactly once.
  Read failures and incomplete enumeration remain explicit.
* `FileId` is `<configured-project>/<relative-path>`, stable across compiler
  candidates, recovery and work-directory relocation. `InputSha256` pins
  original bytes. It never hashes a recovered file in place of the candidate.
* `candidate.CandidateId` hashes file/input identity, converted-Calor digest,
  actual compiler binary/options, conversion options and observed validation
  context settings/reference digests. It is an exact candidate fingerprint,
  not an assertion that different compiler builds are equivalent.
* `candidate.ConvertedCalor` and its digest retain the actual converted text.
  `EmittedCSharpSha256` identifies the postprocessed C# candidate, before
  validation/recovery. Existing raw `EmittedCSharp` remains in-memory only.
* Production and conversion observations are in `candidate.Diagnostics`;
  independent observations are in `candidate.AnalysisDiagnostics`.
  Candidate errors and `StatusBeforeRecovery` remain alongside final `status`,
  legacy `errors` and separate `recovery` entries. A direct `CompileError`
  remains distinct from a candidate accepted then `Reverted`.
* `BuildAttempts` retain command, directory, phase, active candidate paths,
  raw stdout/stderr, exit and structured diagnostics, including validation
  builds and recovery rebuilds. A successful mixed original/generated build
  never overwrites the earlier rejection evidence.
* `TestAttempts` retain `Leg`, ordinal, complete `TestRunResult` and strict
  paired comparison. Test identity is project, assembly, adapter, fully
  qualified name, adapter case ID and display name. Theory-row multiplicities
  are retained, not flattened into one pass.

`evidence_counts` distinguishes inventory, attempted, context-resolved,
semantic-resolved/unassessed, production-compilation accepted/rejected/not
reached, skipped, unattempted, crashed and reverted. These describe different
stages and are **not disjoint buckets to sum**. A file may be compilation
accepted and later reverted. Context resolution means evaluated project parse
context; it is not semantic-reference resolution. Semantic resolution remains
`Unassessed` unless genuinely established by a later measurement/review.
Zero semantic-resolved files is not a claim that zero references resolved.

`compare-evidence` joins the union of immutable input `FileId`s and preserves
both complete file records, including diagnostics and recovery outcomes.
It flags changed input hashes and retry rules; added/removed/excluded inputs
do not disappear. It refuses legacy-only evidence or different project IDs,
instead of inventing missing provenance. Its exit 0 means the comparison was
written, **not** that the candidate passed acceptance. Compare all exposed
options/reference/corpus changes and all test attempts before adjudication.

## Diagnostics and actual provenance

Each diagnostic records code when supplied, severity, message, phase, source
kind/path/digest, and available span coordinates. Diagnostic IDs hash the
observation coordinates/content/phase; classifications are additionally keyed
by candidate and file identity, so identical messages across files do not
conflate evidence. Conversion issues do not supply diagnostic codes or full
spans: null fields mean unavailable, not a fabricated Calor code.

Offsets/lengths are zero-based **UTF-16 code units**. Line/column and available
end coordinates are one-based, end-exclusive. A Calor diagnostic's nominal
path may end in `.cs` because the existing converter/compiler invocation uses
the original path. `SourceKind: ConvertedCalor` plus its retained text is the
coordinate authority, **not the original C#**. Roslyn source diagnostics use
physical source spans. Build text can contain `#line`-mapped coordinates:
`MSBuildReportedLocation` preserves what MSBuild printed; it does not invent
physical offsets or reverse source maps.

`Program.Compile` aggregates several passes. `BindingContext` is actual
binder provenance and supplies the receiving boundary/shape/disposition.
Without it, the producer is unavailable. Shared Calor0200/0202 codes alone
do not identify TypeChecker versus Binder. A separate `shadow-bind` invocation
does identify its producer, but does not prove that production reached it.
MSBuild text does not establish an exact compiler/analyzer producer either.

Run provenance captures actual compiler/harness/Roslyn binary hashes and
assembly identities, informational version, host/runtime, selected `dotnet
--info`, source/corpus Git revision and tracked-diff digest, relevant runtime/
build environment, harness options and the public generated-validation
reference pool. File contexts retain actual evaluated compiler properties
(including nullable settings), selected reference paths/content hashes/
aliases/properties, analyzer hashes, compile/additional inputs and build graph
provenance. These are not guessed reference-pack versions.

The public reference pool is **not** a claim that the private lazy
`MetadataBinder` selected every assembly from it or successfully enriched every
call. That selected-reference/reachability limitation remains explicit.
Missing binary fingerprints or Git/SDK observations are unavailable evidence,
not successful resolution. Original library hashes also pin untracked input
files; a Git revision alone does not.

The harness still applies its existing compatibility postprocessing and
permissive options: effects off, contracts off, generated validation deferred
to the actual project. Snapshot these actual options, rather than claiming
CLI defaults. Working-copy SDK-pin removal and normal recovery remain
visible; there is no mandatory new no-revert mode.

## Manual classification artifact

Each invocation emits `<project>-<run-id>-classifications.json`. The template
contains a file-level row and each candidate/analysis/recovery diagnostic row,
plus excluded inventory rows. All start **unresolved**, without a reviewer.
Keep an immutable copy of the report. Edit a separately named classification
copy, recording the actual reviewer (human login, or agent/model/context ID),
rationale, evidence links and diagnostic/candidate/file IDs. An agent is not
a human reviewer and separate AI contexts do not establish statistical
independence.

Use these classifications:

| Classification | Required explanation/evidence |
|---|---|
| `intended-compatibility-break` | Supported receiving boundary/shape, actual diagnostic, intentional behavior change, and an executable explicit migration. Pin original/migrated source, command/options/references, outputs and exit; a prose suggestion alone does not pass. |
| `annotation-identity-bug` | Incorrect source/target annotation or identity, expected behavior, minimal witness and owning repair issue. A rejected supported safe control blocks acceptance. |
| `unsupported-binding` | Concrete unresolved representation, lookup or receiving-shape gap; not "zero diagnostics therefore safe." |
| `baseline-failure` | Pinned original source and baseline build/test failure with valid denominators where available. Do not blame conversion for a baseline-invalid type. |
| `infrastructure-failure` | Missing runtime/tool, restore/network/timeout/parse failure and retained attempt; no valid outcome denominator is imputed. |
| `unresolved` | Missing evidence or disputed attribution, with next investigation/owner. Never green. |
| `safe-control` | Executed, narrowly stated positive control with pinned inputs, diagnostics and output. Not a proof about all paths or reference shapes. |

This artifact does not automatically authorize Stage A/B, future policy,
issue closure, migration insertion or proof-demotion lifts. Parent-delegated
adjudication and two separate final-head review contexts remain mandatory.
Historical N0 corpus/environment limitations retain their dated provenance.
