# D1: nominal Oblivious policy evidence and decision proposal

**Issue:** [#1400](https://github.com/juanmicrosoft/calor/issues/1400), under
[#1082](https://github.com/juanmicrosoft/calor/issues/1082).
**Date:** 2026-09-12. **Status:** proposal, **Unadjudicated**.
This is a non-shipping, pre-T1 observation checkpoint. It changes no compiler,
converter, harness implementation, diagnostic routing, public policy or release.

## Recommendation for the parent

**Defer Stage B adoption/activation at this checkpoint.** Keep the conservative
candidate: a genuinely Oblivious, resolved, in-scope nominal reference is
possibly null, not safe. The data do not justify a looser trust policy, but
also do not substantiate the migration and identity gates needed to adopt
the candidate now. This recommendation does **not** remove Stage B from 0.22,
authorize a smaller release, or decide policy on the parent's behalf.

The decisive observations are:

1. Source-side reconstruction finds **149 Oblivious nominal obligation
   observations in 28 FluentValidation files**, not 149 Calor rejections.
   These include 147 source-declaration annotations and two instantiated
   delegate signatures. Neither delegate observation proves missing annotations
   on a BCL definition. Nullable-disabled code is not wholesale nullable or
   rejected: eight direct nominal constructor expressions in this same
   source inventory are known non-null.
2. The current Calor target builder supplies names, not declaration/Roslyn
   identities. **384 heuristic nominal-target boundary observations**
   (95 MediatR, 289 FluentValidation) therefore have no justified D1
   policy result. They are not 384 resolved references, failures, or safe
   controls. Some can be value/generic/unresolved encodings; name heuristics
   cannot establish the candidate's actual membership.
3. Executed conversion controls expose both directions of representation
   loss. Nullable-disabled `Echo(Item value) => value` returns null for null
   in both original and generated C#, yet its converted parameter reference
   is `NotAnnotated`. Conversely, `var value = new Item(); return value`
   executes non-null but its converted local reference is `Oblivious`.
   Treating either current annotation as a complete product fact would
   misclassify behavior. #1397/#1380/#1381/#1401 remain relevant.
4. Serilog has a fresh, retained infrastructure failure. FluentValidation's
   original build/tests succeed, but this limited Roslyn reconstruction
   omits generator outputs and reports 11 errors. Actual Calor selected
   reference membership and selected argument maps are not established.
   None of these gaps can be converted into zero unknowns.

Parent options remain **adopt**, **revise a specifically evidenced scope**, or
**defer**. Acceptance of this evidence PR alone must not be recorded as policy
adoption. #1402 must rerun evidence on any implemented candidate; replaying this
observer cannot substitute for activated production negatives and safe controls.

## Provenance and capacity

The accepted slot was recorded in
[issue comment](https://github.com/juanmicrosoft/calor/issues/1400#issuecomment-5643258138)
and the N0 current capacity row **before measurement**:
Copilot context `ada009ab-5eb3-41ac-82cc-68fa9b4e158a`,
name `nominal-policy-1400`. The runtime agent registry did not expose the
implementation model; model confirmation belongs to the parent, not an
invented label. Bounded M AI implementation/measurement/classification capacity
and two separate final reviews were accepted, with provisional checkpoint/
review target 2026-09-12. This is not human staffing, engineer-day equivalence,
statistical independence or a delivery promise.

| Measurement | Exact source |
|---|---|
| Fresh released base | `88b5d38df97fd7e438882c956b9f9dcdc7a6cef5` |
| Existing E1 corpus pipeline | `39348d8c7262bac90120c6d30c01c4476e97acde`; clean tracked diff; only N0 D1 capacity differs from base |
| Final read-only observer and archive source | `d6061f9c90f0a75d67534a1225a2b94a924cfc9c`; clean tracked diff |
| Product / SDK / host | 0.21.0; SDK 10.0.400; .NET runtime 10.0.11; macOS ARM64 |
| Subject runtime policy | explicit `DOTNET_ROLL_FORWARD=Major`, both legs; **not** default execution or .NET8 runtime evidence |
| MediatR corpus | `fb309026775ef953a64fb5339d074426c1ad2c37` |
| Serilog corpus | `0597ddfbd4ec594d9c42edd745fe728a2198bad9` |
| FluentValidation corpus | `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c` |

[Base-to-observer comparison](https://github.com/juanmicrosoft/calor/compare/88b5d38df97fd7e438882c956b9f9dcdc7a6cef5...d6061f9c90f0a75d67534a1225a2b94a924cfc9c)
contains only this documentation/evidence source and the D1 capacity row.
Production source behavior is the released base, **not** PR #1443 or #1445.
At the dependency checkpoint both were open: T1 #1443 at
`6b68a0398351d7cbf0abb58b0d968b86bfa8b0dd`, N3 #1445 at
`1a1dd9533e2d1b46892897fda00bb7c8fa50eaff`. Their owners' work was not imported.

The [manifest](data/manifest.json) pins actual observer/compiler/harness/Roslyn
binary hashes and both compressed and original artifact byte hashes.
E1 reports retain their own binary/options/reference/corpus provenance.
Historical N0 `080ed5a`/`1ea8fe0` and E1 `954aef5f` records are untouched and
are not relabeled as these measurements. The release's MediatR collection
isolation remains identical on baseline/candidate, with no flake waiver.

## Current corpus: all inputs and every declared attempt

These are the **unchanged compiler's** C# baseline versus converted candidate
legs. They are not a widened-nullability compiler comparison.

| Subject | Inventory / excluded / attempted | Kept native + with-losses | Failed conversion/compilation / reverted | Each of two baseline and two candidate suite attempts |
|---|---|---|---|---|
| Synthetic | 5 / 0 / 5 | 5 + 0 | 0 / 0 | 52 passed, 0 failed, 0 skipped |
| Synthetic2 | 1 / 0 / 1 | 1 + 0 | 0 / 0 | 20 passed, 0 failed, 0 skipped |
| MediatR | 32 / 0 / 32 | 18 + 6 | 8 / 0 | 155 passed, 0 failed, 2 skipped |
| Serilog | 112 / 6 / 0 | **unavailable** | conversion not reached / 0 observed reverts | Two baseline attempts failed restore; no valid denominator; candidate attempts not reached |
| FluentValidation | 139 / 0 / 139 | 92 + 4 | 43 / 0 | 865 passed, 0 failed, 1 skipped |

Total inventory is 289 files: 177 attempted, 106 unattempted and six existing
exclusions. Of the 177 attempts, 126 were kept and 51 failed at some stage.
Production Calor compilation separately accepted 155 and rejected 22; accepted
compilation can still fail later validation. These stages must not be summed
as disjoint categories. No corpus conversion crash or recovery reversion
was observed. MediatR's eight failures are two CompileError and six
EmitCompilationError. FluentValidation's 43 are 20 CompileError, 20
EmitCompilationError and three EmitError.

All 16 complete suite attempts passed under the strict all-attempt gate, with
the 12 per-attempt skipped test observations retained (three skipped tests
per full four-subject leg). Serilog adds **two failed baseline attempts**,
not zero-test successes. Its NU1301 SSL/socket restore failure, missing
conversion stages and six configured exclusions remain distinct. This is
one current invocation, not N0's three historical failed invocations.
The shell's project loop continued after the Serilog exit1 to measure
FluentValidation; the loop's final exit does not override the retained
per-project exits.

E1 JSON retains all individual test identities/results/stdout/stderr and
every build attempt. The harness cleans TRX files between attempts:
12 earlier raw TRX files no longer exist and are explicitly marked unavailable
in the manifest; their parsed per-test evidence remains embedded, unchanged.
The four surviving final-attempt TRX files and the 24-case targeted-test TRX
are archived. No later TRX is substituted for an earlier attempt.

## Project context and source-side shadow projection

The observer reuses E1 evaluated contexts, original input hashes, conversion
identities and the real parser/binder. It does not duplicate the harness or
patch its nullability predicate. A separate source-side Roslyn reconstruction
uses the recorded language version, preprocessor symbols, nullable setting,
compile input hashes, reference paths and aliases.

| Subject | Evaluated Nullable | Source files / boundary observations | Directives in pinned input files | Reconstructed reference inputs / errors |
|---|---|---|---|---|
| Synthetic | enable | 5 / 91 | 0 | 167 / 0 |
| Synthetic2 | enable | 1 / 18 | 0 | 167 / 0 |
| MediatR | enable | 32 / 529 | 0 | 161 / 0 |
| Serilog | **unavailable**; physical `Directory.Build.props:12` says enable | no reconstructed context | 0 across all 112 inventory files | unavailable |
| FluentValidation | disable | 139 / 1,915 | 0 | 163 / 11 |

All 189 recorded compile inputs across the four reconstructed contexts were
located with matching byte hashes, including generated input files and an
external package source input. This does **not** include the output of source
generators absent from E1's input inventory. FluentValidation's 11 errors
identify omitted regex/async generator output; they are reconstruction limits,
not failures of its actually passing original build. Source rows retain their
local errors; no complete-project resolution claim is made.

All 658 recorded project-reference occurrences have empty E1 `Sha256` fields
in this run. The observer hashes the actual files it loads, separately, and
records `matchesE1Hash: null`. These are exact **observer-time** reference
bytes, not retroactively proven E1-time hashes. E1's public generated-validation
reference pool is separately fingerprinted. Neither pool is the Binder's
private selected set. Calibration references are likewise the actual reference
set of that explicit calibration compilation only.

The 2,553 source-boundary observations include value, STRING, array, generic,
delegate, unsupported and unresolved rows. Only explicitly typed local
initializations, ordinary native method returns and invocation input syntax
are inventoried. Constructor inputs, mutation/member writes, lambda returns,
`ref`/`out`/`in` inputs and ref returns do not become D1 obligations. Selected
Roslyn parameter/receiver observations are source-side evidence, not proof of
the Calor compiler's selected mappings.

| Source classification | MediatR | FluentValidation |
|---|---:|---:|
| Genuinely Oblivious source at hypothetical bare nominal target | 0 | 149 |
| Explicit nullable source | 44 | 2 |
| Explicit null expression | 0 | 7 |
| Direct known-safe nominal expression | 18 | 8 |
| Declared NotAnnotated | 103 | 9 |
| Nullable target | 10 | 18 |
| Expression transfer unassessed | 37 | 8 |
| Out of bounded nominal scope or unresolved | 317 | 1,714 |

Synthetic/Synthetic2's 109 observations are outside this nominal projection;
their zero Oblivious count is not universal safety. The projection does not
read Roslyn flow state or trust `!` as a guarantee. Composite coalescing and
local-transfer facts stay separate from direct literal/constructor guarantees.
The two FluentValidation delegate-signature observations are not evidence
that `System.Runtime` itself lacks annotations.

A C# target annotation `None` does not promise non-null. Thus the 149 source
observations quantify potential obligations **if converted into bare Calor
contracts**; they are neither a rejection estimate nor a sound upper/lower
bound on future rejection counts. Original-to-Calor boundary membership,
generator completeness and migration behavior are not established.
The prospective rejection numerator and denominator are explicitly **null**,
not 0/0.

## Actual Calor observations and executed controls

The driver invokes the current target builder and current single predicate
read-only. All 177 converted texts parse using `TokenizeAllForParser()`.
It records 2,567 explicit-initialization/native-return observations:
86 Synthetic, 20 Synthetic2, 132 MediatR and 2,329 FluentValidation.
The current predicate returns true for 259 of these (3 Synthetic, 256
FluentValidation); those include non-D1 shapes and do **not** activate errors.
Exact raw diagnostic codes, spans and binding contexts remain in E1.

Of 1,008 observed expression calls, receiver representations are:
485 nominal wrappers, 168 UnresolvedBoundType and 355 unobserved receivers.
A nominal wrapper is not automatically a resolved type identity. The separate
339 statement calls are counted but their selected parameter mappings remain
unassessed; expression calls do not stand in for them. The private per-call
reference selection is unobserved. #1382/#1380/#1381/#1384 own relevant repairs.

The [control source and outputs](data/observations/controls.json.gz) execute
13 argument/result cases across eight methods in **both original and generated
C#**, with identical nullness/string results. Conversion reports no losses and
production compilation reports no diagnostic for this composite control.
That narrow runtime equivalence does not certify a future activated conversion.

| Control | Actual result and representation | D1 disposition |
|---|---|---|
| Disabled `Echo(Item value) => value` | Null remains null; present remains present. Converted parameter reference becomes NotAnnotated | Annotation/context loss; bare non-null target is not a faithful static contract |
| Disabled `new Item()` | Non-null original and converted; direct constructor NotAnnotated | Preserve expression guarantee |
| Disabled inferred local initialized with `new Item()` | Non-null execution; local reference Oblivious | Transfer bug, not genuinely unknown expression behavior |
| Authored `value ?? new Item()` | Non-null for both inputs; result wrapper Oblivious | Explicit original source choice, not converter-inserted migration |
| Authored `value ?? default(Item)` | Null for absent input; raw binder also reports undefined `default`0200 | Not safe or an isolated nullability negative |
| Disabled string literal | `"safe"` on both legs | STRING control; not nominal policy fallout |
| Enabled non-null declaration invoked reflectively with null | Returns null in both versions | Contracts are not a universal runtime null guarantee |
| Explicit nullable target | Preserves null and present cases | Representation control, not Option/reference conversion permission |

Separate metadata calibration emits a small nullable-disabled `Legacy.Item`
library, then resolves `Legacy.API.Read()` as genuinely Oblivious in a
nullable-enabled consumer. Its source and emitted assembly hash are retained;
it is **not** mislabeled as an observed NuGet package. The same consumer resolves
`System.IO.Directory.GetParent("/")` as explicitly Annotated, while `Missing`
produces real CS0246. These distinguish missing annotations, explicit nullable
metadata and unresolved identity without blanket `None` conversion.

### Behavior-preserving converter decision

For an unannotated declaration, permitted future outcomes are:

* Faithful nullable-reference representation **where demonstrated**, including
  declaration identity, null/present execution and downstream receiving
  contracts. This does not mean automatically adding `?` to every declaration.
* Explicit unsupported/raw C# interop preservation, with actual native coverage
  loss kept in the denominator. Passing fallback tests do not erase the loss.
* Deferral when neither outcome can be justified.

This checkpoint implements none of those as a production converter change.
In particular, replacing `Echo(null)` with a default, throw, unwrap or implicit
Option conversion would alter behavior. A user-authored coalesce/default/throw
choice is not an automatic migration. No suppression/trust escape hatch,
annotation blanket or flow-sensitive guarantee is proposed.

## Reproduce and validate

The measured ancestor above retains the exact originally built project.
The final PR stores the observer as non-shipping `.cs.txt`/`.csproj.txt`
source archives, following N0's reproduction-artifact convention, not as
new product C# or a solution project. The first CI head correctly rejected
the original new `.cs` path under the Calor-first guard; no guard or allowlist
was changed. The commands below materialize the archived source in a local
reproduction directory. Use an isolated checkout of the final PR head,
initialize only the three product corpus submodules, and keep scratch
files under that checkout.

```bash
git submodule update --init -- bench/corpus/MediatR bench/corpus/serilog bench/corpus/FluentValidation
mkdir -p .d1-work/runtime .d1-work/reports .d1-work/logs
printf '<Project />\n' > .d1-work/runtime/Directory.Build.props
printf '<Project />\n' > .d1-work/runtime/Directory.Build.targets
printf '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>\n' > .d1-work/runtime/Directory.Packages.props
export TMPDIR="$PWD/.d1-work/runtime" CALOR_TELEMETRY=0 DOTNET_ROLL_FORWARD=Major
unset CALOR_NO_TYPE_CHECK
dotnet build tools/Calor.RoundTrip.Harness --nologo
# If that reports missing Z3 assets, run the existing download script, then rebuild.
for project in Synthetic Synthetic2 MediatR Serilog FluentValidation; do
  dotnet run --no-build --project tools/Calor.RoundTrip.Harness -- \
    run "$project" --dotnet dotnet --capture-binding-analysis --test-attempts 2 \
    --output "$PWD/.d1-work/reports" > ".d1-work/logs/$project.log" 2>&1
  printf '%s\t%s\n' "$project" "$?" >> .d1-work/run-exits.tsv
done
mkdir -p .d1-work/probe
cp docs/plans/evidence/nominal-policy-1400/reproduce/Program.cs.txt .d1-work/probe/Program.cs
cp docs/plans/evidence/nominal-policy-1400/reproduce/Probe.csproj.txt .d1-work/probe/Probe.csproj
cp docs/plans/evidence/nominal-policy-1400/reproduce/packages.lock.json .d1-work/probe/packages.lock.json
dotnet build .d1-work/probe/Probe.csproj -p:CalorRoot="$PWD"
dotnet run --no-build --project .d1-work/probe/Probe.csproj -p:CalorRoot="$PWD" -- \
  "$PWD" "$PWD/.d1-work/reports" "$PWD/.d1-work/observations"
python3 docs/plans/evidence/nominal-policy-1400/reproduce/summarize.py \
  .d1-work/observations .d1-work/reports .d1-work/summary
dotnet test tests/Calor.RoundTrip.Harness.Tests --filter \
  'FullyQualifiedName~RunConfigOverrideTests|FullyQualifiedName~UpstreamFlakeGateTests|FullyQualifiedName~RoundTripExitPolicyTests'
```

The existing targeted selection passed **24/24**, including collection
isolation and strict gate behavior. The observer's validation checks the five
inventories, all declared attempts, metadata/constructor/null calibration,
and 13 paired runtime outputs. The wider local harness suite was not
claimed green: the previously reported unrelated
`RoundTripPipelineSafetyTests` recovery diagnostic failure at line167 remains
a known base/patch local issue, not silently waived.

Initial observer development failures (bad project path, lock refresh, language
version spelling, external source relocation, AST aliasing and optional
candidate fields) are retained in logs. One development pass used raw lexer
tokens rather than parser-ready indentation tokens; its spurious parse errors
were corrected before the pinned measurement, not attributed to the product.
The Z3 missing-asset build failure and subsequent existing-script restoration
are also retained. No dependencies or new test framework were added to the product.

## Classification, review and decision gate

[Classification draft](data/observations/classification-draft.json.gz) has
2,217 rows, including every E1 file/diagnostic and 202 source obligation
observations. The automated template records the implementation analyst;
it is not 2,217 independent manual adjudications. 106 infrastructure rows
and 2,111 unresolved rows remain explicit; configured exclusions are not
infrastructure failures. The implementation context manually classified the
narrow controls above. Allow one bounded 60-minute classification/review
window for each independent final context; escalate unresolved evidence
rather than treating the planning band as staffing or declaring rows safe.

Two separately initiated, non-author final-head reviewers must read this
proposal, live issues, source, raw evidence and final diff:

1. **Compiler integration:** verify original/converted identity and annotation
   claims, source/context/projection limitations, receiving-shape boundaries,
   migration fidelity and preservation of the shipping compiler.
2. **Adversarial compatibility:** challenge denominators and every attempt,
   missing references/receivers/generators, constructor/literal controls,
   deferred/adopted wording, provenance and no hidden exclusions.

Exact prompts, requested/available model identity, context UUIDs, reviewed
final SHA, findings/dispositions and CI are recorded on the issue PR.
Separate AI contexts can be correlated and do not establish statistical
independence or human review. Material fixes require final-head re-review.
Only the parent may adjudicate adoption/deferral, merge or close the issue.

Stage A remains independently gated. D3/D12/D14, runtime guards, Calor0710 and
old-major fail-closed semantics are unchanged. No full milestone completion,
release/publication or paused-research resumption follows from this proposal.
