# Codex review, round 1 (#1132 F5)

- Reviewer: OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`,
  input `git diff origin/main...HEAD` at `a2e71490`. Cross-family, reduced independence.
- Verdict: **REQUEST-CHANGES** (4 BLOCKING, 5 MAJOR, 1 MINOR).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `Use(new int[] { S(1) }, new Cell(S(2)))`: arrays no longer hoisted, but the later `§NEW` argument still is, so `S(2)` ran first (regression from this PR) | Fixed. `HoistComplexArguments` binds every earlier argument that is not a literal, lambda, `ref`/`out`/`in`, or local/parameter read before a hoisted one, in order. Witness: `shapes` row |
| 2 | BLOCKING | `{ x, x = S(), x }`, `{ y, y++, y }`, `a[x = S()]`: converter-level pending statements reorder and double-evaluate | Fixed for arrays: elements, and indices containing an assignment, convert as conditional operands; assignments are preserved (`conditional-expression-hoisting`), `y++` stays native `post-inc`. Witness: `shapes` row (`y++`), negative control (assignment element and index). The same pattern as a plain call argument is pre-existing and not array-specific (README residual) |
| 3 | BLOCKING | Statement sizes `new int[n, Next()]` and `new Cell(x, Next())` inside an element hoisted selectively | Fixed: statement-position sizes are quoted in place; `§NEW` arguments hoist all-or-nothing in order. Witness: `shapes` row |
| 4 | BLOCKING | `new int[0, 3] {}` / `new int[2, 0, 3] { {}, {} }` lost written sizes | Fixed: written sizes kept when the shape has an empty level. Witness: `shapes` row |
| 5 | MAJOR | Escaped identifier in an interpolation hole and a base-class field in another file not reserved | Fixed: reservation walks the parsed tree and the project's other files. Witness: name test |
| 6 | MAJOR | Array receivers now hit `System.Array`'s pure default: `Clone`, `GetEnumerator` certified pure | Fixed: `Clone`, `GetEnumerator`, `ToString` charged `alloc`. Witness: effects theory |
| 7 | MAJOR | `new[] { S(1), S(2) }` became `object[]` | Fixed: Roslyn's inferred element type. Witness: `shapes` row (`imp is int[]`) |
| 8 | MAJOR | `new int[2][]` emitted `new int[][2]`; `new int[,][]` became `int[][,]` | Fixed: created rank first in the C# emitter and in generated binding types. Witness: `shapes` row |
| 9 | MAJOR | `new int[0]` became shared `Array.Empty<int>()` (identity) | Fixed: fresh zero-length arrays. Witness: `shapes` row (`fresh`), `LinqSupportTests` updated |
| 10 | MINOR | Public claims exceed the demonstrated boundary | Website, CHANGELOG and README limits rewritten; residuals listed |
