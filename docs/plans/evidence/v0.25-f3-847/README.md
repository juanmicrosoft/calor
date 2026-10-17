# 0.25 F3 (#847) — native non-capturing local functions in place

**Status:** IN_PROGRESS (two stacked draft PRs; main frozen for 0.24 A1 #1408). PR A (#1519, `milestone-0.25/f3-847-local-functions`): the construct, binding, effects, verification refusal, Calor0211. PR B (`milestone-0.25/f3-847-converter`, stacked on A): C# conversion and the per-row evidence. **Scope:** v0.25 R0 1.0.0,
family F3. **Independence:** reduced (Codex reviews only; see `reviews/`). Not independently
reviewed or verified.

## Design

| Layer | Change |
|---|---|
| Syntax | A `§F{id:Name} (T:x) -> R` nested directly in a `§F`/`§MT` body is a `LocalFunctionStatementNode` (new AST node wrapping a `FunctionNode`). No new token. |
| C# emission | Emitted in place as a C# `static` local function. `static` makes any capture of enclosing locals, parameters or `this` a C# compile error (`CS8421`), so a missed capture can never rebind silently. Emitter state is saved and restored around the body. Local-function names, and inside a local body every name the enclosing callable declares (parameters, bindings, pattern/loop/catch/fixed variables), are never qualified to a module function, so C# resolves them (review rounds 1 and 2). |
| Binding | Local functions of a body are declared in a scope between the host (class/module) scope and the body scope: visible before their declaration, to each other and recursively. Their bodies are bound as separate `BoundFunction`s (`BoundMemberKind.LocalFunction`) in a static context whose scope chain skips the enclosing locals and parameters. A bare call resolves to a visible local function before any `{Class}.Name` / module candidate (`GetCallLookupNames`), and stays attributed to it even when overload checking fails. A bare call whose innermost declaration is a local or parameter is a delegate invocation, never a function lookup (all callables; round 1, finding 4). |
| Effects | A local body is charged to the enclosing callable at its declaration, called or not (sound over-approximation). A call the binder resolved to a local function charges nothing more in the enclosing body; inside a lambda any use of a local function (call, delegate, method group) charges the union of the local bodies (round 1, finding 2); without binder data, a bare call naming a local function is `Unknown` (fail closed), never resolved by name to a same-named member. As an escaping value, a local function's row is the union of all local bodies of that callable (Calor0424 if it does not fit). Local bodies get their own row-site checks. A local function shadows same-named fields and properties in the row checker and in the inferrer's alias, method-group and expression-call paths; a conditional value's row joins its branches (rounds 1 and 2). Local functions cannot declare `§E` or rows (Calor0211). |
| Verification | Local functions cannot declare `§Q`/`§S`, refinement types or `§PROOF` (Calor0211). The Z3 body encoder refuses a body containing a local function declaration (unmodeled statement), and calls are untranslatable, so no outer contract is proven through a local body; runtime guards stay. The reflection walker treats the node as a leaf, so no fact, obligation, assignment or return in a local body is attributed to the enclosing callable. |
| Validation | `ReturnValidationPass` walks local bodies as their own owners and reports `Calor0211` for misplaced/nested local functions (including inside lambdas) and for `§E`, contracts, rows, generics (including an embedded `<T>` name), refinement types, yields, `§PROOF`. `BindValidationPass`, `ValidateMatchExpressions` and the type checker visit local bodies explicitly; the type checker scopes local functions so they shadow module functions. |
| Converter (PR B) | `RoslynSyntaxVisitor.IsNativeLocalFunction`: directly in a method body; no attributes, type parameters, constraints, non-`static` modifiers (async/unsafe/extern), ref return, yield or nested local function; plain by-value parameters without defaults; no capture (semantic model: enclosing locals/parameters, `this`/`base`, unqualified instance members; an unresolved simple name or no semantic model counts as capturing). A member with any other local function is preserved whole as `§CSHARP` (`local-function` issue), as before. `FeatureSupport["local-function"]` is `Partial` with the slice in its description. |

## Per-row results

`reproduce/reproduce.py` reuses the R0 driver and `ProbeRunner` unchanged and writes
`results.json` and `generated/` (Release build, macOS arm64; commit and `src/` tree are recorded
in `results.json`). Surfaces: `cli-default`, `cli-passthrough`, `mcp-default`,
`mcp-passthroughOnError`, `mcp-passthroughOnError-moduleName` (all five give the same outcome per
row). `ProjectMigrator` with and without passthrough is covered by the tests, not by this script.

| Case | Expected | R0 baseline | This branch (all 5 surfaces) |
|---|---|---|---|
| F3-LOCAL-01 | native | preserved-match@permissive-effects | **native-match**, default compile, 0 losses, `3\|120` |
| F3-LOCAL-02 (C-1) | native | preserved-match@permissive-effects | **native-match**, default compile, 0 losses, `3\|1002` |
| F3-LOCAL-03 (C-2, C-3, forward call, delegate) | native | preserved-match@permissive-effects | **native-match**, default compile, 0 losses, `6\|5\|60\|3000\|502` |
| F3-LOCAL-04 (capturing, generic, iterator) | preserved | preserved-match@permissive-effects | preserved-match@permissive-effects (3 `§CSHARP`, `local-function` issue; default compile still Calor0410 in the caller, unchanged) |
| F3-LOCAL-05 (async; fixture `fixtures/F3-LOCAL-05.cs.txt`) | preserved | not-measured | preserved-match@permissive-effects (1 `§CSHARP`) |
| F3-LOCAL-A1 (analysis) | native-or-rejected | not-measured | executable tests only (`LocalFunctionLanguageTests`), no per-case evidence record |

The Calor0410 on preserved rows is the R0 behavior for callers of `§CSHARP` members, not a
local-function regression; #1144/#1430 own it.

## Tests

PR A: `tests/Calor.Compiler.Tests/LocalFunctions/LocalFunctionLanguageTests.cs` — native Calor
name resolution (member, inherited and module shadowing, type checking), effect charging,
escaping delegates and lambdas, Calor0211 placement and shape, capture rejection (CS8421),
verification (no proof from a local body, runtime guard kept), the Calor-emitter round trip, and
one regression per Codex finding on the construct.

PR B: `tests/Calor.Compiler.Tests/Migration/LocalFunctionConversionTests.cs` — every native row
on 8 surfaces (library, CLI default/passthrough options, MCP default/passthroughOnError/moduleName,
`ProjectMigrator` with/without passthrough) converted, compiled with default options and run
against the original; preserved rows on the same surfaces; `--no-fallback`; capture and shape
negative controls; non-capturing look-alikes; the compound-assignment fix.
