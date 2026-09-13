# N4 corpus delta classification (#1383)

## Source pins

- Candidate worktree at corpus regeneration: `files/worktrees/native-string-1383`, HEAD `bde9ee88627acb1fcdf62494b701b24856fa5881`. Later evidence-only probes ran after parent docs/test-only updates at HEAD `7698718b1d65471c07b4e459e29335fc6a84a90c`; production `Binder.cs`/`Scope.cs` blobs still match immutable `f642b11`. Draft PR: #1455. Commit `7698718b1d65471c07b4e459e29335fc6a84a90c` contains corpus/pin/index/docs changes only; the complete PR also contains the implementation ancestor.
- Immutable measured implementation: `f642b11aa5249dc1f9369195af25dcc881ce9f43`.
- Accepted baseline worktree: `files/worktrees/n4-corpus-baseline`, detached at `8f9891a1a07f786a76a293017959c3edf28412bf`.
- Production blobs at `f642b11` and candidate HEAD are identical:
  - `src/Calor.Compiler/Binding/Binder.cs` blob `124d551927ec8819a268fb5814dec57f48cba5c6`
  - `src/Calor.Compiler/Binding/Scope.cs` blob `f8e2189a7795be7a9fb84bdb70c38db728b7360b`
- Baseline production blobs:
  - `Binder.cs` blob `36a6a79b06257ab01a6a405270e581878696bddd`
  - `Scope.cs` blob `2f3e5ecc083b8301cea096ae4a3c1f1040f81eb0`
- Submodules in both worktrees:
  - `bench/corpus/FluentValidation` `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c`
  - `bench/corpus/MediatR` `fb309026775ef953a64fb5339d074426c1ad2c37`
  - `bench/corpus/serilog` `0597ddfbd4ec594d9c42edd745fe728a2198bad9`
- Z3 assets verified with `python3 scripts/verify-z3-assets.py` in both worktrees.
- Runtime: .NET SDK `10.0.400`, host runtime `10.0.11`, RID `osx-arm64`. No .NET 8 evidence used.
- Manifest-only record: `bench/phase0-agent-native/metadata-references-manifest.json`, SHA-256 `b82830666f4bbb1b2e3e01b35d3df0dab94c04c5a3c94a276daa18f3584e4b4d`, `sdkVersionRange` `10.0.0 - 10.9.999`, `generatedFrom` `/opt/homebrew/Cellar/dotnet/10.0.302/libexec/shared/Microsoft.NETCore.App/10.0.10`, 167 pinned entries. The files under `metadata/*-metadata-reference-profile.json` are now explicitly labeled manifest-only and are not claimed as the realized private reference selection. Actual realized `MetadataContext` compilation references were captured in `metadata-realized/*-metadata-realized-references.json`: generated compiler reference pool 184, selected context references 168, pool-minus-context 16, all with identity/path/hash on the .NET 10.0.11 host.

## Commands

Baseline regeneration at exact `8f9891a`:

```bash
TMPDIR=$F/n4-corpus-evidence/tmp/baseline-test \
CALOR_UPDATE_BINDER_BASELINE=1 CALOR_REGENERATE_CALOR0425_LEDGER=1 \
dotnet test tests/Calor.Compiler.Tests/ \
  --filter 'FullyQualifiedName~BinderIncompleteRatchetTests|FullyQualifiedName~Calor0425CorpusLedgerTests' \
  --verbosity normal --logger 'trx;LogFileName=n4-baseline-regenerate.trx'
```

Result: 22/22 passed. Log: `logs/n4-baseline-regenerate.log`.

Candidate regeneration:

```bash
TMPDIR=$F/n4-corpus-evidence/tmp/candidate-test \
CALOR_UPDATE_BINDER_BASELINE=1 CALOR_REGENERATE_CALOR0425_LEDGER=1 \
dotnet test tests/Calor.Compiler.Tests/ \
  --filter 'FullyQualifiedName~BinderIncompleteRatchetTests|FullyQualifiedName~Calor0425CorpusLedgerTests' \
  --verbosity normal --logger 'trx;LogFileName=n4-candidate-regenerate.trx'
```

Result: 22/22 passed. Log: `logs/n4-candidate-regenerate.log`.

Normal no-update moved-pin verification:

