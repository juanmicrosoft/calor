# F2 #906 — Codex adversarial review, round 1

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9).
- Input: `git diff origin/main...HEAD` at `66a8db44` (first implementation commit).
- Focus: overload-selection changes, culture/format differences, effect laundering, dishonest
  feature claims, hole locations.
- **Outcome: incomplete.** The run hit the Codex usage limit before writing its verdict. The
  findings below are the ones it had confirmed with executed probes before the limit. Because the
  round produced no verdict, it is counted as a round (1 of 3) but not as acceptance.

## Findings and dispositions

| # | Finding (Codex, confirmed by probe) | Severity (assigned by implementer) | Disposition |
|---|---|---|---|
| R1-1 | `FormattableString.Invariant($"hello")`: a hole-free interpolated string becomes a plain Calor string, so `(cast global::System.FormattableString "hello")` emits `((FormattableString)"hello")`, which fails C# compilation (CS0030, surfaced as Calor1002). | BLOCKING | Fixed. A FormattableString/IFormattable target with no holes escalates at conversion (`string-interpolation-formattable-constant`); the enclosing statement or member is kept as C#. Witness: `HoleAndTargetShapes_KeepTheirCSharpMeaning` case 1. |
| R1-2 | Parser call lifting changes a string argument's meaning: native `"${String.Concat("${x}")}"` emitted a nested interpolation, where the raw C# path had kept `"${x}"` literal. | BLOCKING | Fixed. A string argument that parses with its own `${...}` holes is not lifted; the hole stays raw C# as before. Converter output escapes `\${`, so it still lifts. Witnesses: `NativeHole_StringArgumentWithDollarBrace_StaysLiteral`, shapes case 3. |
| R1-3 | Holes that cannot be lifted keep Calor syntax inside raw C# (the Calor emitter writes calls in C# form but their arguments in Calor prefix form), so they fail C# compilation or compute something else. | BLOCKING (pre-existing, but in scope: analyzed string behavior) | Fixed. The converter decides per hole: names, literals, dotted member access, operators, `nameof`, `typeof`, and dotted positional calls whose arguments are in that set convert natively; any other hole keeps its original C# text as `§CS{...}` and records an `InteropPreserved` loss (`string-interpolation-hole`). Multi-line holes or holes with comments escalate. Witness: shapes case 4 (indexing, `?.`, named and generic calls, `?:`, nested FormattableString). |
| R1-4 | A nested FormattableString target inside a hole emits `(cast ...)` as raw C#. | BLOCKING | Fixed by R1-3: a nested interpolated string is outside the native hole subset and stays C#. Witness: shapes case 4 (`{Kind($"{x}")}`). |
| R1-5 | Probe: `Log($"a{x}" + $"b{x}")` with `Log(Handler)` and `Log(object)`. The concatenation converts to the handler as one operand, but only each operand's converted type was checked. | BLOCKING (implementer follow-up of the probe; Codex's output was cut before its conclusion) | Fixed. The target type is read from the root of a `+` chain of interpolated strings. Witness: shapes case 2. |
| R1-6 | Probe: hole diagnostic on a later line reported at line 4, column 26 in Codex's run. | None | Matches the source (column 26 in its probe text); no defect reported. |

## Implementer additions in the same round

- .NET handler overloads (`StringBuilder.Append($"...")`, `string.Create(provider, $"...")`) are
  `[InterpolatedStringHandler]` targets too; they are preserved and pinned by
  `BclHandlerTargets_ArePreservedAndMatch`.
- `nameof` is never lifted to a call (it would be charged an unknown effect); pinned by
  `NameofHole_IsNotTreatedAsACall`.
- F-2 counts were regenerated after the fixes (see `rows.json`, `corpusCounts`). Opaque
  boundaries rise 129 -> 137 because holes that already reparsed as raw C# are now counted.
