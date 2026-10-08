# Codex review, round 2 (#1132 F5)

- Reviewer: OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`,
  input `git diff origin/main...HEAD` at `a3293c14` (evidence data excluded). Cross-family, reduced
  independence. The first attempt hit the Codex usage limit; the run waited for the window to reopen.
- Verdict: **REQUEST-CHANGES** (2 BLOCKING, 4 MAJOR, 1 MINOR). Codex confirmed the round-1
  witnesses, assignment controls, constructor order, empty-level sizes, rank order and fresh empty
  arrays, and that 57 approved snapshots match.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `new object[,] { { new int[1, 1] }, { 42 } }`: a sized expression `§ARR2D` without a closer took the parent's next `§ROW` (2x1 became 1x1) | Fixed: expression-position sized and zero-size `§ARR2D` carry `§/ARR2D{id}`; the parser accepts the closer after sizes. Witness: `round2` row |
| 2 | BLOCKING | `new int[n, n++]` and `new int[x = Bump()]`: sizes still converted with pending statements (early read, double evaluation) | Fixed: sizes convert in place like elements; the assignment size is preserved (`conditional-expression-hoisting`). Witnesses: `round2` row, negative control |
| 3 | MAJOR | Name reservation reparsed without the conversion's preprocessor symbols; escape test also declared the name plainly | Fixed: the converted syntax tree is reserved too; the test isolates the escape and adds an `#if FOO` case |
| 4 | MAJOR | `object[] a = new[] { S() }` became `new object[]` (receiving type won) | Fixed: Roslyn's created element type first. Witness: `round2` row |
| 5 | MAJOR | `null` / `default` before a hoisted argument bound to an untyped temp (Calor0251 / CS8716) | Fixed: literals and `default` stay in place (converter and emitter). Witness: `round2` row |
| 6 | MAJOR | `GetValue` boxes (alloc); `Initialize` runs element constructors (arbitrary effects) | Fixed: `GetValue` `alloc`; `System.Array.Initialize` resolves as unknown. Witness: effects theory |
| 7 | MINOR | Published computed-size boundary narrower than behavior (`flag ? new int[n + 1] : …` preserved) | Website, CHANGELOG and README boundary corrected |