```bash
TMPDIR=$F/n4-corpus-evidence/tmp/moved-pins \
dotnet test tests/Calor.Compiler.Tests/ \
  --filter 'FullyQualifiedName~BinderIncompleteRatchetTests.ConversionLeg_IncompleteCount_MatchesBaseline|FullyQualifiedName~Calor0425CorpusLedgerTests.Calor0425CorpusLedgerMatchesRecomputation' \
  --verbosity normal --logger 'trx;LogFileName=n4-moved-pins-final.trx'
```

Result: 2/2 passed. Log: `logs/n4-moved-pins-final.log`.

Full originally requested no-update filter:

```bash
TMPDIR=$F/n4-corpus-evidence/tmp/final-ratchets \
dotnet test tests/Calor.Compiler.Tests/ \
  --filter 'FullyQualifiedName~NativeStringApplicabilityTests|FullyQualifiedName~BinderIncompleteRatchetTests|FullyQualifiedName~Calor0270CorpusVolumeTests|FullyQualifiedName~Calor0425CorpusLedgerTests' \
  --verbosity normal --logger 'trx;LogFileName=n4-corpus-final.trx'
```

Result: 62 total, 61 passed, 1 failed. The remaining failure was `Calor0425CorpusLedgerTests.R1_NamesTheLargestBindingCluster_AndTheFrozenRuleSizesR2`: hard-coded test pin said `ModulesEnforced` must be 326, regenerated ledger correctly measures 327. This was outside my allowed edit scope (no test-code edits) and was later fixed by the parent/test owner.

## Changed golden paths

- `bench/phase0-agent-native/binder-source-coverage.json`
- `bench/phase0-agent-native/calor0425-corpus-ledger.json`

`bench/phase0-agent-native/binder-incomplete-baseline.json` is unchanged versus actual `8f9891a`.

## Final validation handoff notes

Parent-owned validation after this measurement:

- Draft PR #1455 is at `7698718b1d65471c07b4e459e29335fc6a84a90c`; that latest commit contains corpus/pin/index/docs changes only. Production `Binder.cs`/`Scope.cs` remain the frozen `f642b11` blobs named above.
- Full ordinary compiler-project run artifact: `$F/n4-final-validation/n4-full.trx` and `$F/n4-final-validation/full-compiler.log`. It established the actual execution inventory as 9102 tests: 9098 passed, 1 failed, 3 skipped. The sole failure was `LedgerCommitStampTests.EveryIndexEntryStillMatchesItsLedgersOwnStamp`, caused by the Calor0425 index stamp still naming `b2c43561` while the measured ledger names `bde9`. This artifact must not be relabeled as a full pass.
- Parent then surgically updated only the Calor0425 index entry to `bde9` with measurement attribution; research-related entries were untouched.
- Final targeted ordinary ratchet artifact: `$F/n4-final-validation/n4-final-ratchets.trx` and `$F/n4-final-validation/final-ratchets.log`. Selection `FullyQualifiedName~BinderIncompleteRatchetTests|FullyQualifiedName~Calor0425CorpusLedgerTests|FullyQualifiedName~LedgerCommitStampTests` passed 25/25 with 0 skips, confirming the recomputation, exact R1 pin, source guard, and index assertions.
- Raw `--list-tests` discovery count 9034 is not an executed-test inventory and is not used as measured evidence.


## Binder/source coverage deltas

### `serilog/src/Serilog/Events/LogEventProperty.cs`

Source/coverage identity is unchanged: source hash `ac398230b91da5c0bac90aedd7ec51503546a321783b2c62c195a8c26a1365fc`; binder attempts 27; source expressions 79; exact spans/opaque/unmapped coordinates unchanged.

One propagated binding error is removed:

- Baseline diagnostic: `Calor0208` span `1377..1397`, line 35 col 32, propagated compilation error, message `No overload of internal call 'IsValidName' matches (STRING). Candidates: Serilog.Events.LogEventProperty.IsValidName(OPTION[inner=STRING]) [...]`, snippet `§C{IsValidName} name`.
- Candidate: no diagnostic at that call. The selected call is expression `IsValidName`, argument 0 `name` type `str`/canonical `STRING`, selected overload `Serilog.Events.LogEventProperty.IsValidName(OPTION[inner=STRING])`, return `BOOL`, mapping arg 0 -> parameter 0 `name`, effective parameter type `OPTION[inner=STRING]`/nullable string.
- Classification: intended non-null -> nullable-accepting compatibility repair. It drops an old false `Calor0208`; it is not a 208/207 -> 274 handoff, not broad Stage A, not a mitigation lift. Constructors are not involved.

