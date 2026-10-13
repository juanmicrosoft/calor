# 0.25 F5 (#1132) evidence: array expressions

**Status:** IN_PROGRESS on PR 2 (branch `milestone-0.25/f5-1132-narrowed`). Not a READY record:
READY needs a candidate commit, per-case evidence carrying `requirementSha256`, the blocker
references below and the maintainer's approval (`docs/plans/v0.25-interop-scope-and-baseline.md` §10).
Reduced independence: Codex records only; not independently reviewed.

F5 has two PRs. PR 1 (#1521, branch `milestone-0.25/f5-1132-array-expressions`) spent its review
budget and its extra verification pass returned REQUEST CHANGES; the maintainer closes it unmerged. PR 2 is the
narrowed re-scope and the PR that lands F5. The PR 1 sections below are kept as history; the
**PR 2 (narrowed)** section is current.

## PR 2 (narrowed)

### Decision

@juanmicrosoft, 2026-10-13, under §9 "close and re-scope" after `reviews/extra-verification-pass-codex.md`
(2 BLOCKING, 1 MAJOR, 2 MINOR): narrow F5 in PR 2.

- Drop the array-member effect certification entirely. Array instance members are unknown calls,
  exactly as on main.
- Keep the array-expression conversion. A shape that cannot convert correctly, including the
  MAJOR's multi-line rectangular element, is preserved as C# with a reported loss.

### What PR 2 removed, relative to #1521

| Removed | Where | Finding it closes |
|---|---|---|
| The `System.Array` instance-member table for array receivers (`ArrayInstanceMembers`, `IsArrayInstanceMember`) and the `System.Array.Initialize` guard | `Effects/EffectResolver.cs` (now byte-identical to main) | BLOCKING 1, BLOCKING 2, MINOR 2 |
| Array type inference for effect keys: a local bound from an in-module call returning an array, element types of `ArrayAccess`/`MultiDimArrayAccess`, types of array creations (`ElementTypeOf`) | `Effects/EffectEnforcementPass.cs` | (fed the certification) |
| The object-initializer shortcut that charged a field store on an in-module class as nothing | `Effects/EffectEnforcementPass.cs` | not a finding; not needed for conversion, so removed to keep effect changes to the one soundness fix |
| `ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure`, the member rows of `ArrayMembersAndRowElements_AreCharged` | tests | MINOR 1 (the masked witness) |
| The key-ledger delta attributed to element typing | `bench/phase0-agent-native/` | see "Key ledger" below |

The only effect-pass change left is a soundness fix: the elements of `§ARR2D` rows are charged
(`InferFromExpression` on `MultiDimArrayCreationNode` now unions the row elements). It adds effects;
it never removes one.

### What PR 2 kept

Everything Codex verified across #1521's rounds: in-place arrays (inline `§ARR2D`, `?:` branches,
`&&`/`||` operands, field/property initializers, member-access targets), element/size/argument
evaluation order, rank 3+ (sized `§ARR2D` + `§ROW`), jagged rank order (`new int[2][]`,
`new int[,][]`, declared `int[][,]`), collision-free generated names (source, escaped identifiers,
`#if`-active symbols, other project files), fresh empty arrays, and the `(§IDX2D g i j).Member`
grouping. The C# emitter, parser, `ConversionContext` and `CSharpToCalorConverter` changes are #1521's
unchanged.

### What PR 2 added

`RoslynSyntaxVisitor.ConvertArrayElement` asks the Calor emitter (`CalorEmitter.WritesInPlace`, a
probe emission of the converted element in the same in-place context) whether the element is
written in place. If it is not, the statement (or member, where the surface preserves members) is
preserved as C# and reported as `array-element-block` (`FeatureSupport`, `NotSupported`):

- **Rectangular arrays:** any element whose Calor is multi-line (an anonymous object; an object
  initializer holding one; a `switch` expression). The MAJOR witness `object[,] a = { { new { A = 1 } } };`
  is now preserved on all 8 surfaces (`BlockElements_ArePreservedWithAReport_AndRunLikeTheOriginal`).
- **Any array:** an element whose Calor is a statement block emitted ahead of the array (a
  collection initializer). This was silently wrong, and partly on main too: `object[] b = { new List<int> { 1 }, 2 };`
  converted to a one-element array on main and on #1521 (the `§LIST` block was emitted before the
  array and the element dropped). `return new object[,] { { new List<int> { 3 }, 4 } }.Length;` was
  rescued on main and became a `[1, 1]` array on #1521. Both are preserved now.

