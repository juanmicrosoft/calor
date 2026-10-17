# Fix #1524/#1528 — Codex extra verification-only pass (EXC-1540-OPAQUE-FIX)

- Exception: EXC-1540-OPAQUE-FIX (@juanmicrosoft, 2026-10-17; R0 scope amendment 1.3.0): one fix plus exactly one Codex verification-only pass. A BLOCKING or MAJOR finding stops the PR for a maintainer decision.
- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt plus `git diff HEAD~1..HEAD -- src tests` on stdin.
- Reviewed commit: `81ef7f5cc` ("keep a preserved target-typed new() as its source text").
- Scope: the opaque-span fix only. A preserved target-typed `new()` escalates to its enclosing statement (`PreserveStatement`, caught in every mode) instead of being written with its explicit type.
- Verdict: **REQUEST CHANGES**, with 2 MAJOR findings. Source-level verification only: Codex's read-only sandbox could not run the tests.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | In a lambda body (`Use(() => { return new() { L = { 5 } }; })`), escalation preserves only the lambda's `return` statement. Argument hoisting can then bind the lambda untyped (`var _lam = () => { ... }`), where the target type of `new()` cannot be inferred. | Open. Not fixed: the exception allows one fix and one pass, and both are spent. |
| 2 | MAJOR | Statement preservation copies leading trivia (`ToFullString()`). If the escalated statement is the first one under `#if`, the raw statement repeats the opening directive that `ConvertBlock`'s preprocessor node also emits, which leaves an unmatched `#if`. | Open. Not fixed, for the same reason. |

Per the exception, the PR stops here for a maintainer decision. No corpus ledger was regenerated after this pass.

Raw output: `results-extra-verification-pass-codex.md`.
