# #1135 G3 repair PR (#1492) — adversarial review round 1 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Reviewed commit:** `452afeb2`
(the logic of `941ffc71` is the same; that commit only moved the two new classes into existing
files for the calor-first guard). **Verdict:** 1 BLOCKING, 2 MAJOR, 2 MINOR, 1 NIT. Changes
requested.

No protocol execution or control run was dispatched while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | The registered seed (`random_seed` 42) is never applied: Z3 4.15.7 rejects it as a context parameter, and D006 checks only the constant's text | **Recorded, not applied.** The finding is correct (it was also in the PR's findings). Applying the seed would change every Z3-backed test's search. That is a verifier behavior change outside a determinism repair. Amendment 1.2.0 adds `z3.randomSeed.applied: false` and an `effect` text, and appends a sentence to `z3.rule`. The effective configuration (Z3 defaults plus the timeout) is deterministic. README Pins and the amendment section say the same |
| 2 | MAJOR | Old Windows cache entries for non-ASCII string literals stay valid, because `SemanticsVersion` did not change | **Fixed differently.** `VerificationCacheEntry.CurrentFormatVersion` 1.18 → 1.19 evicts every entry. `SemanticsVersion` stays `z3-executable-semantics-v2`, because Linux and macOS output is unchanged and the value is frozen in `g2-1421/cases.json` (`translator-fixture` expected) and the #1419 registration. No test was added: a new test in a registered project is an unregistered case |
| 3 | MAJOR | The fresh context drops the caller's context settings (`rlimit`, `model`, …) | **Fixed.** `Z3ContextFactory` records the settings of every context it creates (`ConditionalWeakTable`). `CreateLike(source)` makes the check context, and the `Simplify` context, with exactly those settings. A context the factory did not create has unknown settings, so the check runs in that context as before, without isolation. `IsolatedSolver` now takes the source context |
| 4 | MINOR | Replay keeps the engine but loses incremental search state; a different valid counterexample can result | **Documented.** The `IsolatedSolver` remarks, the CHANGELOG, and the PR body say so: a satisfiable check can return a different, equally valid model, and a check near the timeout can end differently. That state was itself GC-dependent. Not tested: no new test may be added to a registered project |
| 5 | MINOR | The CHANGELOG claims determinism on every platform before the protocol ran | **Fixed.** The entry now says three causes are fixed and that the #1421 execution decides determinism |
| 6 | NIT | "No registered case was run" conflicts with local test runs | **Fixed.** Now reads "No decision-bearing protocol execution was run; the repair PR ran local and ordinary-CI tests" |