One-dimensional multi-line elements stay native (statement position re-indents the element; the
expression form parses), as #1521 verified. The probe also rejects an element that queues a
binding ahead of the array (Codex PR 2 round 1): a block lambda would be bound to an untyped temp
and lose its delegate type (`D[] { (int x) => { … } }`).

Two argument-order repairs in `HoistComplexArguments` (Codex PR 2 rounds 1 and 2). When a later
call argument must be hoisted, the earlier ones are bound first, in order (#1521). Now:

- a local or parameter read stays in place only when no argument after it, up to the last
  hoisted one, runs code (`Use(x, new int[] { Bump(ref x) }, new Cell())` read `x` after `Bump`);
- an earlier argument whose conversion to the parameter is not identity, numeric, reference,
  boxing or nullable is preserved (`conditional-expression-hoisting`) instead of bound: an
  untyped temp drops a method group's target type (CS8917) and runs a user-defined conversion
  after the hoisted argument.

And one declared-type repair in `CalorEmitter` (round 2): `object[] xs = new[] { "a" }` keeps
`xs` an `object[]` (`§B{any[]:xs}` with the array inline). The block form declared the created
type (`string[]`), which changed overload resolution and, for `new[] { task }`, the effect
receiver (`Task[]`, certified by a namespace default).

### Effect parity with main

`tests/Calor.Compiler.Tests/Migration/ArrayMemberEffectParityTests.cs` pins 23 results:
`GetLength`, `GetLongLength`, `GetLowerBound`, `GetUpperBound`, `Rank`, `LongLength`, `GetValue`
(scalar and `int[]` overloads), `SetValue`, `Clone`, `GetEnumerator`, `ToString`, `GetHashCode`,
`Equals`, `Initialize`, `CopyTo` on `i32[,]` and `i32[]`, a user extension
`GetLength(this int[], long)` called with a `long`, a second user extension, and `System.Array` /
`ValueTask[]` receivers. Every receiver typed as a Calor array (`i32[]`, `i32[,]`) gives
`Calor0411` (unknown external call) then `Calor0410` (effect `unknown` undeclared). Receivers
declared `System.Array` or `System.Threading.Tasks.ValueTask[]` give no diagnostic (rows 21-23),
which is main's behavior, pinned as such (see the residual below).

Proof of parity: the same file, unchanged, was run against `origin/main` at `043a2ed38` (an
exported tree with the same Z3 assets and an isolated HOME): 23 of 23 pass. A message-level dump of
every row's `Calor04xx` diagnostics was also diffed between that tree and this branch: identical.
`src/Calor.Compiler/Effects/EffectResolver.cs`, the effect manifests and the resolver key ledger's
scope are unchanged from main.

What the parity claim covers: for a given receiver type, every array member gets main's effect
result. It does not claim that converted code always has main's receiver types. Where main's
converter typed a local wrongly, F5 types it as C# does, and the resolver then answers for the
right type. Example (Codex PR 2 round 3, finding 1, contested): `var xs = new[] { t }` with `t` a
`Task` was an `object[]` on main (wrong: C# makes it a `Task[]`); it is now a `Task[]`, so
`xs.Initialize()` resolves through the `System.Threading.Tasks` namespace default, exactly as a
`Task[]` written in the source does on main. Explicitly declared types are kept
(`object[] xs = new[] { t }` stays `object[]`, round 2 finding 1).

MINOR 2 (real `System.Array` overloads report `Calor0410`) does not apply as a regression: on main
`g.GetValue(indices)` with `int[] indices` already reports `Calor0411`/`Calor0410` (row 18 of the
parity table), and so does every other array member.

Residual on main, unchanged here and outside F5's narrowed scope: a receiver declared as
`System.Array` or `System.Threading.Tasks.ValueTask[]` resolves through its manifest or namespace
default with no diagnostic, so `a.Initialize()` on such a receiver is not reported. The parity table
pins this as main's behavior (rows 21-23); it is not an array-receiver certification added by F5.

### Per-row results (PR 2)

`reproduce/reproduce_pr2.py` runs PR 1's driver unchanged on the same seven surfaces and writes
`pr2/results.json` and `pr2/generated/` (PR 1's `results.json` and `generated/` are kept unchanged). Each `.calr` is compiled with default options; when that
fails only on effect diagnostics it is also compiled with `--permissive-effects` (the R0 driver's
`compile_with_fallback`), and the generated C# is run against the original.

Measured at `17943b9a2` (`src/` tree `d98c3b9f`), after Codex PR 2 rounds 1-3 and the rebase onto `c020de748`; Release build, macOS arm64. The per-surface outcome is the same as at `241006e3a`.

| Case | Every surface (`cli-default`, `cli-passthrough`, `cli-no-fallback`, `cli-migrate`, `mcp-default`, `mcp-passthroughOnError`, `mcp-passthroughOnError-moduleName`) |
|---|---|
| F5-ARRAY-01 | native: 0 `§CSHARP`, 0 `§CS{`, no loss. Default compile: `Calor0410` (unknown effect: `GetLength` and the object-initializer field setter on `Cell`, as on main). `--permissive-effects` compile passes; run matches (`2x2\|2\|3`) |
| F5-ARRAY-02 | native, default compile passes, run matches (`13\|2\|5\|67125`) |
| F5-ARRAY-03 (control) | native, default compile passes, run matches; output unchanged from R0 |
| F5-ARRAY-04 | native, default compile passes, run matches (`1\|2\|50\|boom\|123`) |
| F5-ARRAY-05 | native, no loss. Default compile: `Calor0410` (unknown effect: `c.Rank`, as on main). Permissive compile passes; run matches (`3\|4\|3\|9\|b\|6`) |
| F5-ARRAY-06 | declared boundary, table below |

35 of 35 cells convert natively and run like the original. 21 of 35 compile with default options;
the other 14 (F5-ARRAY-01 and 05) fail only on `Calor0410` from array members and object-initializer
setters, which are unknown calls on main. Compared with the R0 baseline: F5-ARRAY-01 MCP default was
`compile-error:Calor0410` with permissive compile `Calor1002`; it now runs. F5-ARRAY-01/02/04/05 CLI
were `preserved-match` (rescued as `§CSHARP`); they are now native.

### F5-ARRAY-06: declared boundary (PR 2)

PR 1's table (below) holds, with these changes:

| Shape | Expression position | Statement position |
|---|---|---|
| rectangular, an element whose Calor is multi-line (anonymous object, `switch` expression, an object initializer holding a multi-line value) | preserved, `array-element-block` | preserved, `array-element-block` |
| any array, a collection-initializer element (`new List<int> { 1 }`) | preserved, `array-element-block` | preserved, `array-element-block` |
| one-dimensional, `switch` expression element | rescued, `post-validation-fallback` (unchanged from main) | rescued, same |
| array members on the converted code (`GetLength`, `Rank`, ...) | unknown call (`Calor0411`/`Calor0410`), as on main | same |

### Key ledger

`bench/phase0-agent-native/effect-resolver-key-ledger.json` moves 826 -> 825 string-origin keys
(tests 137 -> 136). The cause is the `13-11` conversion snapshot: `CreateEmpty` returned
`§C{Array.Empty<str>}` (one resolver key) and now returns a fresh `§ARR{str:arrStr005:0}`. #1521's
note attributed the delta to element typing; with element typing removed, the delta remains, so that
attribution was wrong. The ledger is regenerated at `17943b9a2` with a pending identity (phase 1 names that head and the PR), as #1521 did.

### Mapped 0.24 blockers (how the READY record must resolve them)

F5's blockers are #1426, #1423, #1496 (R-OBL) and #1503 (R-OBL-RESIDUALS). For the two mapped 0.24
repairs, §10 and `mappedBlockers` require, in `blockersResolved`:

- **#1496 R-OBL**, `mergeCommit` `ba2e31cb6373bcc10aeaa04e3f94e9067d0b8c06`: a `witnessRun` record
  under `docs/plans/evidence/` naming the READY candidate commit and the repair, listing
  `tests/Calor.Compiler.Tests/S2ObligationStateTests.cs`, and saying `passed`. F5's relevant change:
  element stores and calls that were opaque `§CSHARP` (rescued arrays) are now native.
- **#1503 R-OBL-RESIDUALS**, `mergeCommit` `9f5dfd763f90aebe16e4e4783f4c1703e464c8d3`: the same, with
  `tests/Calor.Compiler.Tests/S2ObligationResidualTests.cs`. F5's relevant change: native element
  initializers that may throw (F5-ARRAY-04's `Boom()` mid-row).
- **#1423** and **#1426**: a `reference` each.

Both witness files pass on this branch (they are in the `Calor.Compiler.Tests` run below), but that
is not a witness run on a READY candidate; it must be re-run and recorded on the candidate commit.
The READY record should also cite an F5-specific obligation witness (a function with an obligation
whose body has a native throwing element initializer or an element store); none exists yet.

### Tests (PR 2)

Fast lane, isolated HOME and CALOR_HOME, at `6002c19da` (the round-3 code; later commits change only docs, the manifest and the ledger):

| Project | Total | Failed | Skipped |
|---|---|---|---|
| Calor.Compiler.Tests | 13564 | 0 | 9 locally (corpus submodules not initialized; 3 in CI) |
| Calor.Conversion.Tests | 476 | 0 | 0 |
| Calor.Semantics.Tests | 55 | 0 | 0 |
| Calor.Enforcement.Tests | 695 | 0 | 1 |
| Calor.Verification.Tests | 473 | 0 | 0 |

### Line ceiling

Non-test source (`git diff --numstat origin/main...HEAD -- src`) at `6002c19da`: +592/-286, 878
changed lines, against #1521's 880 (+596/-284) and EXC-F5-NONTEST-LINES' allowance of 880. The exception is
recorded "for #1521 only"; whether it covers this PR, or this PR needs its own exception, is the
maintainer's call (it is not amended here).

### Reviews (PR 2)

Fresh budget: 3 Codex rounds plus 1 verification-only pass. Records: `reviews/pr2-*`.

| Invocation | Record | Verdict |
|---|---|---|
| 1 (round 1) | `reviews/pr2-round-1.md` | REQUEST CHANGES: 3 MAJOR, 1 MINOR; all fixed |
| 2 (round 2) | `reviews/pr2-round-2.md` | REQUEST CHANGES: 1 BLOCKING, 1 MAJOR, 1 MINOR; all fixed |
| 3 (round 3) | `reviews/pr2-round-3.md` | REQUEST CHANGES: 1 BLOCKING (contested, not changed), 3 MAJOR (fixed) |

---

# PR 1 (#1521) record, kept as history

### What the R0 baseline showed, and what changed

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

After Codex round 1 (REQUEST-CHANGES, `reviews/round-1.md`):

- Call/constructor arguments: when a later argument is hoisted (`§NEW`, collections, a ternary
  holding them), every earlier argument that is not a literal, lambda, `ref`/`out`/`in` argument, or
  local/parameter read is bound first, in order (`Use(new int[] { S(1) }, new Cell(S(2)))` ran
  `S(2)` first once arrays stopped being hoisted).
- Array elements are converted as conditional operands (`ConvertInPlace`): an assignment used as an
  element, or an index containing an assignment, is preserved as C# (`conditional-expression-hoisting`)
  instead of being queued ahead of the statement AND kept as the value (evaluated twice); `x++` stays
  native (`post-inc`); a decomposed chain is captured in place.
- Statement-position sizes are quoted in place like expression-position ones (`new int[n, Next()]`
  read `n` after `Next()`); `§NEW` arguments hoist all-or-nothing in order (`new Cell(x, Next())`).
- Written sizes are kept when an empty level hides them (`new int[0, 3] {}` became `[0, 0]`).
- Generated-name reservation walks the parsed tree (escaped identifiers in interpolation holes) and
  the project's other files (`AdditionalSemanticSyntaxTrees`, cached per tree; only digit-final names
  can collide with a generated name).
- `System.Array` instance members `Clone`, `GetEnumerator`, `ToString` are `alloc` (the type's pure
  default would have certified them once array receivers resolved to it).
- `new[] { F(), G() }` uses Roslyn's element type (was `object[]`); `new int[2][]` / `new int[,][]`
  emit the created rank before the element's ranks (C# emitter and binding types); `new T[0]` and
  `{ }` stay fresh arrays (were the shared `Array.Empty<T>()`).

After Codex round 2 (REQUEST-CHANGES, `reviews/round-2.md`):

- Expression-position sized `§ARR2D` ends with `§/ARR2D{id}` (a sized 2-D element took its parent's
  next `§ROW` as an initializer: `new object[,] { { new int[1, 1] }, { 42 } }`).
- Sizes convert in place like elements (`new int[n, n++]` read `n` after the increment;
  `new int[x = Bump()]` evaluated `Bump()` twice and is now preserved).
- The tree actually converted (its preprocessor symbols) also reserves names.
- `new[]` uses Roslyn's created element type before the receiving declaration's.
- `null` and `default` arguments stay in place before a hoisted argument (an untyped temp lost
  their target type).
- `GetValue` is `alloc`; `Array.Initialize` is an unknown call (element constructors).

After Codex round 3 (REQUEST-CHANGES, `reviews/round-3.md`, the last review round):

- Array receivers answer only `System.Array`'s own instance members, from a table in
  `EffectResolver` (`SetValue` `mut`+`alloc`); anything else (extensions, `Initialize`) falls
  through to extension resolution and unknown-call charging. The manifest is unchanged from main.
- Statement-position array elements are no longer hoisted for carrying a call: an element keeps its
  implicit conversion and its target type (method groups) on its own line.
- Multi-dimensional indices convert in place (`g[n, n++]`).
- A `ref`/`out`/`in` argument whose address evaluates a call, before a hoisted argument, preserves
  the member (`conditional-expression-hoisting`).
- `new T[][,]` binds as `T[][,]` (parser), and a declared `int[][,]` keeps its element rank.
- An empty level leaves no elements: the creation is its sizes (constant expressions included).
- `new[,] { … }` converts as a rectangular array (created type from Roslyn; preserved if unknown).

After the Codex verification pass (REQUEST-CHANGES, `reviews/verification.md`; the review budget
is now spent, so these last changes are **not** Codex-reviewed):

- Array elements are never hoisted, in statement position too (a hoisted constructor argument ran
  ahead of an earlier element's implicit conversion); a multi-line statement element is re-indented
  with its line. The round-1 "hoist every non-literal element" rule now applies only to call and
  constructor arguments.
- The array-member table also matches argument count and integer index types, so an extension named
  like an array member stays unknown; `System.Array.Initialize` is unknown again.

Statement-position output (F5-ARRAY-03) is byte-identical to the R0 baseline.

### F5-ARRAY-06: declared boundary

| Shape | Expression position | Statement position |
|---|---|---|
| 1-D (`new T[] {…}`, `new[] {…}`, `T[] x = {…}`) | native | native |
| jagged (`T[][]`, `T[,][]`, `T[][,]`) | native | native |
| implicitly typed rectangular (`new[,] { … }`) | native | native |
| rectangular rank 2 with initializer (with or without written sizes, bare `{…}`) | native | native |
| rectangular rank 3+ with initializer | native (sized `§ARR2D` + `§ROW`) | native |
| sized, no initializer, literal/name sizes | native | native |
| sized, no initializer, computed sizes | native (quoted embedded size, converted in place); preserved (`conditional-expression-hoisting`) when the array is itself a `?:`/`&&`/`\|\|`/`??` operand and a size is not a name or integer literal | native (quoted in place) |
| size is an assignment | preserved, `conditional-expression-hoisting` | preserved, same |
| element is an object initializer | native, inline | native |
| element (or index) is an assignment | preserved, `conditional-expression-hoisting` | preserved, same |
| element is `x++` / `x--` | native | native |
| empty (`new T[0]`, `{ }`) | native, fresh array | native, fresh array |
| element type: any type the converter maps (primitives, user classes, arrays, `object`, delegates) | native | native |
| `stackalloc` | unchanged by F5 | unchanged |

Anything the emitter still cannot write in place is never moved: the member is preserved by the
post-conversion rescue and reported as `post-validation-fallback` (F6 owns that label's wording).

### Files

| Path | Content |
|---|---|
| `reproduce/reproduce.py` | Driver. Imports the R0 driver and `ProbeRunner` unchanged |
| `results.json` | Per-surface results for F5-ARRAY-01..05 |
| `generated/` | Every `.calr` produced, per surface (`.calr.txt`, kept out of the corpus ledgers) |
| `reviews/` | Codex adversarial review records |

### Results (from `results.json`)

Measured at `0e27e043` (`src/` tree `c631e8b6`), Release build, macOS arm64.

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

### Not measured / residual

- `(§IDX{xs} 0).V` on a local declared by a sized statement array (`§B{[Cell]:xs} §ARR{Cell:xs:1}`)
  still reports an unknown getter effect (the local's type is not resolved by the effect pass);
  seen in a non-registered probe, not array-expression specific.
- An object initializer outside an array (`c = new Cell { V = 7 }`) on a class that has methods
  reported an unknown setter before this change; the field fix above also covers it.
- Not array-specific, unchanged here: method-chain decomposition (`a.F(x).G(new T())`) still hoists
  chain arguments; an assignment used as a call argument (`F(x = S())`) is still queued and kept as
  the value; a local read as an earlier argument stays in place when a later argument is hoisted (only
  a closure invoked by that argument could change it). `System.Array` static `Copy` is listed pure
  (pre-existing manifest entry).
- `int[] r = c ? new int[3] : null;` fails conversion loudly (`Calor0272`, nullability of the
  declared binding); seen on a non-registered probe, not attributable to this change.
- MCP `calor_migrate` and `calor_batch`, MSBuild and LSP surfaces: not run. macOS arm64 only.
