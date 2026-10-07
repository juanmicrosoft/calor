# 0.25 F5 (#1132) evidence: array expressions

**Status:** IN_PROGRESS (branch `milestone-0.25/f5-1132-array-expressions`). Not a READY record:
READY needs a candidate commit, per-case evidence carrying `requirementSha256`, the #1423/#1426
blocker references and the maintainer's approval (`docs/plans/v0.25-interop-scope-and-baseline.md` §10).
Reduced independence: Codex records only; not independently reviewed.

## What the R0 baseline showed, and what changed

R0 measured four residuals and one control. The "id/name repair" did not exist: no earlier commit
fixed it. Each residual below is repaired at its cause, not hidden by a rename.

| Residual (R0) | Cause | Repair |
|---|---|---|
| F5-ARRAY-01: `§ARR2D{arr2d004:arr2d005:Cell}` then `§R arr2d005`; CS0201, CS0103, CS1061 | Expression-position `§ARR2D` was a statement block bound to a name the C# emitter never declared; `§IDX2D g 0 1.V` parsed `.V` as part of the last index | `CalorEmitter` writes expression-position initializers inline (`§ARR2D{id:id:T} §ROW … §/ARR2D{id}`); `(§IDX2D g 0 1).V` / `(§IDX{a} i).V` group an element access (parser: a `(` before `§IDX`/`§IDX2D` is a grouping) |
| F5-ARRAY-02: `?:` with array branches does not parse (`Calor0106`) | The 2-D branch was appended as a block and returned `""` | Same inline form; `§IF{tern} c → … §EL → …` keeps the unselected branch unevaluated |
| F5-ARRAY-04: user local `_hoist000` rebound (`Calor0260`); parameters named like generated ids | Generated names were counter-only | `ConversionContext.IsReservedName` (identifier tokens with escapes decoded, plus identifier-shaped text, so interpolation holes count); `GenerateId` and `CalorEmitter.FreshTemp` skip reserved names |
| F5-ARRAY-05: jagged element type `i32` (CS0029); rank-3 rows wrong; `var c = Cube(); c.Rank` unknown effect | Roslyn `ElementType` drops later rank specifiers; the initializer reader assumed rank 2; array members did not resolve on `System.Array` | Element type keeps `[]` specifiers; rank 3+ (and written sizes) flatten to innermost `§ROW` vectors with the shape as sizes (`§ARR2D{id:id:i32:2:1:2}`), regrouped by the C# emitter; effect resolver maps array receivers to `System.Array` |

Evaluation-order repairs found while writing the witnesses (all silent before):

- `Use(S(1), new int[] { S(2) })` ran `S(2)` first: the converter hoisted array arguments
  (`HoistComplexArguments`, chain arguments, `HoistIfComplex`) into a temp bound before the call.
  Arrays now stay inline.
- `int[] a = { x, SetX(), x }` read the new `x` in slot 0: only `§`-bearing elements were hoisted.
  Now, if any element of an array must leave its line, every non-literal element is bound to a fresh
  temp in source order (`RenderArrayElements`).
- Object initializers as elements were hoisted ahead of the array (and preserved as `§CS` inside
  `?:`). `§NEW{T} A = x §/NEW` is written on the element's line instead, on every surface.
- Expression-position sizes (`new int[S(1)]`, `new int[S(5) + 1, S(6)]`) were hoisted ahead of the
  statement. They are now quoted embedded expressions, re-parsed in place (the existing `§ARR` form;
  `§ARR2D` sizes accept it too).
- `new int[,] { … }.Length` became `§NEW{}` (a silently wrong object creation): `§ARR2D` is no longer
  treated as a block-only collection.
- `§ARR2D` row elements were not effect-inferred at all, so a row could carry an undeclared effect.
  They are now charged. Object-initializer stores to a field of an in-module class were reported as
  an unknown setter (`Calor0411`/`Calor0410`); they are charged `mut` only.

Statement-position output (F5-ARRAY-03) is byte-identical to the R0 baseline.

## F5-ARRAY-06: declared boundary

