# S2 #1413 fix-num-refusal — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

**MAJOR — The while fix can introduce a fabricated counterexample by discarding a refused bound.**

`WhileConditionAnalyzer.GetIntValue` now rejects oversized literals (`src/Calor.Compiler/Verification/Z3/KInduction/WhileConditionAnalyzer.cs:410`). However, `AnalyzeLessOrEqual` still returns a non-null result with a missing upper bound (`:158`), and conjunction analysis combines it with the surviving lower bound (`:90`). That result remains analyzable (`:27`).

On the same bound-tree path reviewed in round 3, use:

```text
condition: i <= -4294967296 && i >= 1
transition: i = i + 1
invariant: i < 0
```

The real condition permits no entry state. After the fix, analysis drops the upper bound and retains `i >= 1`. The base query therefore becomes `i >= 1 && !(i < 0)`, which is satisfiable. The prover returns `Disproven` with an “Invariant fails at loop entry” counterexample (`src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:378`, `:394`, `:398`). Every such model violates the original upper bound.

This directly checks the new refusal path. The original positive-bound false-proof witness is closed, but the repair does not safely refuse the complete condition. Consequently, the CHANGELOG’s claim that both loop forms refuse oversized bounds remains inaccurate (`CHANGELOG.md:33`). Propagate the refused-bound failure through conjunction analysis and add a direct bound-tree regression; inability to reach it from Calor source does not cover the public bound-tree path.

The other round-3 fixes check out:

- **Wrapped signed magnitude: resolved.** Sign/magnitude limits replace the wrapped `Value` check, including the extra negative magnitude; valid explicit unsigned literals retain their path (`src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:99`). Translation and simplification share that predicate (`:520`, `:90`; `src/Calor.Compiler/Verification/ExpressionSimplifier.cs:33`). The exact reported witness requires `Unsupported` (`tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:278`).
- **Cache coverage: resolved.** Warm-order tests assert the prime verdict and entry writes (`tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:212`). The forged-entry theory covers current format, 1.20, and 1.21 with discriminating verdicts (`:226`, `:255`, `:270`). Production checks exact format equality and deletes rejected entries (`src/Calor.Compiler/Verification/Z3/Cache/VerificationCacheEntry.cs:225`; `src/Calor.Compiler/Verification/Z3/Cache/VerificationCache.cs:234`).
- **Counts: consistent.** Nine facts plus 13 theory rows total **22** verification cases: **411 → 433** (`eng/test-manifest.json:106`). The single added compiler fact accounts for **12641 → 12642** (`tests/Calor.Compiler.Tests/Analysis/KInductionTests.cs:515`; `eng/test-manifest.json:8`). Differential totals independently reconcile to **435 Proven, 150 Assumed, 585 Refuted**, totaling 1,170 cases and 40 eliding forms (`bench/phase0-agent-native/verifier-runtime-differential.json:19`, `:29`).

No additional false `Proven` or false `Discharged` was identified within the round-3 changes. The fabricated-counterexample path above prevents confirming requirement (2). This was source-only verification; no dotnet or native Z3 execution was performed.