The remaining three raw binding diagnostics (`EventProperty.None`, `property.Name`, `property.Value`) remain `Calor0200` analysis-only and unpropagated.

### `serilog/src/Serilog/Formatting/Json/JsonValueFormatter.cs`

Source/coverage identity is unchanged: selected source hash `84be8c16764d9f5a982d8aa51e182e2254e4a2adf40612d02176e8140b77a769`; binder attempts 392; exact/opaque/unmapped coordinates unchanged. Raw binding errors increase 55 -> 56, propagated errors remain 0.

One analysis-only diagnostic is added:

- Candidate diagnostic: `Calor0274` span `3343..3355`, line 86 col 40, `BindingContext` `MethodArgument/ScalarString`, `ReplacesNativeOverloadError=false`, analysis-only, snippet `_typeTagName`, message `Argument to parameter 'str' declares non-nullable 'string' but the value may be null (source annotation: 'Annotated'). Change the parameter type to '?string' or add an explicit non-null check at the interop boundary.`
- Selected call: statement `WriteQuotedJsonString`, args `_typeTagName` type `?str`/canonical nullable string and `state` type `TextWriter`; selected overload `Serilog.Formatting.Json.JsonValueFormatter.WriteQuotedJsonString(str, TextWriter)`, return `VOID`, mapping arg 0 -> parameter `str` effective `str`/non-null string; arg 1 -> `output` `TextWriter`.
- Baseline had no propagated/public rejection for this call; the no-match was under the existing unresolved/invisible suppression condition (`TextWriter` is not resolved in the single-file converted module). Therefore the new `Calor0274` correctly stays analysis-only and does not change production gating.

## Calor0425 ledger deltas

Aggregate `Calor0425` effect-row diagnostics do not change: 117 sites across 47 modules. Disposition buckets are stable: `UnknownSource + InvocationUndetermined = 0`, `Assumed = 0`, invocation assumed = 0, D3/D12/D14-style demotions/guards remain untouched by this measurement. No `Calor0425` site is added or removed.

The only denominator movement is Serilog:

- `ModulesEnforced` 98 -> 99; aggregate 326 -> 327.
- `ModulesNotMeasured` 14 -> 13; aggregate 38 -> 37.
- `ExcludedBindFailed` 14 -> 13; aggregate propagated bind-failed 38 -> 37.
- `BindFailureCauses.Calor0208` 6 -> 5; `Calor0201` stays 1 and `Calor0250` stays 7.
- Removed bind-failure module: `Calor0208 Serilog/Events/LogEventProperty.cs`.
- `Calor0411Sites` 551 -> 555; `Calor0411Modules` 61 -> 62.
- CLI cross-check agrees: Serilog `BindStopped` 14 -> 13, `ReachEffectPass` 98 -> 99, `StopCalor0410` 56 -> 57.

Per-module CLI proof for `LogEventProperty.cs` used the recorded default invocation `dotnet <worktree>/src/Calor.Compiler/bin/Debug/net10.0/calor.dll -i m.calr -o out.g.cs` from the scratch module directory, with no semantic flags. It shows baseline stopped at `Calor0208`; candidate reaches effects and emits 4 `Calor0411` warnings plus `Calor0410`/`Calor0422` errors. Log files: `logs/n4-baseline-LogEventProperty-cli.log`, `logs/n4-candidate-LogEventProperty-cli.log`. The v2 boundary controls record their exact default CLI invocations separately and run them from the repo cwd so the private metadata manifest discovery matches `Program.Compile`.

## Classification verdict

No product defect found. The measured product corpus delta is exactly one intended Serilog denominator recovery (`LogEventProperty.cs`) plus one new unpropagated analysis-only nullable argument diagnostic in `JsonValueFormatter.cs`. No unknown/incomplete/non-emitting input was classified as safe; source hashes, conversion status, source/opacity/coverage coordinates, binder-attempt counts, and non-diagnostic coverage identities are unchanged. Diagnostic propagation is explicitly changed, not preserved: `LogEventProperty.cs` propagated binding errors move 1 -> 0, aggregate propagated binding diagnostics move 109 -> 108, and aggregate bind-stopped modules separately move 38 -> 37 (Serilog 14 -> 13).

The downstream exact-pin failure was later fixed by the parent/test owner to name the 327 denominator; I did not edit tests per scope.

## Native boundary baseline/candidate controls

