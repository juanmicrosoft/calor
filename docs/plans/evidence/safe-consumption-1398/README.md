# Safe consumption #1398 final PRODUCT corpus diagnostic audit

**Outcome:** accepted. The c43 PRODUCT corpus measurement changes only the four already-audited nullability transfer rows, and the four prior 2d6 pattern-scope regressions are restored exactly to the approved N1 baseline. I updated `bench/phase0-agent-native/binder-source-coverage.json` from the c43 regeneration and preserved the rejected 2d6 audit as historical evidence.

## Scope and runtime

- Current source: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398`, branch `fix/1398-safe-consumption`, HEAD `c43aefbb3d945eb63d39c6376537efb683b3683a`.
- Approved N1 baseline source: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/t2-corpus-baseline-1398`, HEAD `3b513a632149290481161b195a71aa6cb0e251a8`.
- Base approved N1 main: `3b513a63`.
- Corpus gitlinks: FluentValidation `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c`, MediatR `fb309026775ef953a64fb5339d074426c1ad2c37`, serilog `0597ddfbd4ec594d9c42edd745fe728a2198bad9`.
- Runtime/profile: .NET SDK `10.0.400` on macOS Darwin 25.6.0 arm64.
- Package version remains `0.21.0`; no package/version files were edited.

## Commands and gate results

The requested solution-level command was run first:

```bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398
CALOR_UPDATE_BINDER_BASELINE=1 dotnet test --no-build \
  --filter "FullyQualifiedName~ConversionLeg_IncompleteCount_MatchesBaseline"
```

It regenerated `binder-source-coverage.json` and the matching `Calor.Compiler.Tests` test passed, but the solution-level runner exited `1` because unrelated test DLL arguments were rejected or had no matching tests. The clean compiler-project regeneration command passed:

```bash
CALOR_UPDATE_BINDER_BASELINE=1 dotnet test tests/Calor.Compiler.Tests/ \
  --no-build --filter "FullyQualifiedName~ConversionLeg_IncompleteCount_MatchesBaseline" \
  --logger "console;verbosity=minimal"
```

Result: `Passed: 1, Failed: 0, Skipped: 0`, duration 36 s.

The ordinary repeat ratchet passed after accepting the c43 source-coverage update:

```bash
dotnet test tests/Calor.Compiler.Tests/ --no-build \
  --filter "FullyQualifiedName~ConversionLeg_IncompleteCount_MatchesBaseline" \
  --logger "console;verbosity=minimal"
```

Result: `Passed: 1, Failed: 0, Skipped: 0`, duration 33 s.

The diagnostic probe was rerun against the actual N1 and c43 assemblies for all eight audited files. Outputs are session artifacts:

- `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/safe-consumption-1398-audit/baseline-diagnostics.c43-final-audit.json`
- `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/safe-consumption-1398-audit/current-diagnostics.c43-final-audit.json`
- `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/safe-consumption-1398-audit/binder-source-coverage.c43aefbb-current-measured.json`

## Measured totals

| Metric | N1 baseline | c43 measured |
| --- | ---: | ---: |
| Source coverage records | 364 | 364 |
| Added/removed rows | 0 / 0 | 0 / 0 |
| Changed rows | 0 | 4 |
| Binder attempts | 35,179 | 35,179 |
| Source expressions | 88,158 | 88,158 |
| Exact expression source spans | 32,227 | 32,227 |
| Opaque source expressions | 3,597 | 3,597 |
| Native opaque boundaries | 76 | 76 |
| Serialized opaque boundaries | 93 | 93 |
| Binding errors | 4,922 | 4,918 |
| Propagated binding errors | 109 | 109 |
| Unmapped source expressions | 52,334 | 52,334 |
| Expressions with opaque descendants | 28 | 28 |

Conversion-leg aggregate totals stayed fixed: `Incomplete=0`, `LegacySourceOrderAttempted=18005`, `RoslynSelectedAttempted=35179`, `FilesSeen=364`, `ConvertedAndBound=364`, `ConvertExceptions=0`, `EmptyOutput=0`, and `OutputParseFailures=0`.

Preserve-mode coverage stayed fixed: `FilesSeen=364`, `OpaqueBoundaries=114`, `OpaqueExpressions=6866`, `OpaqueUnmapped=0`, `OpaqueIdentityCount=114`, `UnconvertedFiles=0`, `UnconvertedIdentityCount=0`, `ConvertExceptions=0`, `EmptyOutput=0`, and `OutputParseFailures=0`.

## Final changed-row audit

Full machine-readable identities are in `corpus-delta.json`. The four final changed rows are the expected nullability transfers:

| File | Count delta | Audit result |
| --- | ---: | --- |
| `FluentValidation/src/FluentValidation.Tests/NotEqualValidatorTests.cs` | 53 -> 53 | Same `Calor0273` span on `§R (? (== _value null) null (str _value.Value))`; source annotation in the message changes from `Oblivious` to `Annotated`. |
| `FluentValidation/src/FluentValidation/Internal/RuleBase.cs` | 41 -> 40 | Removes one `Calor0273` on `_displayNameFactory?.Invoke(context) ?? _displayName ?? _propertyDisplayName`; accepted null-coalescing fallback transfer. |
| `FluentValidation/src/FluentValidation/Internal/RuleComponent.cs` | 18 -> 16 | Removes two `Calor0272` diagnostics on `_errorMessageFactory?.Invoke(...) ?? _errorMessage`; accepted null-coalescing fallback transfer. |
| `FluentValidation/src/FluentValidation/Resources/LanguageManager.cs` | 82 -> 81 | Removes one `Calor0273` on `return value ?? string.Empty`; accepted null-coalescing fallback transfer. |

These four rows have 194 baseline diagnostics and 190 current diagnostics: five identities removed, one identity added, net `-4` binding errors. The only code-count changes are `Calor0272: 3 -> 1` and `Calor0273: 68 -> 66` across the final changed rows.

## Restored rejected 2d6 rows

The four previously defective rows now match the approved N1 coverage and probe diagnostic identities exactly:

| File | Rejected 2d6 count | c43 count | Restored check |
| --- | ---: | ---: | --- |
| `FluentValidation/src/FluentValidation/Validators/EmptyValidator.cs` | 6 | 4 | `Calor0200` undefined `s` and `e` identities are absent; coverage hash matches N1. |
| `FluentValidation/src/FluentValidation/Validators/NotEmptyValidator.cs` | 6 | 4 | `Calor0200` undefined `s` and `e` identities are absent; coverage hash matches N1. |
| `serilog/src/Serilog/Core/Logger.cs` | 89 | 88 | `Calor0200` undefined `context` identity is absent; coverage hash matches N1. |
| `serilog/src/Serilog/Events/EventProperty.cs` | 5 | 4 | `Calor0200` undefined `other` identity is absent; coverage hash matches N1. |

The rejected 2d6 measurement is not relabeled as final c43 evidence. It is preserved separately in:

- `rejected-2d6e1530-README.md`
- `rejected-2d6e1530-corpus-delta.json`

## Limitations

This artifact is implementation evidence assistance, not the final independent review. The final accepted source-coverage update is scoped to the c43 measurement only; the earlier 2d6 blanket acceptance remains rejected historical evidence. No compiler, tests, source policy, manifests, scoping, release, website, changelog, commits, or pushes were changed.
