# Fix #1524/#1528 — Codex verification-only pass

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt plus `git diff 4eed94c95..HEAD` (review records excluded) on stdin.
- Reviewed commit: `4a4d62c8a`.
- Scope: the four round-3 dispositions only (see `round-3-codex.md`).
- Verdict: **APPROVE**. Source-level verification; Codex's read-only sandbox could not run the tests. The tests were run locally (see the PR body).

| Item | Result |
|---|---|
| 1. `CreationSourceText` spells out target-typed `new()` in the visitor `§CS` and the emitter `CSharpSource` | VERIFIED |
| 2. `PendingStatementsChanged` ignores only declaration-only binds and still detects every evaluating hoist, queue shrinkage, or a changed prefix | VERIFIED |
| 3. `ConvertNestedBodyBlock` restores the enclosing queue once, in order; body hoists stay in the lambda body | VERIFIED |
| 4. `ConvertDefaultExpression` dispatches on the resolved `SpecialType`, spelling only without a semantic model | VERIFIED |

Raw output: `results-verification-pass-codex.md`.