Initial raw-binder evidence controls are preserved in `control-results/` and `historical-initial-attempts-20260912T0625/`. Corrected strict controls are in `native-controls/` and `control-results-v2/`. These are evidence-only, not product/test code, and do not change source.

Reproduction command shape for the corrected native controls:

```bash
dotnet run --project $F/n4-corpus-evidence/native-controls/NativeBoundaryProbe.csproj \
  /p:CalorRepoRoot=<repo> -- <repo> \
  $F/n4-corpus-evidence/control-results-v2/<baseline|candidate>-native-boundary-controls-v2.json \
  $F/n4-corpus-evidence/tmp/native-controls-v2/<baseline|candidate>
```

The contributor did not retain exact historical outer shell commands, shell
working directories or environment settings for the strict v2 corpus, native
or metadata probe launches. This block is not a historical transcript or
evidence of an exact launch count/TMPDIR value. Native and metadata driver
source explicitly sets its process working directory to the supplied repo;
native result JSON separately records each actual child CLI invocation,
working directory, exit code and binary hash. The corpus driver does not set
or record its inherited working directory and does not capture its loaded
compiler assembly. Do not use the separate metadata probe as proof of that
corpus process's complete environment or reference selection.

Corrected controls used baseline commit `8f9891a1a07f786a76a293017959c3edf28412bf` and current candidate docs/test HEAD `7698718b1d65471c07b4e459e29335fc6a84a90c`; candidate production blobs still match immutable `f642b11`. Every paired control has matching baseline/candidate source SHA-256. Results separate raw binder routing, default `Program.Compile`, and default CLI outcome.

Observed boundaries:

1. `direct-nullable-input`
   - Baseline: propagated `Calor0208` over call `Take(STRING?)` against candidate `Take(str)`.
   - Candidate: propagated `Calor0274`, `MethodArgument/ScalarString`, `ReplacesNativeOverloadError=true`, selected `Take(str)` with arg `string?` -> param `str`.
   - Classification: faithful old `NoMatch` -> `274` ownership handoff.

2. `nonnull-to-nullable-target`
   - Baseline: propagated `Calor0208` for `Take(STRING)` against `Take(?str)`.
   - Candidate: no diagnostic; selected `Take(?str)` with arg `STRING` -> param `?str`.
   - Classification: safe non-null -> nullable accepting compatibility repair; no nullable risk and no `274`.

3. `already-applicable-object-alternative`
   - Baseline: selected `Take(object)`, no diagnostic.
   - Candidate: selected `Take(str)` but emitted `Calor0274` with `ReplacesNativeOverloadError=false`; disposition analysis-only, unpropagated.
   - Classification: already-applicable OBJECT alternative remains analysis-only; no public rejection is created.

4. `old-nullable-string-ambiguity-handoff`
   - Baseline: propagated `Calor0207` ambiguity for `Take(STRING?)` between `Take(any, INT)` and `Take(any, BOOL)`.
   - Candidate: propagated `Calor0274`, `MethodArgument/ScalarString`, `ReplacesNativeOverloadError=true`, selected `NativeString.Take(str)`.
   - Classification: faithful old `Ambiguous` -> `274` ownership handoff.

5. `old-numeric-ambiguity-preserved`
   - Baseline and candidate: propagated `Calor0207` for `Take(INT)` between `Take(i64)` and `Take(f64)`.
   - Classification: non-string ambiguity remains unchanged.

6. `invisible-argument-suppression`
   - Baseline raw binder/routing: no propagated diagnostic; this is not labeled public acceptance.
   - Candidate: selected `Take(str, Invisible)` but emitted `Calor0274` with `ReplacesNativeOverloadError=false`; disposition analysis-only, unpropagated.
   - Classification: existing unresolved/invisible suppression remains analysis-only; no broad Stage A activation.

Summary JSON: `control-results-v2/native-boundary-controls-v2-summary.json`.

## 2026-09-12 evidence corrections

### Realized metadata references

The original `metadata/*-metadata-reference-profile.json` files are manifest-only copies (167 committed entries generated from .NET 10.0.10). They are preserved and labeled as such. They are not used as a claim about the realized private reference set.

The corrected realized-reference probe is `metadata-realized-probe/`. It sets its process working directory to the supplied baseline or candidate repo, checks child process exit codes for git/dotnet provenance, and writes `metadata-realized/*-metadata-realized-references.json` plus `metadata-realized/metadata-realized-summary.json`. Its outer shell launch cwd/environment was not retained.

