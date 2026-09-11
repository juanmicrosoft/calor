# N0 evidence: 2026-09-11

This is the scoped, unpaid local measurement record for
[#1379](https://github.com/juanmicrosoft/calor/issues/1379), governed by
[the N0 plan](../../v0.22-nullability-enforcement-scoping.md).
It changes no compiler behavior or benchmark-study semantics.

## Provenance and interpretation

Primary candidate: `080ed5a7482612130fa7572dfebe0f3c58a87ea2`.
`compatibility-1ea8.json` and `cache-default-layout.json` are explicitly later
measurements at `1ea8fe0bfddfb4bddac85fe5698d750c2a1939c4`.
The latter commit changes source comments and a legacy-major hint, not binding
or cache logic. All other reports retain their original candidate.

`api-lsp-baseline.json` contains all40 final source strings, source hashes,
independent parser/TypeChecker/binder results, bound expression kind/annotation/
identity trees, symbol type-name strings, actual LSP ranges, seven API modes,
default emitted C#, executed runtime controls, conversion inputs/loss reports,
compiler binary hash and all168 metadata-reference hashes/identities.

The diagnostic field `compilationError` means only
`BindingDiagnosticPolicy.IsCompilationError(diagnostic)`. It is **not** the
overall outcome of TypeChecker, parsing or compilation. Use `HasErrors`, the
pass-specific arrays and CLI exits for that. No diagnostics does not mean a
reference is resolved or safe; nominal OBJECT/Oblivious can represent unknowns.

`case-matrix.md` supplies the manual source/receiving-target and migration
classification. It deliberately distinguishes resolved failures, unsupported
representations, false positives and runtime counterexamples.
`cli-matrix.json` has152 argv/env/exit/parsed-JSON records and raw stdout/stderr.
Its `.n0-evidence/...` source-artifact paths identify the original capture;
the embedded `stdoutText`/`stderrText` are the durable outputs.
`surfaces.json`, `sdk-matrix.json`, `reuse.json` and the cache supplement retain
the other actual entry-point/reuse observations.

`standalone-corpus.jsonl` pins244 tracked inputs and every observed pass result;
`standalone-summary.json` preserves file-versus-diagnostic denominators and
error paths. It does not classify accepted files as safe. `roundtrip/` retains
successful reports and **all** failed/incomplete attempts, including the
unreached FluentValidation leg and Serilog restore crashes. No recovery,
interop, skipped-test or failed-file count is silently folded into "native."
`tests.json` records every selected existing test name/outcome and TRX counters.

Absolute owned-worktree paths are replaced with `<REPO>`; help captures have
trailing empty lines trimmed. Framework paths and
assembly hashes are the actual host inputs. Source/generated hashes were
captured before presentation normalization; `#line` paths affect emitted bytes.
Timestamps, host references and compiler binary hashes are environment-specific.
`sha256.json` hashes the committed presentation artifacts (excluding itself).

## Reproduce the primary measurement

Use an isolated checkout of the exact primary candidate; do not change a
working user's root checkout or frozen research assets. Copy this evidence's
`reproduce/` files from the N0 PR into that checkout's hidden `.n0-evidence`
workspace. They are stored with `.txt` suffixes for C# so documentation evidence
is not accidentally compiled as new product/test code by repository globs.

```bash
mkdir -p .n0-evidence/probe .n0-evidence/tmp
cp PATH_TO_THIS_EVIDENCE/reproduce/Probe.cs.txt .n0-evidence/probe/Probe.cs
cp PATH_TO_THIS_EVIDENCE/reproduce/Probe.csproj.txt .n0-evidence/probe/Probe.csproj
cp PATH_TO_THIS_EVIDENCE/reproduce/*.py .n0-evidence/probe/
export TMPDIR="$PWD/.n0-evidence/tmp"
export CALOR_TELEMETRY=0
unset CALOR_NO_TYPE_CHECK
git submodule update --init
src/Calor.Compiler/scripts/download-z3.sh
git ls-files -- samples benchmarks tests/TestData/Benchmarks |
  grep '\.calr$' > .n0-evidence/tracked-inputs.txt
dotnet run --project .n0-evidence/probe/Probe.csproj -- \
  "$PWD" "$PWD/.n0-evidence/cases" "$PWD/.n0-evidence/tracked-inputs.txt"
python3 .n0-evidence/probe/cli.py "$PWD"
python3 .n0-evidence/probe/surfaces.py "$PWD"
dotnet build src/Calor.Tasks --nologo
python3 .n0-evidence/probe/msbuild.py "$PWD"
```

`dotnet run` builds the referenced Compiler and LanguageServer projects before
the Python CLI drivers run. The probe itself clears `CALOR_N0_UNSET` before
executing runtime cases; an inherited value cannot change those observations.

Before running generated execution/MSBuild/harness projects, create the same
neutral scratch-ancestor files in `.n0-evidence/tmp`: `Directory.Build.props`
and `Directory.Build.targets` each contain `<Project />`;
`Directory.Packages.props` contains
`<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`.
These are scratch isolation settings, not edits to repository or subject
configuration. Subject-local nearer settings still apply.

The recorded first ordinary targeted test failed for missing Z3 assets.
The successful run used existing owned-worktree assets verified against the
repository manifest, equivalent to downloading valid platform assets.
Normal main-project restores otherwise worked; no global NuGet configuration
was changed. A scratch restore updated a tracked LanguageServer lockfile's
project-version minimum; that incidental change was inspected and restored,
not included in this PR.

Run the default-output cache supplement with the documented later candidate:

```bash
python3 .n0-evidence/probe/cache.py "$PWD"
```

The script pins the measured candidate in its result; do not run it against a
different compiler and relabel the result. The initial explicit-output controls
in `surfaces.py` intentionally cannot hit the root incremental cache.

Existing targeted runners, with TRX output directed into the hidden workspace:

```bash
dotnet test tests/Calor.Compiler.Tests --filter \
  'FullyQualifiedName~NullabilityIntegrationTests|FullyQualifiedName~NullabilityCheckerTests|FullyQualifiedName~MetadataBinderNullabilityTests|FullyQualifiedName~BoundTypeArchitectureTests|FullyQualifiedName~BinderOverloadSetTests'
dotnet test tests/Calor.Verification.Tests --filter \
  'FullyQualifiedName~W1Slice1SoundnessTests|FullyQualifiedName~ArrayLengthSoundnessTests|FullyQualifiedName~MethodElisionCursorTests'
dotnet test tests/Calor.Compiler.Tests --filter \
  'FullyQualifiedName~IncrementalCliBuildTests|FullyQualifiedName~VerificationCacheTests'
dotnet test tests/Calor.Tasks.Tests --filter 'FullyQualifiedName~BuildStateCacheTests'
dotnet test tests/Calor.LanguageServer.Tests --filter \
  'FullyQualifiedName~DocumentStateTests|FullyQualifiedName~DiagnosticConverterTests'
```

Recorded outcomes:139,92,54,40,42 passed respectively; no selected test failed
or skipped. This is targeted baseline coverage, not the entire test suite.

Existing corpus runner commands (exit134 is a recorded crash, not success):

```bash
dotnet run --project tools/Calor.RoundTrip.Harness -- \
  run --all --dotnet dotnet --output "$PWD/.n0-evidence/roundtrip"
DOTNET_ROLL_FORWARD=Major dotnet run --no-build --project tools/Calor.RoundTrip.Harness -- \
  run --all --dotnet dotnet --output "$PWD/.n0-evidence/roundtrip-attempt2"
DOTNET_ROLL_FORWARD=Major dotnet run --no-build --project tools/Calor.RoundTrip.Harness -- \
  run FluentValidation --dotnet dotnet --output "$PWD/.n0-evidence/roundtrip-fluent"
DOTNET_ROLL_FORWARD=Major NuGetAudit=false RestoreSources=https://api.nuget.org/v3/index.json \
  dotnet run --no-build --project tools/Calor.RoundTrip.Harness -- \
  run Serilog --dotnet dotnet --output "$PWD/.n0-evidence/roundtrip-serilog-attempt3"
```

The first all-project run lacked the .NET8 runtime for MediatR and later
aborted in Serilog restore. The second used explicit major roll-forward and
again aborted at Serilog. FluentValidation was then run independently.
The final invocation-only NuGet mitigation followed actual restore failures
and did not fix Serilog. The missing-runtime message was read from the first
MediatR TRX; the report retains its incomplete comparison and nonzero exits.

No blanket rerun-until-green policy, timeout relaxation, converter repair,
reference substitution, forced nullable activation or paid model call was used.
