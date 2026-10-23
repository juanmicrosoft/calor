# Codex review, round 3 (#1132 F5)

- Reviewer: OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`,
  input `git diff origin/main...HEAD` at `c8bd8716` (evidence data excluded). Cross-family, reduced
  independence. This is the last review round allowed by the R0 capacity (3 rounds plus one
  verification-only pass).
- Verdict: **REQUEST-CHANGES** (4 BLOCKING, 4 MAJOR, 1 MINOR). Codex confirmed the round-1/2
  witnesses, the closer, assignment-size, symbol-aware reservation, inferred element type, fresh empty
  arrays, null/default and named array-member fixes, and that 57 approved snapshots match.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Mapping every array receiver to `System.Array` let its pure default certify extension methods (`this int[]` writing to the console) | Fixed: manifest restored to main; `EffectResolver` answers an array receiver only from a table of `System.Array`'s own instance members, anything else falls through to extension/unknown. Witness: extension fact |
| 2 | BLOCKING | Hoisting every non-literal statement element moved implicit conversions (`scsc` became `sscc`) and lost method-group target types (CS8917) | Fixed: statement elements are no longer hoisted for carrying a call; they stay on their line. Witnesses: `round3` row (conversion order), preserved fact (method group) |
| 3 | BLOCKING | `g[n, n++]` read `n` after the increment | Fixed: multi-dimensional indices convert in place. Witness: `round3` row |
| 4 | BLOCKING | `Use(in xs[I()], new int[] { S() }, new Cell())` evaluated `I()` after the hoisted arguments | Fixed: a ref/out/in argument whose address evaluates a call or update, before a hoisted argument, preserves the member. Witness: preserved fact |
| 5 | MAJOR | `new int[][,]` bound as `int[,][]`; declared `int[][,]` lost its element rank | Fixed in the parser binding type and `TryGetDeclaredArrayElementType`. Witness: `round3` row |
| 6 | MAJOR | `new int[1, 1 + 1, 0] { { {}, {} } }` regrouped by a non-literal size (CS0847) | Fixed: an empty level leaves no elements, so the creation is its sizes. Witness: `round3` row |
| 7 | MAJOR | `new[,] { … }` took the 1-D path (CS0029) | Fixed: converted as rectangular from Roslyn's created type, preserved if unknown. Witness: `round3` row |
| 8 | MAJOR | `SetValue` boxes but was charged only `mut` | Fixed: `mut` and `alloc`. Witness: effects theory |
| 9 | MINOR | The interpolation-escape name test used a plain identifier | Fixed: the fixture spells `_hoist001` only as `_hoist001`; the probe now forces a hoist |
