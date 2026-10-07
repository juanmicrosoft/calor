# Fix binding performance regression — Codex adversarial review, round 1

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, diff `origin/main...HEAD` (commit `74906592`) on stdin.
- First attempt (16:48) hit the Codex usage limit before reviewing; it was rerun unchanged at 17:57.
- Asked to check: equivalence of an absent `_callableState` key with a stored
  `CallableMutationSummary.Empty` for every read and enumeration; exact equivalence of the
  symbol rollback (enumeration order, exception paths, nested loops, re-entrancy); the
  declaration-counter journal; remaining quadratic paths; test meaning and flakiness;
  manifest/baseline consistency; CHANGELOG accuracy.
- Verdict: **APPROVE**. No BLOCKER or MAJOR findings. Reviewer's summary: "I found no change to
  binder semantics, diagnostics, symbol IDs/order, nullability, callable-mutation tracking, or
  soundness." It statically confirmed that missing entries return the exact `Empty` singleton,
  joins and the fixed point keep their reference-equality behavior, duplicate-id `Add` failures
  happen before list insertion, reverse rollback preserves order, nested discovery returns before
  replacing the journal, and counter restoration reverses repeated increments.

## Findings and dispositions

| # | Finding | Severity | Disposition |
|---|---|---|---|
| 1 | The CHANGELOG claim "binding is linear in module size" is too broad. Two module-wide costs remain: (a) a mutation-free lambda creates a distinct non-`Empty` summary, so locals holding lambdas stay in `_callableState` across functions and are copied at each snapshot (N functions with one local lambda each gives Θ(N²) entry copies); (b) nominal type resolution (`Binder.cs` `_symbolsById.Values.OfType<TypeSymbol>()` scans) is linear per lookup. Both are existing limitations, not regressions from this change. | MINOR | Fixed in wording. The CHANGELOG entry now describes the quadratic copying that was removed and names both remaining costs. Not changed in code: (a) a non-`Empty` summary is not reference-equal to `Empty` and `UnionCallableEffects` distinguishes them, so dropping those entries would change semantics; (b) predates #1461 and is outside the regression. Neither is exercised by the performance suite. |
| 2 | The CHANGELOG said both tables were copied "at every loop, branch, and lambda". The symbol table and declaration counters were copied only in `PrepareLoopCallableState`; branches and lambdas copied the callable-state map. | NIT | Fixed. The entry and the `Binding_ScalesLinearlyWithFunctionCount` doc comment describe the two mechanisms separately. |

The reviewer did not rerun tests (read-only sandbox). Test and timing evidence is in the PR body.
