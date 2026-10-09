# Fix #1524/#1528 — Codex adversarial review, round 3

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt plus `git diff origin/main...HEAD` (review records excluded) on stdin.
- Reviewed commit: `4eed94c95`.
- Verdict: **REQUEST CHANGES** (3 blockers, 1 major-silent). Codex traced the code; it did not run probes.
- Codex confirmed the round-2 witnesses are fixed and found no double evaluation, no member-level fallback, and no `§CS` brace-scanning defect (the scanner skips comments and ordinary, verbatim, interpolated and raw strings).

## Findings and dispositions

| # | Finding (repro) | Severity | Disposition |
|---|---|---|---|
| 1 | The emitter fallback kept target-typed `new() { ... }` text. In `Use(new() { A = new Q().A })` the converter hoists the argument into an untyped binding, so the generated `var _newP = new() {...}` fails with CS8754. New in this PR. | blocker (loud) | Fixed. `CreationSourceText` spells out the resolved type of a target-typed creation (`ToMinimalDisplayString` at the creation's position) for both the visitor's `§CS` and the emitter's `CSharpSource`. Test `PreservedTargetTypedNew_SpellsOutItsType` asserts `§CS{new P() { A = new Q2().A }}`. That shape still does not compile end to end: Calor's overload resolution types a `§CS` argument as `object` (loud, pre-existing for every preserved argument). An assignment-target row (`q = new() { A = new Q2().A }` → `k7kq5`) runs end to end. |
| 2 | `new P { B = Get(out int x) }; return x;`: the out-variable declaration is a pending statement, so the rollback preserved the creation and dropped the `x` declaration; the type checker then reports `Undefined variable 'x'`. New in this PR. | blocker (loud) | Fixed. `PendingStatementsChanged` ignores declaration-only binds (`BindStatementNode` with no initializer): they evaluate nothing, so the creation stays native as on main. Test row → `7:True`, native. If such a creation is preserved for another reason, the later use of `x` is still a loud type-check error, never a silent change. |
| 3 | `using Single = System.Int32; default(Single)` became `SINGLE:0` (boxes a `Single`); main printed `0`. | major (silent) | Fixed. `ConvertDefaultExpression` dispatches on the resolved `SpecialType` when a semantic model is present, and on the spelling only without one. Test `DefaultOfAliasedName_UsesTheResolvedType` → `Int32:0`. |
| 4 | Snapshot comparison misses hoists erased by a block lambda: in `new P(new Q()) { F = () => { return 0; } }` the argument hoist is cleared by the lambda's `ConvertBlock`, so the queue looks unchanged and the node references an undeclared temp. Pre-existing on main (loud there). | blocker (loud) | Fixed at the root. Block-bodied lambdas and anonymous methods now convert through `ConvertNestedBodyBlock`, which saves the enclosing hoist queue and restores it afterwards (expression-bodied lambdas already did this). The argument hoist then survives, the rollback sees it, and the creation is preserved. Test row → `qk0`. |

## Negative control

The five new tests were run against the round-2 sources (`4eed94c95`): the four targeting findings 1–4 fail; the assignment-target row passes there too (it is an order control, not a witness).
