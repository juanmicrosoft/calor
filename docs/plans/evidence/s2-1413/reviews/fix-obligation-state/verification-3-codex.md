# S2 #1413 fix-obligation-state — Codex final verification-only pass (maintainer decision Q8)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: APPROVE

No findings within the authorized final-commit scope (`674e3bdf`).

- **MAJOR resolved.** Only `ProofObligation` uses `IsStaleBefore`; subtype obligations use `IsStaleAfterEntry` ([ObligationSolver.cs:304](src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:304)). The initializer’s heap read therefore makes its SAT result inexact ([FactCollector.cs:91](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:91)), yielding `Unsupported` before counterexample construction ([ObligationSolver.cs:314](src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:314)).
- **No new false proof, false Discharged, or fabricated counterexample introduced.** The executable change only tightens SAT demotion for non-proof obligations. Solver assertions, UNSAT handling, and proof-obligation behavior are unchanged.
- **Regression and counts consistent.** The added test reproduces the hiding-property witness and rejects `Failed` ([S2ObligationStateTests.cs:314](tests/Calor.Compiler.Tests/S2ObligationStateTests.cs:314)). There are **24 Facts + 17 theory rows = 41 cases**; **12,587 + 41 = 12,628**, matching [test-manifest.json:8](eng/test-manifest.json:8). Verification.Tests remains **411** ([line 106](eng/test-manifest.json:106)); skip counts are unchanged.

Static verification only; no `dotnet` execution. Reported passing runs and the pre-fix regression failure remain author-provided evidence. The two separately dispositioned residuals were excluded as instructed.
