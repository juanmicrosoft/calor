# Safe consumption #1398 T2 corpus diagnostic audit

**Outcome:** baseline acceptance is blocked. The measured T2 corpus run changes eight
`binder-source-coverage.json` rows, but four rows introduce real
`Calor0200` undefined-variable diagnostics for pattern variables that should be in scope
in supported `&&` conditions or their true branch bodies. I restored the regenerated
coverage file to the approved N1 contents and did not update the ratchet baseline.

## Scope and runtime

- Current T2 source: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398`,
  branch `fix/1398-safe-consumption`, HEAD `2d6e153062458513d7010dba92b0031e5e6d2412`.
- Approved N1 baseline source: detached worktree
  `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/t2-corpus-baseline-1398`,
  HEAD `3b513a632149290481161b195a71aa6cb0e251a8`.
- Corpus gitlinks were identical in both worktrees:
  FluentValidation `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c`,
  MediatR `fb309026775ef953a64fb5339d074426c1ad2c37`,
  serilog `0597ddfbd4ec594d9c42edd745fe728a2198bad9`.
- Runtime/profile: .NET SDK `10.0.400` on macOS Darwin 25.6.0 arm64. The detached N1
  worktree initially failed on missing Z3 assets; after that observed failure, the
  already-verified T2 `src/Calor.Compiler/z3/` and `src/Calor.Compiler/runtimes/`
  assets were copied into only that detached baseline worktree.

## Measurement commands

Initial T2 ratchet run from the requested worktree failed as expected:

```bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398
dotnet test tests/Calor.Compiler.Tests/ \
  --filter "FullyQualifiedName~BinderIncompleteRatchetTests.ConversionLeg_IncompleteCount_MatchesBaseline" \
  --no-restore --logger "console;verbosity=minimal"
```

The failure was the first row mismatch in
`FluentValidation/src/FluentValidation.Tests/NotEqualValidatorTests.cs`:
`BindingErrorIdentityHash`
`bfccd6b797d3ad802ead70c350bee7c7fc8bd40aa7afafc08f085ae20c3f646d` ->
`37c14a9d6978f476e772e207ac8ac498bc6e6eb731813e458000b87474b19161`.

After the defective baseline update was rejected and the approved N1 coverage file was
restored, the same ratchet command was repeated and failed with the same first mismatch.
There is no successful repeat-ratchet result for this audit because accepting the measured
file would bless real pattern-scope binding regressions.

I then ran the existing regeneration path as a measurement only:

```bash
CALOR_UPDATE_BINDER_BASELINE=1 dotnet test tests/Calor.Compiler.Tests/ \
  --filter "FullyQualifiedName~BinderIncompleteRatchetTests.ConversionLeg_IncompleteCount_MatchesBaseline" \
  --no-restore --logger "console;verbosity=minimal"
