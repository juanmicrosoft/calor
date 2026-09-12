# Safe consumption #1398 final PRODUCT corpus diagnostic audit

**Outcome:** accepted. The c43 PRODUCT corpus measurement changes only the four already-audited nullability transfer rows, and the four prior 2d6 pattern-scope regressions are restored exactly to the approved N1 baseline. I updated `bench/phase0-agent-native/binder-source-coverage.json` from the c43 regeneration and preserved the rejected 2d6 audit as historical evidence.

## Scope and runtime

PR #1453's first exact-head reviews examined `1452bb4b`. The integration
review accepted; the adversarial review blocked on two old raw-Binder tests
that expected `??` to unwrap runtime Option payloads. Actual default compilation
already rejected those Option expressions before T2. The tests now require
non-resolution rather than fictional payload conversion, and a generated-runtime
control exercises explicit `Option.Unwrap()` on Some and None.

The internal transfer helper was colocated with the expression types in
`BoundNodes.cs`, following the existing compiler source layout and Calor-first
gate. Subsequent cleanup also applies the existing negative/disjunctive pattern
binding restriction to `VarPatternNode`, with a direct regression control.
Round two examined `5b60733e`: compatibility accepted, while integration
identified two explicit type-rule gaps on the API path that defers Roslyn
validation. Known non-exception call/new values can no longer be thrown just
because of their syntax, and known non-nullable value operands cannot use `??`.
These are type errors, not activation of nullable-state diagnostics. Unknown
nominal inheritance and external expressions still require generated C#
validation; this work does not claim a complete exception type checker.
Valid `throw null` behavior and nullable `var` pattern bindings have separate
runtime controls.

Overloaded, generic and shadowed native calls do not borrow the last registered
function's return type: they remain unmodeled in this checker until selected-call
information is available. An overloaded exception-factory runtime control pins
that boundary without changing N3's call resolution.

The pre-N2 candidate added 52 compiler cases (8,856 -> 8,908).
Approved N2 main `63220eab` was then merged into the issue branch without
rebasing. Its 101 compiler cases and all other evidence/configuration are
preserved. Two N2 tests had explicitly characterized nominal conditionals as
unmodeled; T2 now models these actual known branch identities, so the tests
assert the resulting non-null/nullable annotations instead. A runtime control
confirms the selected value, including null, through both inferred locals.
An actual C# typed-pattern conversion also exercises non-null call and return
consumers on string, null and unmatched inputs; no converter repair is needed.
The N2-integrated candidate inventory was 8,957 + 54 T2 cases = 9,011, with unchanged skips.
The original 42-case and 44-case candidates remain historical, not relabeled.
Both fresh reviews accepted `cf292161`, but its actual compiler/coverage CI
still exposed three cases. Primitive-constructor recognition is now scoped to
throw validation so ordinary constructor/member inference remains unchanged.
Two older parser/emitter and latent-division fixtures used non-nullable `int`
with `??`; they now use nullable `int`, preserving the original emission and
division-finding assertions without relaxing the new operand rule.
Round four examined `e7063060`: compatibility accepted with a duplicate-warning
finding, while integration blocked on a missing typed-pattern success scope in
`while` bodies. Both Binder and TypeChecker now transfer the condition's binding
into that body only, including conjunctions. TypeChecker reuses the already
inferred pattern binding type instead of resolving it again and duplicating
unknown-type warnings. Thirteen additional cases cover all three loop consumers,
null/unmatched inputs, invalid scope uses, and one warning per source occurrence.
That candidate's combined inventory was 8,957 + 67 T2 cases = 9,024.
The same e706 compiler CI also reached two more stale non-nullable-integer
coalesce fixtures, in operator suggestions and wrapper diagnostic reachability.
They now use nullable inputs without dropping their original assertions.
The ordinary product corpus ratchet still passes without another baseline update.
Following the same true-condition paths exposed the matching transfer gap in
statement and expression match guards. Both now introduce successful guard
bindings only in that case. Four more cases cover actual guard consumers,
round-trip execution, default/negative/disjunctive scope controls and diagnostic
reuse. The pre-N3 combined inventory was 8,957 + 71 T2 cases = 9,028, unchanged
skip budgets. That affected run passed 602 cases, including the ordinary
corpus ratchet, with no further baseline edits.
The statement-match runtime control uses the supported `var` carrier pattern.
A guarded wildcard statement case currently emits invalid C# (`case _ when`);
this transfer repair does not claim to repair that emission form.
Final-head review and CI records live on the PR. The source-specific corpus
measurements below remain pinned to their actual c43 and rejected 2d heads.

- Measured source: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398`, branch `fix/1398-safe-consumption`, HEAD `c43aefbb3d945eb63d39c6376537efb683b3683a`.
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

## Approved N3 integration

Both fresh non-author reviews accepted `95e579dc`, but approved N3 main
`8f9891a1` landed during that review. Normal merge `37195a7c` preserves its
selected maps, taint identities, 19-policy/33-route catalog, editor506 and
harness291. The original T2 and N3 measurements are not relabeled.

The first combined run had 964 passes and one corpus mismatch: N3's
`JsonValueFormatter` raw binding-error count55 becomes54. A fresh capture of
all54 identities, plus exactly the removed0274 identity, reconstructs the
approved N3 hash. This is the scalar statement argument
`value.ToString() ?? ""` at source line541: T2's non-null fallback now reaches
N3's shared argument validator. The other two N3 statement findings remain.
Regeneration compared all364 records and changed only that row's diagnostic
count/hash; the four original T2 rows and every other field were preserved.
Raw errors across the approved N3 baseline move4925 to4920, with unchanged
coverage and propagated counts. This is not a safety/activation claim.

[Exact integration audit](integration-8f9891a1-audit.json) pins the source,
runtime, actual removed diagnostic and reconstruction method. Four new joint
controls exercise reversed named arguments through both call forms with
fallback/throw and unsafe nullable inputs using default compilation, raw Binder,
CLI and runtime. The final inventory is 9,049 + 75 = 9,124, with unchanged
skips. The combined969-case selection and35 editor state cases pass, including
the ordinary corpus ratchet. New final-head reviews and CI remain required.

## Measured totals (original c43 audit)

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
