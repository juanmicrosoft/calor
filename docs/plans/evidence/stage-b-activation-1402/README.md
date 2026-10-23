# Stage B nullability activation evidence

## Scope and source

This packet records technical review and corpus adjudication for
`c2a8816d1bd3864e70966120c9849e8f8d988cb2` in implementation PR #1461.
The compiler at that source declares **0.21.0**; this is preparation for 0.22,
not evidence that the 0.21 NuGet packages contain these changes.
Publication and package verification belong to the separate release process.

Stage B adds supported arrays, whitelisted immediate STRING generic payloads,
and identity-proven nominal references to Stage A's scalar STRING checks.
The three receiving boundaries are initialization (`Calor0272`), native return
(`Calor0273`), and resolved method input (`Calor0274`). Type/effect opt-outs and
transpile-only do not disable these active binding checks.

Generic outer-container nullability is retained but not enforced. Arbitrary
generic payloads, general mutable assignment/rebinding, member writes,
constructor inputs, unresolved shapes, and whole-program non-nullness are not
established. Sized array allocations retain declared element annotations; they
do not prove every slot initialized. D3/D12/D14 demotions and runtime guards
remain, and #875 stays open.

## Reproducible corpus comparison

- Baseline: PR #1459 head
  `562259c528494ace7d5130dd6382f46b3b3f4a29`, accepted Stage A tree;
  [CI run 34930956008](https://github.com/juanmicrosoft/calor/actions/runs/34930956008).
- Candidate: PR #1461 head `c2a8816d`; CI merge
  `032834238d7c2b68675cae06a68e55935ddc3747`;
  [CI run 34988156401](https://github.com/juanmicrosoft/calor/actions/runs/34988156401),
  `roundtrip-reports` artifact **10405320561**.
- Candidate compiler SHA-256:
  `723754c1a7225f92686a70991fa6f63dafa7dee241db1a8b7d1ae95c719782c6`.
- Toolchain: .NET SDK 10.0.401, runtime 10.0.12, Ubuntu 24.04.5, Roslyn 5.3.
  `corpus-summary.json` retains exact assembly/reference identities, hashes,
  options, corpus revisions, outcomes, and attempt counts.
- The existing harness `compare-evidence` produced **289 equal-input rows**
  and identical retry rules for all five subjects. All baseline/candidate
  attempt failure lists are empty. Each leg declares one complete attempt.
  `equal-input-comparison.json` retains every input hash and status.

| Subject | Inventory | Attempted | Replaced | CompileError | Native | Runtime passed / total |
|---|---:|---:|---:|---:|---:|---:|
| Synthetic | 5 | 5 | 5 | 0 | 5 | 52 / 52 |
| Synthetic2 | 1 | 1 | 1 | 0 | 1 | 20 / 20 |
| MediatR | 32 | 32 | 23 | 3 | 17 | 155 / 157 |
| Serilog | 112 | 106 | 57 | 10 | 44 | 811 / 811 |
| FluentValidation | 139 | 139 | 97 | 19 | 93 | 865 / 866 |

Runtime pass counts and inventories equal their paired baselines; the three
non-passing entries above are unchanged skips, not new failures. Serilog has
six explicit pattern exclusions. All **283 attempted files are semantically
unassessed** under the harness's `CaptureBindingAnalysis: false`; context
resolution is not semantic resolution. CompileError and emission failures
retain original C# in the hybrid run. Zero recovery reversions is not evidence
that all runtime tests exercised Calor-generated implementations.

### The one accepted coverage loss

`MediatR/src/MediatR/Internal/ObjectDetails.cs` changes from Replaced to
CompileError, with six Annotated nominal `Calor0274` errors at three two-argument
calls. Its early-return null guards are not transferred by this bounded
analysis. This is a compatibility limitation, **not a newly discovered null bug
in the original C#**.

`SafeConsumptionRuntimeTests.NominalEarlyReturnMigrationPreservesNullOrderingAtRuntime`
rejects a reduced original and executes an explicit manual migration over all
four null/non-null input pairs, retaining outputs `1, 1, -1, 0`. Coalescing
throws after the unchanged guards express the required local facts; those
throws are unreachable in this witness. The converter does not insert them.
The website guide describes this manual choice and its limits.

The earlier Synthetic FizzBuzz rejection was a real supported-safe regression,
not an accepted loss. The allocation repair restores all five Synthetic native
replacements. `ArrayAllocationAnnotationTests` converts the actual FizzBuzz
source, executes Range, and retains nullable-element/container negative controls.

### Diagnostic and provenance changes

After sorting complete diagnostics and normalizing only run-specific temporary
paths, **the only final round-trip diagnostic additions are ObjectDetails'
six errors**. ConditionBuilder's differing first-ten display errors are ordering
and truncation; its 54 complete structured diagnostics agree.

CollectionPropertyRule's emitted-source hash variation was reproduced exactly
by changing the run-specific `#line` path. A fixed path emits identical output
before and after binding. Raw reports retain original hashes and paths; this
explanation does not relabel them as byte-identical output.

The separate F-2 measurement visits 26 additional expressions in
`try_catch_when.calr` (12 to 38), from six newly bound filters with
3, 3, 3, 3, 7, and 7 expressions. Its conversion leg gains three visits in each
of MediatR's RequestExceptionActionProcessorBehavior and
RequestExceptionProcessorBehavior (35,153 to 35,159 overall).
Source, opacity, and represented-source identities do not change.

In those two files, the same analysis-only `Calor0200`
`Undefined variable 'invocationException.InnerException'` moves from the body
to the newly bound filter: converted-Calor half-open spans
`[3068,3102)` to `[2958,2992)`, and `[3147,3181)` to `[3037,3071)`.
Successful-filter narrowing supplies a scoped **INT/Oblivious placeholder** for
body lookup, not resolved member metadata. This is not diagnostic deduplication.
Other raw identities and the propagated `Calor0208` identities remain unchanged.
The separate three-subject effect ledger remains 265 enforced / 99 excluded,
with 135 `Calor0425` sites in 48 modules; do not mix its denominator with 289.

## Review, execution, and publication gates

`adjudication.json` records two non-author final-head review contexts, repaired
findings, and the bounded parent decision. Isolated agent contexts are practical
review separation, not statistical independence or a separate human approval.
The compiler inventory is **12,018** before documentation; this PR adds **50**
snippet cases for **12,068**. The final CI links, not an older green run, govern
merge eligibility.

Earlier runs are retained rather than erased: the initial allocation regression,
the preapproval failure, two source job-budget cancellations, and website
`networkidle` failures are not called clean attempts. The helper exception was
approved separately in #1462 before implementation. Source-job time budgets now
allow 30 minutes for the compiler and 50 for quality-ratchets, without changing
partitions, assertions, inventory gates, or per-test limits. This is capacity
for the expanded suite, **not a fix for the #1150 memory leak**.

The generated reductions deliberately retain `Unadjudicated`, the instrument's
output. The separate human-readable/JSON parent adjudication interprets those
observations; the instrument does not approve itself.
