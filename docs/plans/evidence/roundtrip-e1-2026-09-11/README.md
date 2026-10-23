# E1 structured round-trip evidence — 2026-09-11

Product harness work for [#1399](https://github.com/juanmicrosoft/calor/issues/1399),
[PR #1442](https://github.com/juanmicrosoft/calor/pull/1442), under #1082.
This is not the paused paid study, #1400's policy decision, activation,
release evidence or permission to close #1399. Parent adjudication is pending.

## Pinned implementation and measurements

**Primary measured implementation:** `954aef5f0ec135d96eeac1977bf46649437a343f`.
The primary corpus and controlled fixtures use that clean tracked source tree;
each report retains the actual compiler/harness/Roslyn binary hashes, options,
reference identities/hashes, runtime and source/corpus revision.
The report/evidence documentation commit is later, not a relabeled measurement.
`manifest.json` hashes both compressed artifacts and their original bytes.
Gzip is storage compression only; decompress to obtain the original full
report/TRX/log, without path, timestamp, counter or diagnostic normalization.

Earlier implementation iterations are retained separately, not treated as
replicates of the final candidate. Precommit reports pin `6a0000bb` **plus
their tracked-diff digest and actual binary hashes**, not clean main.
One initial Synthetic run had five option-serialization crashes; its test
suites still passed, but its conversions did not. The reflection snapshot
was corrected to serialize concrete Roslyn parse settings, not the entire
Roslyn diagnostics object graph. Initial fixture failures are retained in
the failure log archive. They are development failures, not evidence that
Calor's nullability policy was activated.

`00e90c24` corpus reports predate the mapped-location fix: several
project-validation failures retained raw error strings but missed per-file
structured diagnostics whose `#line` path pointed to the source working
copy rather than the validation copy. Fresh corpus inspection found this;
`954aef5f` corrects both root normalization and explicit subset attribution.
A real MSBuild `#line 40` regression now checks `CS0029` at mapped line43.
The original reports remain visible with that limitation; no counters or
diagnostic arrays have been backfilled.

## Outcomes and denominators

| Observation | Input / outcome | Runtime/test evidence | Interpretation |
|---|---|---|---|
| Synthetic, complete existing pipeline | 5 inputs,5 attempted,5 native kept,0 failed/reverted | Two fixed full-suite attempts per leg,52 passed each | Bounded unchanged control, not universal null safety |
| MediatR, complete existing pipeline | 32 inputs,32 attempted;18 native +6 with-losses kept;2 CompileError +6 EmitCompilationError;0 reverted | Two fixed attempts per leg,155 passed and2 skipped each | Eight conversion losses remain; passing fallback tests do not erase them |
| Converted nullable + explicit migration + literal control | 4 inputs:3 attempted/accepted/kept +1 excluded | Emitted code compiled and executed by existing xUnit test | Three production acceptances, **not** three semantic-resolution proofs |
| Missing-type control | 1 attempted; Calor compilation accepted; generated C# rejects withCS0246 | Identical original C# bytes separately fail a real `dotnet build` withCS0246 | Baseline-invalid input, not conversion-induced nullability rejection |
| Recovery control | 1 accepted candidate then1 Reverted; raw nullable0273 preserved | Actual build reportsCS0103 after explicitly injected missing symbol; actual recovery restores original bytes | Injected mechanism control, not a naturally observed corpus reversion |
| Missing-tool control | 1 original input retained;0 attempted; no baseline/test denominator | Real missing-executable failure, incomplete report persisted | Infrastructure failure, not0/0 success |

The two real MediatR `CompileError` files are
`src/MediatR/Pipeline/RequestExceptionActionProcessorBehavior.cs` and
`src/MediatR/Pipeline/RequestExceptionProcessorBehavior.cs`. Both have an
actual **Binder-origin Calor0208** at their `GetExceptionTypes` internal call:
bound argument `OBJECT` does not match the selected internal signatures.
Their exact code/severity/Calor span and converted text are retained. These
are genuine resolution failures, not substitute nullable0272/3/4 failures.
All six later project-validation failures also retain their structured
candidate diagnostics and separate raw validation/build attempts.

### Executed explicit migration, not an inserted converter default

`runtime-observations.json` records actual generated-code execution:

| Source/control | Absent environment key | Present value |
|---|---|---|
| Original nullable BCL return | null | `"present"` |
| Manually authored `?? "fallback"` migration | `"fallback"` | `"present"` |
| Non-null literal control | `"safe"` | N/A |

The test creates the original C# and the explicitly migrated C# separately;
conversion does not invent the fallback. Raw binding reports0273 for both
the unsafe original and the runtime-safe coalesce. The latter is the known
annotation-transfer defect owned by #1398, not a reason to classify this
safe control as a valid activated rejection.

**No production nullable rejection is claimed.** Calor0272/0273/0274 remain
AnalysisOnly. The nullable fixture demonstrates an actual converted-file
raw-bind Error and actual production acceptance/runtime null. The prospective
compatibility break has an executable migration, but no presently enforced
break is classified as accepted. The activated negative remains a later
Stage A/B requirement, subject to the parent deciding E1's bounded acceptance.

## Classification and limitations

`classifications.json` contains file-level and diagnostic-level rows keyed by
report/run/file/input/candidate/diagnostic. The actual manual analyst is
**Copilot/GPT-6 Astra implementation context
`e9c6935e-da28-4a3b-94d4-0a5bbb596332`**, not a human and not either independent
final reviewer. Categories follow
[the schema contract](../../../design/roundtrip-evidence.md).
Unreviewed/supportedness-disputed corpus rows remain **unresolved**, never
green; evidence is `Unadjudicated`. Baseline-failure, infrastructure-failure,
unsupported-binding, annotation-transfer-bug and bounded safe-control rows
are explained. There is **no activated intended-compatibility-break row**:
the original nullable row instead records the prospective category and
links the tested migration without claiming enforcement.

References from evaluated C# project contexts are exact captured inputs.
The actual public validation reference pool is also pinned. The private lazy
MetadataBinder's per-call selected set/reachability is not exposed by the
production API; this report does not substitute the pool for that observation.
Other producer passes are unavailable unless the diagnostic carries actual
BindingContext; shared codes alone are not a producer identifier.
Semantic resolution remains `Unassessed`, not inferred from diagnostics.
These are concrete limits for parent adjudication/future shadow measurement.

N0 evidence is unchanged. Serilog's three historical restore failures still
have no valid outcome/test denominator. This bounded local E1 capture does
not rerun or relabel Serilog, FluentValidation or Synthetic2. Final normal CI
runs its own configured corpus independently. MediatR here explicitly uses
`DOTNET_ROLL_FORWARD=Major` on both legs; it is not the earlier default
missing-.NET8 attempt. Every retained corpus invocation used two fixed suite
attempts on both legs, with no early green exit or waived failure.

## Reproduce

Use a separate checkout of the measured SHA. Do not reset another worktree.
Keep scratch files under that checkout (the commands below never use `/tmp`).
Initialize only the product corpus; do not touch frozen study worktrees.
Create neutral `Directory.Build.props` / `.targets` containing `<Project />`
and `Directory.Packages.props` disabling inherited central package management
inside `.e1-reproduction/runtime`, as in N0, before generated subject builds.

```bash
git submodule update --init
mkdir -p .e1-reproduction/runtime .e1-reproduction/corpus
export TMPDIR="$PWD/.e1-reproduction/runtime"
export CALOR_TELEMETRY=0
unset CALOR_NO_TYPE_CHECK
# Run the existing Z3 download script only if the build reports missing assets.
dotnet build tools/Calor.RoundTrip.Harness --nologo
DOTNET_ROLL_FORWARD=Major dotnet run --no-build \
  --project tools/Calor.RoundTrip.Harness -- \
  run Synthetic MediatR --dotnet dotnet --capture-binding-analysis \
  --test-attempts 2 --output "$PWD/.e1-reproduction/corpus"
CALOR_ROUNDTRIP_TEST_EVIDENCE_OUTPUT="$PWD/.e1-reproduction/fixtures" \
dotnet test tests/Calor.RoundTrip.Harness.Tests --filter \
  'FullyQualifiedName~ReportGeneratorTests|FullyQualifiedName~ComparisonTests|FullyQualifiedName~FidelityTests|FullyQualifiedName~RoundTripExitPolicyTests|FullyQualifiedName~RunConfigOverrideTests|FullyQualifiedName~UpstreamFlakeGateTests|FullyQualifiedName~TrxParserTests|FullyQualifiedName~RoundTripPipelineSafetyTests' \
  --logger 'trx;LogFileName=e1-product.trx' \
  --results-directory "$PWD/.e1-reproduction/tests"
```

The primary affected-product selection has131 passing cases; the full harness
discovers289 cases, matching the unchanged-skip manifest. No study/paid
benchmark execution is part of these commands.

The original baseline-invalid control is stored as `baseline-original.cs.txt`
with byte hash matching that fixture's inventory. Copy it to `Original.cs`
beside `baseline-original.csproj.txt` renamed `Baseline.csproj` inside the
neutral scratch ancestor, then run `dotnet build Baseline.csproj`: observed
exit1, CS0246 at1:53. Its failure log is retained. This must not be counted
as a new converted failure.

For full JSON, use Python `gzip.open(path, "rt")` or `gzip -dc`; compare reports
with the existing harness `compare-evidence` command. `corpus-comparison.json.gz`
retains both sides of the00e90c→954aef evidence comparison, including earlier
capture gaps and every test attempt. Exit0 from that reader only means the
comparison was written.

## Review provenance

Initial immutable-head reviews at `4d86f257`:

* Integration context `83c4ce57-ff41-4d63-a20b-b43e568cb7b4`,
  requested/registry model `gpt-5.5`: **fix required**. Later failed fixed
  attempts were visible but did not block top-level CLI/JSON. `00e90c24`
  made evidence failures block the shared gate and added failed/incomplete
  second-attempt regressions. Matching Unknown/Aborted outcomes now remain
  incomplete instead of green.
* Evidence/compatibility context `7028d549-8b11-4408-9048-016d35ab30b6`,
  requested/registry model `claude-opus-4.8`: **no blocking finding in its
  initial scope**, with final measurements/re-review still required.

Final review requests/results, exact final SHA and prompts are
recorded durably on PR #1442 after this artifact commit. Reviewers cannot
introspect their underlying model build. Separate AI contexts are not
human or statistical independence. The implementation author does not
adjudicate either review, merge the PR or close the issue.