| Shape | Expression position | Statement position |
|---|---|---|
| 1-D (`new T[] {…}`, `new[] {…}`, `T[] x = {…}`) | native | native |
| jagged (`T[][]`, `T[,][]`) | native | native |
| rectangular rank 2 with initializer (with or without written sizes, bare `{…}`) | native | native |
| rectangular rank 3+ with initializer | native (sized `§ARR2D` + `§ROW`) | native |
| sized, no initializer, literal/name sizes | native | native |
| sized, no initializer, computed sizes | native (quoted embedded size), except inside a `?:`/`&&`/`\|\|`/`??` operand when the size calls a method: preserved, `conditional-expression-hoisting` | native (sizes hoisted in order) |
| element is an object initializer | native, inline | native |
| element is an assignment, inside a conditional operand | preserved, `conditional-expression-hoisting` | — |
| element type: any type the converter maps (primitives, user classes, arrays, `object`, delegates) | native | native |
| `stackalloc` | unchanged by F5 | unchanged |

Anything the emitter still cannot write in place is never moved: the member is preserved by the
post-conversion rescue and reported as `post-validation-fallback` (F6 owns that label's wording).

## Files

| Path | Content |
|---|---|
| `reproduce/reproduce.py` | Driver. Imports the R0 driver and `ProbeRunner` unchanged |
| `results.json` | Per-surface results for F5-ARRAY-01..05 |
| `generated/` | Every `.calr` produced, per surface (`.calr.txt`, kept out of the corpus ledgers) |
| `reviews/` | Codex adversarial review records |

## Results (from `results.json`)

Measured at `453da318` (`src/` tree `82a3f174`), Release build, macOS arm64.

| Case | R0 baseline (CLI / MCP default) | Every surface now (`cli-default`, `cli-passthrough`, `cli-no-fallback`, `cli-migrate`, `mcp-default`, `mcp-passthroughOnError`, `mcp-passthroughOnError-moduleName`) |
|---|---|---|
| F5-ARRAY-01 | preserved-match / compile-error:Calor0410 | 0 `§CSHARP`, 0 `§CS{`, no loss, default compile passes, run matches (`2x2\|2\|3`) |
| F5-ARRAY-02 | preserved-match / preserved-match | same, run matches (`13\|2\|5\|67125`: the unselected branches' `Side(3)`, `Side(4)` never run) |
| F5-ARRAY-03 (control) | native-match / native-match | same, run matches; output identical to the R0 `mcp-default` text |
| F5-ARRAY-04 | preserved-match / compile-error:Calor0260 | same, run matches (`1\|2\|50\|boom\|123`: user `_hoist000` keeps 50, `Note(4)` never runs) |
| F5-ARRAY-05 | preserved-match / compile-error:Calor0410 | same, run matches (`3\|4\|3\|9\|b\|6`) |

35 of 35 cells are native and match. `--no-fallback` now succeeds on all five (nothing is
unsupported). No surface reports `post-validation-fallback`.

Library, CLI option, MCP and `ProjectMigrator` (with and without `PassthroughOnError`) surfaces,
plus two ordering witnesses (element order around a mutating call, object initializers as 2-D
elements, `?:` laziness, `&&`/`||` laziness over array operands, index order, an array argument
after another argument, rank 3, sized and bare initializers, a nested array, an element that throws
mid-row, field/property initializers, computed sizes in argument and return position) are covered by
`tests/Calor.Compiler.Tests/Migration/ArrayExpressionConversionTests.cs` (8 surfaces each).

## Not measured / residual

- `(§IDX{xs} 0).V` on a local declared by a sized statement array (`§B{[Cell]:xs} §ARR{Cell:xs:1}`)
  still reports an unknown getter effect (the local's type is not resolved by the effect pass);
  seen in a non-registered probe, not array-expression specific.
- An object initializer outside an array (`c = new Cell { V = 7 }`) on a class that has methods
  reported an unknown setter before this change; the field fix above also covers it.
- MCP `calor_migrate` and `calor_batch`, MSBuild and LSP surfaces: not run. macOS arm64 only.