```

That measurement passed and showed `binder-incomplete-baseline.json` unchanged. The
regenerated `binder-source-coverage.json` was copied to the session artifact directory
for audit and then restored in the worktree because the delta contains defects.

To avoid inferring from hashes, I used a session-local diagnostic probe under
`/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/safe-consumption-1398-audit/DiagnosticProbe`
against the actual N1 and T2 assemblies. The probe reproduces the conversion leg's
lossy C# -> Calor conversion, parses the generated Calor, binds it, and dumps sorted
`Code:Start:End:Message` identities for each changed row.

## Measured totals

| Metric | N1 baseline | T2 measured |
| --- | ---: | ---: |
| Source coverage records | 364 | 364 |
| Added/removed rows | 0 / 0 | 0 / 0 |
| Changed rows | 0 | 8 |
| Binder attempts | 35,179 | 35,179 |
| Source expressions | 88,158 | 88,158 |
| Exact expression source spans | 32,227 | 32,227 |
| Opaque source expressions | 3,597 | 3,597 |
| Native opaque boundaries | 76 | 76 |
| Serialized opaque boundaries | 93 | 93 |
| Binding errors | 4,922 | 4,924 |
| Propagated binding errors | 109 | 109 |
| Unmapped source expressions | 52,334 | 52,334 |
| Expressions with opaque descendants | 28 | 28 |

Conversion-leg aggregate totals stayed fixed:
`Incomplete=0`, `LegacySourceOrderAttempted=18005`,
`RoslynSelectedAttempted=35179`, `FilesSeen=364`, `ConvertedAndBound=364`,
`ConvertExceptions=0`, `EmptyOutput=0`, and `OutputParseFailures=0`.

Preserve-mode coverage stayed fixed:
`FilesSeen=364`, `OpaqueBoundaries=114`, `OpaqueExpressions=6866`,
`OpaqueUnmapped=0`, `OpaqueIdentityCount=114`, `UnconvertedFiles=0`,
`UnconvertedIdentityCount=0`, `ConvertExceptions=0`, `EmptyOutput=0`, and
`OutputParseFailures=0`.

## Row audit

Full machine-readable row details are in `corpus-delta.json`. The meaningful source
diagnostic deltas are:

| File | Count delta | Audit result |
| --- | ---: | --- |
| `FluentValidation/src/FluentValidation.Tests/NotEqualValidatorTests.cs` | 53 -> 53 | Message annotation changed from `Oblivious` to `Annotated` for the same `Calor0273` span on `§R (? (== _value null) null (str _value.Value))`; acceptable if isolated. |
| `FluentValidation/src/FluentValidation/Internal/RuleBase.cs` | 41 -> 40 | Removed one `Calor0273` on `_displayNameFactory?.Invoke(context) ?? _displayName ?? _propertyDisplayName`; expected coalesce/nullability improvement. |
| `FluentValidation/src/FluentValidation/Internal/RuleComponent.cs` | 18 -> 16 | Removed two `Calor0272` diagnostics on `_errorMessageFactory?.Invoke(...) ?? _errorMessage`; expected coalesce/nullability improvement. |
| `FluentValidation/src/FluentValidation/Resources/LanguageManager.cs` | 82 -> 81 | Removed one `Calor0273` on `return value ?? ""`; expected coalesce/nullability improvement. |
| `FluentValidation/src/FluentValidation/Validators/EmptyValidator.cs` | 4 -> 6 | Defect: added `Calor0200` undefined `s` and `e` for `value is string s && ...` and `value is IEnumerable e && ...`. |
| `FluentValidation/src/FluentValidation/Validators/NotEmptyValidator.cs` | 4 -> 6 | Defect: same supported `&&` pattern-variable scope regression as `EmptyValidator.cs`. |
| `serilog/src/Serilog/Core/Logger.cs` | 88 -> 89 | Defect: added `Calor0200` undefined `context` in the true branch after `value is string context`. |
| `serilog/src/Serilog/Events/EventProperty.cs` | 4 -> 5 | Defect: added `Calor0200` undefined `other` in `obj is EventProperty other && Equals(other)`. |

Minimal repro shape from pinned product source:

```csharp
if (value is string s && string.IsNullOrWhiteSpace(s)) { ... }
return obj is EventProperty other && Equals(other);
if (... && value is string context) {
    _overrideMap.GetEffectiveLevel(context, out minimumLevel, out levelSwitch);
}
```

T2-generated Calor for those supported patterns binds the variable in the pattern but
then reports the later use as undefined, for example:

```calor
§IF{if006} (&& (is value str s) (isblank s))
§R (&& (is obj EventProperty other) §C{Equals} §A other §/C)
§C{_overrideMap.GetEffectiveLevel} §A context §A{out} minimumLevel §A{out} levelSwitch §/C
```

## Blocker

There is no successful repeat-ratchet result to report because accepting the measured
`binder-source-coverage.json` would bless real pattern-scope binding regressions. The
worktree intentionally keeps the approved N1 coverage baseline, so the product ratchet
continues to fail until those pattern-variable diagnostics are fixed and remeasured.
