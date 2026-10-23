## [0.25.0] - 2026-10-19

Calor 0.25 is an interoperability release. It changes how the C# → Calor converter
(`calor convert`, `calor migrate`, the MCP tools and the `CSharpToCalorConverter` library)
handles six groups of everyday C#: `ref`/`out`/`in` arguments, interpolated strings, local
functions, iterator properties, array expressions, and the reports of what stays C#. The
language gains native local functions and two new errors (`Calor0211`, `Calor0212`). The
website gains a guide to converting C#, and CI now compiles its examples.

**These notes were frozen with the release candidate on 2026-10-19, before the 0.25 acceptance
review (A1, #1430).** A release can publish only after that review accepts the candidate, and
the notes cannot change after the freeze. At the freeze:

- **Nothing was released.** There was no v0.25.0 tag, NuGet package or GitHub release. The
  newest release on NuGet was 0.24.0.
- **No family was ready.** The 0.25 scope packet (the frozen cases each of the six converter
  families must pass) recorded 0 of 6 as ready. A family is ready only when its cases pass on the
  frozen candidate and the maintainer approves. The results below were measured before the freeze.
- **0.24's checks had to pass again.** The 0.24 evidence review (#1408) ended `MILESTONE-FAILED`
  (see 0.24.0), so this candidate must re-pass 0.24's proof, provenance and release checks on its
  own commit. No 0.24 approval or record is reused.
- **The three release-process gaps from 0.24 were closed.** #1545, #1546 and #1547 closed on
  2026-10-19, before the freeze (see "Release process" below). Acceptance requires them closed.
- **Independence is reduced.** OpenAI Codex reviewed each change, and the maintainer approved
  and merged it. No reviewer independent of the two took part, so every 0.25 evidence claim is
  at most *bounded*.
- **Known open defects.** A private base-class member named `@default` makes a converted
  `out var` fail to compile with `CS0122` (#1573); the failure is loud, not silent. Reading a
  field of a struct array element still needs `--permissive-effects`.

This release publishes no new benchmark results.

### Added

- **Native local functions (#847).** A `§F` written inside a `§F` or `§MT` body is a local
  function. Calor emits it in place as a C# `static` local function. Its name is visible in
  the whole body, before the declaration too, and a bare call such as `§C{Add}` names it
  even when the class has a method `Add`. It cannot capture the enclosing locals,
  parameters or `this`; a capture is a compile error (`CS8421`). Its body's effects are
  charged to the enclosing callable. Other placements and shapes (nested in a block,
  `§E`, contracts, type parameters, refinement types, `§PROOF`, `§YIELD`) are rejected
  with the new diagnostic `Calor0211`. So is a use as a value in a position whose effect
  row Calor does not check, such as inside `??`, an array or a qualified store. See
  "Local Functions" on the website's structure-tags syntax reference page.

### `ref`, `out` and `in` arguments are checked before C# generation (#943)

Calor already had `§A{ref} x`, `§A{out} x` and `§A{in} x`. This release follows the
contract in `docs/plans/v0.25-refout-contract.md` (#1427).

- **New error `Calor0212` (InvalidByReferenceOperand).** Passing something C# cannot pass by
  reference is now a Calor error: a literal, a computed value such as `(+ v 1)`, a call
  result, a property, an indexer element, `this`, a readonly field outside its constructor,
  or an `in` parameter passed as `ref` or `out`. Before, these failed only when the generated
  C# was compiled (`Calor1002`).
- **More valid operands compile.** An array element (`§A{ref} §IDX{arr} 1`) and a field
  through a receiver (`§A{ref} k.n`) now compile in module functions too. `§A{out} _` is a
  discard in module functions. It is rejected with `Calor0212` when a variable named `_` is
  in scope, because C# would write to that variable.
- **Writes through `ref`/`out` are charged as effects.** Passing an array element, a field
  through a receiver, or a dotted path as `ref` or `out` now charges `mut`, the same as
  `§ASSIGN` to it. Before, a function declared `§E{alloc}` could write an array element
  through `ref` without declaring `mut`. That was an effect laundering bug: a write the effect
  checker did not see. Code with this bug now fails with `Calor0410`; add `mut` to `§E{...}`.
- **Null checks on parameters that may share storage end earlier.** In a function with two or
  more by-reference parameters, writing one ends a null check on the others. With one or more
  by-reference parameters, any call, construction, or field, element or property read ends it
  too. Code that relied on the old behavior now reports `Calor0272`.
- **`calor convert` keeps by-reference identity.** `Bump(ref pts[0].X)` on an array of
  structs now passes the element's field itself, so the callee's write is kept.
  C# `out var n` becomes a mutable binding declared just before the statement, when it
  appears in an expression statement such as `int.TryParse(s, out var n);`, a local
  declaration, a `return`, or an `if` condition directly in a block, its type is known, and
  its name clashes with nothing in scope. The binding starts at `default`, so the next line is
  never read as its value. Other forms, including chained assignments and code with a member
  named `default`, keep their statement or member as C#. Before, an `out var` inside `?:` became
  inline `§CS{...}`, and one in a `while` header was declared outside the loop.
- **Shapes without a native form stay C#.** These are ref locals and ref returns (block or
  `=>` bodied), conditional refs, `scoped` and `ref readonly` parameters, `this ref`/`this in`
  extension calls, by-reference delegates and lambdas, by-reference constructor and indexer
  arguments, calls that omit `in`, and method-generic `out T` parameters. A flow attribute
  such as `[NotNullWhen]` on an `out` parameter is copied to the native declaration but has
  no effect on Calor's null checks. A C# `ref` argument to an `in` parameter now converts to
  `§A{in}`.
- **`calor feature-check` changes.** `out-var` is now `Partial`, because only the forms above
  are lowered. The `ref-parameter` and `out-parameter` entries no longer suggest tuples or
  `Result<T, E>`, and `in-parameter` names the call sites that stay C#. `ref-expression` and
  `multidim-array` no longer claim that every form converts.
- **Effect on converted real-world code.** Over the three vendored corpora (MediatR, Serilog,
  FluentValidation), 33 more members and types are kept as C#. In 31, an `out var` cannot be
  lowered: the one-file conversion cannot resolve its type, it sits in a lambda, or part of its
  statement already stays C#. The other 2 declare a method-generic `out T` parameter. Before,
  most of the first group became an untyped binding that failed with `Calor0250`. The native
  source expressions over the corpora drop from 35,177 to 34,371 (2.3%), and 10 more of 364
  modules now get through binding to effect checking.

These are checked by tests that compile every case in the contract, and by convert, compile
and run comparisons on the CLI, `calor migrate` and MCP conversion paths. All 57 Calor cases
in the contract now hold. One registered witness (`D1-LV-03`) had declared a property named
`i32` by mistake, so it could not show the property refusal; a contract amendment corrected it,
and it now reports `Calor0212`. One case stays open: a struct array element's field read still
needs `--permissive-effects`. The 0.25 scope packet records this family as in progress, not ready.

### Interpolated strings keep their C# target type (#906)

- **Overload selection is kept.** In C#, `Kind($"k={x}")` can call `Kind(FormattableString)`
  instead of `Kind(object)`, depending on the interpolated string's target type. `calor convert`
  now wraps `FormattableString` and `IFormattable` targets in an explicit cast, so the converted
  program calls the same overload. Before, a method whose only overload took a
  `FormattableString` made the converted Calor fail to compile, and the CLI fell back to keeping
  the whole class as C#.
- **Custom interpolated-string handlers are kept as C# at conversion time.** A call such as
  `Log($"a{x}b")`, where `Log` takes an `[InterpolatedStringHandler]` type, keeps its enclosing
  statement or member as C# interop on every surface. Before, MCP `calor_convert` produced a
  Calor string argument that the compiler then rejected (`Calor0208`).
- **Edge targets are kept as C# instead of miscompiled.** `FormattableString.Invariant($"text")`
  (a `FormattableString` target with no holes) and `Log($"a{x}" + $"b{y}")` (a concatenation
  passed to a handler) keep their enclosing statement or member as C# interop.
- **Holes outside the native subset are marked as C#.** Holes made of names, string, character,
  `bool` and `int` literals, `double` literals with a fractional part, dotted member access,
  operators, `nameof`, `typeof`, and calls with a dotted target and positional arguments convert
  natively. A digit-only hole such as `{1}` is written `${INT:1}`, because `${1}` reads back as
  literal text. Other holes, such as `{items[i]}`, `{x?.Name}` or
  `{obj.GetType().Name}`, now keep their original C# text as `§CS{...}` inside the hole and are
  counted as interop. Before, the converter mixed C# and Calor syntax in such holes, which could
  produce C# that did not compile.
- **Hole evaluation order is kept.** A hole containing a string literal, such as
  `$"q={Tag("t")}"`, is no longer moved into a temporary ahead of the statement. That move ran
  `Tag` before earlier arguments and even when an enclosing `?:` skipped the string.
- **Holes are analyzed.** A call written in C# form inside a hole, such as
  `"${Console.ReadLine()}"`, is now an ordinary call, so effect checking sees it. A pure (`§E{}`)
  function containing it now fails with `Calor0410` (undeclared effect). Before, it compiled with
  only the `Calor0419` warning that its effects were assumed. Holes in other forms still get
  that warning. This can turn existing code that compiled with a warning into an error; declare
  the effect to fix it.
- **Diagnostics point at the hole.** An error inside a hole, such as an undefined name, is now
  reported at the hole's line and column instead of line 1, column 1.
- **`string-interpolation` is now `Partial`** in `calor feature-check`, because handler targets
  are not converted natively. New entries: `string-interpolation-to-string` (`Full`),
  `string-interpolation-formattable` and `string-interpolation-hole` (`Partial`), and
  `string-interpolation-handler` and `string-interpolation-formattable-constant`
  (`NotSupported`).

These are checked by convert, compile and run tests that compare the converted program's output
with the original C# on the CLI and MCP conversion settings, including `de-DE` and `fr-FR`
formatting. The 0.25 scope packet records this family as in progress, not ready.

### Conversion reports name the path that kept each member as C# (#1144)

When the C# → Calor converter cannot produce working Calor for a member, it keeps the
original C# in a `§CSHARP` block. Reports now say which mechanism did that, on every
conversion surface: `calor convert`, `calor migrate`, the MCP tools `calor_convert`,
`calor_migrate` and `calor_batch`, and the `CSharpToCalorConverter` library.

- **Fixed: a wrong label.** `calor convert` printed "(N via --passthrough fallback)" for every
  rescued member, even when you had not passed `--passthrough`. The rescue is on by default
  on the command line. The line now reads, for example,
  `Preservation paths: 2 by automatic rescue (round-trip-failure: 2; no passthrough request needed)`.
- **New: path and trigger fields.** Each recorded loss now has `path` (`interop`, `rescue`,
  `passthrough`, `lossy` or `dropped`). Rescues and passthroughs also have `trigger`
  (`parse-failure`, `round-trip-failure` or `unsupported-construct`) and `enabledBy` (the
  default or option that allowed it). A new per-file `conversionPaths` object (schema
  version 1) gives the outcome (`native`, `preserved`, `lossy` or `refused`), a count per
  path, the triggers, and whether rescue and passthrough were on. All fields are additive;
  existing fields and counts are unchanged.
- **Fixed: a custom module name disabled the rescue.** With MCP `calor_convert`, a
  `moduleName` (or an `inputPath`, which derives one) stopped the rescue from finding a
  global-namespace type's original C#, so the call failed. `calor_batch` with
  `moduleNameOverride` had the same problem. These now rescue like other calls.
- **Fixed: `calor_convert` with `mode: "roundtrip"` ignored `passthroughOnError`.** It now
  honours it, like the `convert` and `validate` modes, and its result gains a `lossSummary`.
- **Changed: a `calor_batch` abort is now JSON.** With `skipOnError: false`, the abort used to
  return only an error sentence. It now returns JSON with that sentence in `error`, plus
  `preservationPaths` and each file's `conversionPaths`; `isError` is still `true`.
- **Unchanged, now documented: defaults differ by surface.** `calor convert` rescues members
  that fail the C# round trip by default; `calor_convert` and the library do not, and say
  so in their reports (`rescueUnusableMembers: false`). `calor migrate`, `calor_migrate` and
  `calor_batch` rescue only members whose Calor does not parse. `calor_batch` does so even
  with `passthroughOnError`; the other two have no passthrough option. No default changed
  in this release.

### Changed

- **C# local functions convert natively when they are non-capturing (#847).** Before,
  every member with a local function was preserved as a `§CSHARP` block, and its callers
  needed `--permissive-effects` to compile. Now a local function declared directly in a
  method body, without capture, attributes, type parameters, `async`/`unsafe`/`extern`,
  `yield`, nested local functions or `ref`/`out`/`in`/`params`/default parameters,
  becomes a nested `§F` in the same method. The three registered cases (F3-LOCAL-01 to 03)
  convert natively on all 8 measured surfaces, compile with default options and print
  the same result as the original. Capturing, generic, async and iterator local
  functions are still preserved as `§CSHARP` and reported as `local-function`. The
  `local-function` feature is now `Partial` instead of `NotSupported`.

### Fixed

- **`--format json --verbose` now keeps stdout a single JSON document.** Three commands
  printed `--verbose` progress text to stdout before the JSON envelope, so a program that
  parsed stdout failed. `calor convert` on a C# file printed `Converted N nodes` and member
  counts; `calor verify` printed compiler phase lines such as `Lexer produced N tokens`;
  and `calor benchmark <directory>` printed `Benchmarked: <name>`. In JSON mode this text
  now goes to stderr, as the rest of the human-readable report already did. The JSON
  content and text-mode output do not change. Tests pin the same contract for every
  command that has both `--format` and `--verbose`: the root compile command, `convert`,
  `format`, `lint`, `verify`, `ids check`, `assess`, `analyze-convertibility`,
  `benchmark` and `watch` (one JSON document per stdout line). (`calor migrate` has no `--format` option; its JSON report goes to a file.)
- **`calor assess` and `calor coverage` no longer say `ref` and `out` parameters are
  unsupported.** Since #943 they convert natively, but the migration analyzer behind these
  commands (and MCP's analysis tools) still described them as "not yet supported", and
  `out var` the same way. The descriptions now say what converts and which shapes stay C#
  (for example ref returns, method-generic `ref T`, and an `out var` inside a lambda). The
  constructs are still listed, with the same names and counts, because those shapes keep
  part of a file as C#. Scores do not change.
- **Expression-bodied compound assignments keep their operator.** `void M() => Log += s;`
  was converted to `Log = s` with no reported loss. It now converts to a compound
  assignment (`§ASSIGN` is no longer used for `+=`, `-=`, and so on in this position).
- **A local or parameter now shadows a same-named function in calls.** In
  `§C{Add} §A 1 §/C`, if the innermost declaration named `Add` is a local or a parameter
  (for example a `Func<i32, i32>`), the binder now treats the call as a delegate
  invocation, as C# does. It used to resolve `Add` to a same-named class method or
  module function, so overload errors and effect charges came from the wrong callee.
- **C# → Calor conversion turned whole-number `double` literals into integers (#1528).**
  `double r = n / 2.0;` converted to `(/ n 2)`, which is integer division when `n` is an `int`.
  The converter now writes `2.0`, so the division stays floating-point. The same fix covers
  attribute arguments (`[Kind(2.0)]` no longer picks an `int` overload) and keeps
  `default(float)` and `default(decimal)` at their own width (`SINGLE:0`, `DEC:0`).
- **C# iterator properties and indexers no longer convert to illegal Calor (#1139).** A getter
  that uses `yield return` or `yield break` is a C# *iterator accessor*. Calor has none:
  `§YIELD` inside a `§GET` is error `Calor0209`. The converter used to emit exactly that. Only
  the CLI's post-conversion rescue hid it; the MCP `calor_convert` tool and the library API
  returned Calor that failed to compile. Now the converter keeps the whole member as a
  `§CSHARP` block and reports an `iterator-accessor` loss. The output is the same on
  `calor convert`, `calor migrate`, MCP `calor_convert` and the library, with or without
  passthrough, and it compiles with default options. Laziness, `finally` on dispose, and
  exception timing match the original in 2 of 2 registered fixtures on 6 surfaces (3 CLI, 3
  MCP). With `--no-fallback` the conversion fails and names the feature. Iterator methods
  still convert natively to `§YIELD`. See "Iterator Properties and Indexers" on the
  `calor convert` docs page.
- **C# 13 partial properties and indexers are preserved instead of breaking the build.** Calor
  has no partial members, so a defining declaration such as `public partial int Q { get; }`
  used to become a second, auto-implemented property (C# error `CS0102`). The converter now
  keeps every `partial` property or indexer declaration as `§CSHARP` and reports a
  `partial-property` loss, or `iterator-accessor` when any part in the same file yields. Parts
  split across files are each preserved, but only parts in the same file share the
  `iterator-accessor` label.

- **Arithmetic on named refinement types (#1515).** A named refinement type is declared with
  `§RTYPE`, for example `§RTYPE{r1:NatInt:i32} (>= # INT:0)`. Before this fix, the type checker
  rejected arithmetic on such a value with `Calor0202`, because it did not unwrap `NatInt` to its
  base type `i32`. Inline refinements (`§I{i32:x} | (>= # INT:0)`) already worked. Now operators
  see a named refinement as its base type: `+ - * / %`, unary minus, comparisons, bitwise and
  shift operators, logical operators on a `bool` refinement, and loop bounds. The result has the
  **base** type, not the refinement, because `NatInt - NatInt` can be negative. The exception is
  increment and decrement (`inc`, `dec`, `post-inc`, `post-dec`) of a variable: they keep the
  refined type, because the write back into the variable is itself checked. Incrementing a
  collection element or field gives the base type. Writing a result
  back into a refined variable or return is still checked: you get the same verification
  obligation and runtime guard as with an inline refinement. Binding a plain base value with
  `§B{x:NatInt}` is still rejected by the type checker, as before.
- **`decimal` arithmetic result type.** The type checker typed `decimal + decimal` as `i32`. It
  now types it as `decimal`, as C# does. So `§B{x:i32} (+ a b)` with `decimal` operands is now a
  type error (`Calor0202`) instead of a failure in the generated C#. A nested mix such as
  `(+ (+ a b) f)` with an `f64` operand is now rejected like the direct `decimal + f64` case.
  When the other operand is a type the checker does not model, such as a class with its own `+`
  operator, the checker still leaves the result to the C# compiler.

- **A `§PROOF` can now refer to a `§B` local** (#1516). `§PROOF` states a fact the compiler
  tries to prove; when it succeeds, the obligation is *discharged* and the runtime check is
  removed. Before this fix, any `§PROOF` that named a local, such as
  `§B{newBalance:i32} (- balance amount)` then `§PROOF{p1} (>= newBalance INT:0)`, was reported
  `Unsupported` with "Unknown variable", even when it was easy to prove. The solver now uses the
  local's defining expression, but only when that is exactly the local's value at the proof:
  - the local is immutable (`§B`, not `§B{~...}`), bound once, and never reassigned or passed by `ref`;
  - its binding runs on every path to the proof (it comes earlier in the same block, or in a block
    that encloses the proof);
  - its definition uses only literals, operators, parameters the function never changes, and
    other locals that meet these rules.

  The solver also refuses a local whose C# name collides with another variable (`a-b` and `ab`
  both become `ab`). It refuses all locals in a function when a refinement predicate (a
  `§RTYPE` or parameter refinement) does more than compare names and literals, or when the
  module declares operator overloads. In these cases, code the solver cannot see could change
  the local.

  Any other local still makes the proof `Unsupported`, and the runtime check stays. The message
  now says which local and why, for example "local 'm' is mutable". If the proof needs the
  local's definition to finish without integer overflow, it is reported `Assumed`, not
  discharged, and the runtime check stays.

### A task-first guide to converting C# (#1429)

A new website guide, "Converting C#: What Stays C# and Why",
walks through the C# features the 0.25 converter changes: `ref`/`out`/`in` calls such as
`TryParse` and `TryGetValue`, interpolated-string target types, local functions, arrays in
expressions, and iterator properties. For each one it shows real converter output, what stays C#,
and what the report says. Each converted block on the page is checked in CI against the
converter's actual output, and each complete program among them is also compiled. The `convert`, `migrate`, MCP, `feature-check`, `self-check` and
structured-output pages now state each surface's defaults (`calor convert` rescues round-trip
failures by default; MCP `calor_convert` needs `passthroughOnError: true`; project migration
never rescues them), how to read native and interop counts, and what a partial or refused
conversion returns.

Corrections found while checking the pages against the converter:

- Four `#943` defects, fixed in the same release (see the `#943` entry): `out var` in a call
  statement failed the C# round trip, an expression-bodied ref return lost its `ref`, four
  `calor feature-check` descriptions did not match the converter, and `ref a[Next()]` was
  reported as `in-parameter`. `yield-return` (methods only) is still a listed gap.
- `docs/cli/feature-check.md` no longer lists `string-interpolation` as fully supported, and its
  example now shows current output.

### Website examples are now checked in CI (#1143)

`calor self-check docs` now reads `website/content/**/*.mdx`, not only `docs/`. It already runs
on every pull request, so a wrong website example now fails CI.

- **Complete programs compile.** A `calor` code block whose first line starts with `§M` is
  compiled the way `calor --input file.calr` compiles it, including the check of the generated
  C#. It must report no errors. 49 programs on 96 pages are checked.
- **Failing examples say how they fail.** An example meant to fail is labelled with its
  diagnostic codes, for example `expect=Calor0272`. The check fails if the compiler reports any
  other error or warning code, or if the text around the example does not name the code.
  Multi-file examples are compiled together with `group=`.
- **Quoted compiler output is real or labelled.** A block marked `output` must match the
  diagnostics the example above it produces. Output that is not checked, such as `calor query`
  results and MCP responses, is marked `illustrative`, and the site now shows it as "Example
  output (not checked)". A block that looks like tool output and has neither label fails.
- **Diagnostic codes, keywords and versions.** Website pages now get the same checks as
  `docs/`: every cited `CalorNNNN` code exists, every `§` keyword exists, and no page
  hardcodes the current version. The changelog page is excluded because it records past
  releases.

The new check found 11 website examples that did not compile. This change fixes them:

- 3 in the nullability guide were intentional failures; they are now labelled.
- 4 used old syntax: `§IF` without an ID (2), a C# line inside a Calor block, and `←`
  annotations inside code.
- 2 in the cross-module effects guide used undefined types and a call with a missing argument,
  and could only be compiled as a pair. They are now one consistent two-file example, and its
  quoted output is checked.
- 2 refinement-type examples did arithmetic on named refinement types (`§RTYPE`), which the
  type checker then rejected (`Calor0202`). They were rewritten with inline refinements. A later
  fix in this release (#1515, under "Fixed") makes that arithmetic type-check.

The 0.25 baseline counted 13 failures; the other 2 were method fragments (`§MT`), which are not
complete programs. This change also fixes 5 stale keyword references in prose (`§FOREACH` twice,
`§MATCH`, `§CAST`, `§WHILE`) and replaced
an invented error format on the `compile` page with real, checked output. The MCP walkthrough
in the dependent-types tutorial showed a response its example could not produce; it now shows
responses captured from the MCP server. The refinement-type pages now say that a default
`calor` compile does not run the obligation solver (the `calor_refine` MCP tool does). New findings use
`Calor1332`–`Calor1334`.

### C# array expressions (#1132)

- **C# array expressions now convert to native Calor and keep C# evaluation order** (#1132).
  An array created inside an expression (a `return`, a call argument, a `?:` branch, or a field
  or property initializer) now converts to Calor instead of being kept as C#. Before, the
  converter wrote `return new T[,] { … }` as a block bound to a name that the generated C# never
  declared, so the C# failed to compile (`CS0103`, `CS0201`). Arrays in a `?:` branch did not
  parse. In both cases, the only thing that saved the conversion was the post-conversion rescue
  (`§CSHARP`, which keeps the original C# verbatim).
  - Elements are evaluated once, left to right, where the C# had them. The unselected `?:` branch
    and the right operand of `&&`/`||` are still skipped. Before, elements, sizes and arguments
    could be moved into temporaries that ran before earlier parts of the same statement. For
    example, `int[] a = { x, SetX(), x }` read the new `x` in slot 0, and `new int[n, Next()]`
    read `n` after `Next()` ran.
  - New Calor forms: an inline `§ARR2D{id:id:T} §ROW … §/ARR2D{id}`, a sized `§ARR2D` with
    `§ROW` lines for rank 3 and up (`§ARR2D{id:id:i32:2:1:2}`), and `(§IDX2D g i j).Member` for
    a member of an element. Jagged arrays keep their element type and rank order
    (`new int[2][]`, `new int[,][]`). A bare `T[,] g = { … }` initializer is a rectangular array.
    `new[] { F(), G() }` keeps its element type instead of becoming `object[]`. `new T[0]` and
    `{ }` create fresh arrays instead of sharing `Array.Empty<T>()`.
  - Names the converter generates (`_hoist000`, `arr2d004`) skip every identifier in the source
    and in the other files of a project migration. A user local named `_hoist000` is no longer
    rebound (`Calor0260`).
  - Some shapes cannot be written in place. They stay C# and are reported as a conversion loss.
    An assignment used as an element, index or size is reported as
    `conditional-expression-hoisting`; before, it ran twice. An element that Calor writes as a
    block is reported as `array-element-block`. That covers a collection initializer such as
    `{ new List<int> { 1 }, 2 }`, which before lost the list and produced a one-element array.
    It also covers a multi-line element such as an anonymous object in a rectangular array row.
  - Effect checking: the elements of `§ARR2D` rows are now charged. Before, they were not
    inferred, so a row could hide an effect its function did not declare. Effects of array
    members such as `GetLength` and `Rank` are unchanged. On a receiver typed as an array
    (`i32[]`, `i32[,]`) they are still unknown calls, so a function that calls one reports
    `Calor0410` (an effect used but not declared) unless it declares that effect or is compiled
    with `--permissive-effects`. Two cases resolve through Calor's built-in effect manifests
    instead, as before: a receiver declared as `System.Array`, and an array whose element type
    is in a namespace the manifests give a default (for example `System.Threading.Tasks.ValueTask[]`).

### Release process

These change how Calor is published, not the compiler. The release gate (#1410) lets a release
publish only with an adjudication identity: the accepted commit plus the hash of its record.

- **#1545.** Publishing to NuGet now uses nuget.org Trusted Publishing: the publish job exchanges a
  short-lived GitHub OpenID Connect token for a NuGet key, and no NuGet API key is stored in
  GitHub. The trusted-publishing policy requires the `nuget-release` environment, which only `main`
  can use, so the publish workflow kept in an older tag cannot obtain a key. On 2026-10-19 the
  read-only settings check (`scripts/check_release_credentials.py --live`) reported the gap
  closed; one setting, the Actions workflow permissions, was confirmed by a recorded maintainer
  attestation because the API does not expose it to the check.
- **#1546.** The gate checks on every run that this notes section in the candidate hashes to the
  adjudicated notes (`G014`). So these notes had to be final at the freeze.
- **#1547.** Publishing a release no longer publishes benchmark results. That needs a separate
  manual run and an approval in the acceptance record (`G015`).
- The release jobs install exactly .NET SDK 10.0.401, and the website no longer fetches its fonts
  from Google Fonts at build time.