Observed realized private metadata selection on this .NET 10.0.11 host:

- Baseline HEAD `8f9891a1a07f786a76a293017959c3edf28412bf`; loaded compiler assembly `metadata-realized-probe/bin/Debug/net10.0/calor.dll`, SHA-256 `e0c26efc29f7c70ede0206d72a799dd02d6d10ef5d38005ce98e06f80235ed5b`.
- Candidate evidence HEAD `7698718b1d65471c07b4e459e29335fc6a84a90c`; loaded compiler assembly `metadata-realized-probe/bin/Debug/net10.0/calor.dll`, SHA-256 `735fa2eccf16a5be45bc9cb6a36718fd118ef50ae65d2267ea8b94a3a620036f`.
- Candidate source pins still match immutable `f642b11`: `Binder.cs` blob `124d551927ec8819a268fb5814dec57f48cba5c6`; `Scope.cs` blob `f8e2189a7795be7a9fb84bdb70c38db728b7360b`.
- `GeneratedCSharpCompiler.References` pool: 184 portable references.
- Realized `MetadataContext.HostCompilationForBinder.References`: 168 portable references.
- Pool-minus-context: 16 references, including compiler/test/dependency assemblies and framework facades, listed with path/hash in the JSON.
- Baseline and candidate selected reference sets are identical by `(file, assembly identity, version, SHA-256)`; set hash `10f43cd51bd432ddbe066398f55be4c631b411995b88aba98055907961ffbab1`.

### Strict probe behavior

Both probe programs now avoid success-shaped fallbacks:

- `probe/Program.cs.txt` fails nonzero on empty conversion output, converted-parse errors, effect-pass exceptions, uncataloged binder diagnostics, and failed git child commands.
- `native-controls/Program.cs.txt` fails nonzero on parse failure, uncataloged binder diagnostics, missing CLI assembly, and failed provenance commands. CLI compilation itself is captured with exit code because compile failures are the expected measured outcome for some samples.
- Initial broad-catch/raw-only outputs are preserved under `historical-initial-attempts-20260912T0625/`.

Strict corpus probes (`probe-results-v2/`) reproduce the same classified product deltas:

- `LogEventProperty.cs`: baseline raw binder `Calor0208` + 3 `Calor0200`, propagated `{Calor0208:1}`; candidate raw binder 3 `Calor0200`, propagated `{}`; candidate effect pass then emits 4 `Calor0411`, 1 `Calor0410`, 2 `Calor0422`.
- `JsonValueFormatter.cs`: baseline raw binder 51 `Calor0200`, 3 `Calor0274`, 1 `Calor0272`; candidate adds exactly one more raw `Calor0274`; propagated remains `{}` on both; effect diagnostics unchanged (77 `Calor0411`, 4 `Calor0425`, 25 `Calor0410`, 1 `Calor0422`).

Strict native controls (`control-results-v2/`) separate raw binder routing from actual default `Program.Compile` and default CLI. All paired sample source SHA-256 values match between baseline and candidate.

- Direct nullable input, expression and statement forms: baseline raw/`Program.Compile`/CLI all produce propagated/default `Calor0208`; candidate raw/`Program.Compile`/CLI all produce `Calor0274` with `MethodArgument/ScalarString` and `ReplacesNativeOverloadError=true`.
- Non-null argument to nullable target, expression and statement forms: baseline produces `Calor0208`; candidate raw/`Program.Compile`/CLI are clean.
- Already-applicable `object` alternative: baseline raw/compile/CLI clean; candidate raw binder records analysis-only `Calor0274` (`ReplacesNativeOverloadError=false`), while default `Program.Compile` and CLI stay clean.
- Nullable-string ambiguity handoff: baseline raw/compile/CLI produce `Calor0207`; candidate raw/compile/CLI produce `Calor0274` with `ReplacesNativeOverloadError=true`.
- Non-string numeric ambiguity: baseline and candidate raw/compile/CLI continue to produce `Calor0207`.
- Invisible/unmodeled argument suppression: candidate raw binder records analysis-only `Calor0274` (`ReplacesNativeOverloadError=false`), but default `Program.Compile` and CLI diagnostics are unchanged from baseline (`Calor0200`/`Calor1002` generated validation/unmodeled-source errors). This is not claimed as public acceptance.

No `Calor0275` evidence was observed or claimed, and these controls do not establish broad Stage A activation.
