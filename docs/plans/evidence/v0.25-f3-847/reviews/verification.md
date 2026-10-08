# Codex verification pass (0.25 F3, #847)

- **Reviewer:** OpenAI Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family. Reduced independence; not an independent human review.
- **Input:** the combined diff of both stacked PRs (#1519 at `eda1da70`, #1531 at `f436256c`) from the merge base `fd34f41f`, evidence data excluded. The prompt asked only whether the round-3 dispositions (`round-3.md`) hold, using the current Release compiler with default effect enforcement.
- **Verdict:** **VERIFIED.** No new problems were reported.

## Codex output (verbatim)

VERIFIED

1. Expression-call: `§C Take §A Help §/C`, directly and inside the recorded lambda → Calor0211.
2. Other value positions: `(?? f Help)` in return/binding, array/List elements, and lambda binding → Calor0211.
3. Qualified store: `§ASSIGN Probe.Saved Help` → Calor0211.
4. Accepted limitation: recorded loop-variable `Add`/module-function collision → Calor1002/CS0103 ("Add does not exist"); compilation fails loudly.
5. Bind validation: local `Add → List<i32>` shadowing module `Add → [i32]` → successful compile, zero diagnostics, no Calor0254.
6. Converter: C# array/List storage, `f ?? L`, qualified store, cast, expression-call argument and lambda-return probes → whole member preserved as §CSHARP with `[local-function]` report.
7. Allowed positions: direct §B, §R, simple §ASSIGN, ?: branches of each, and §C{…} arguments directly/inside a lambda → 8/8 compile successfully, zero diagnostics.

All probes used the current Release MCP compiler with `autoFix:false` and default effect enforcement.

## Review summary

| Round | Verdict | Findings | Outcome |
|---|---|---|---|
| 1 | REQUEST-CHANGES | 5 BLOCKING, 2 NON-BLOCKING | all fixed with regressions |
| 2 | none (usage limit, then a content-filter stop) | 4 reproduced in progress notes | all fixed with regressions |
| 3 | REQUEST-CHANGES | 3 BLOCKING, 2 NON-BLOCKING | 4 fixed; 1 non-blocking accepted as a loud-failure limitation |
| Verification | **VERIFIED** | — | — |

Pre-existing gaps noted outside F3: ordinary (non-local) method groups can still pass through `??`, collections and qualified stores without a row check, and the converter drops object initializers on `new StringBuilder { … }` (being filed separately).
