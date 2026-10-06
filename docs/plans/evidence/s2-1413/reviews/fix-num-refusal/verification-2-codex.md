# S2 #1413 fix-num-refusal — Codex verification-only pass under amendment 1.3.1 (condition 5)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`). Change commit `bdb430db` on base `3f3016d2` (the change was squashed into one commit before the pass, with maintainer authorization, to meet condition 1, and gained the condition-2 tests for a forced solver unknown and the dropped-conjunct while witness). The 1.3.1 text, the previous verification pass, the linux-arm64 CI failure, and the change diff were in the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

**MAJOR — Change (b) still makes verdicts depend on solver time, violating condition 3.**

When the static rule cannot establish safety, `CanFailForSomeInput` runs a timeout-bearing solver probe (`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:90`, `:1659`). Its answer determines the outcome: `SATISFIABLE` enables `Assumed`; `UNKNOWN` returns `Unsupported` (`:92`, `:525`, `:572`).

The added test demonstrates this directly: the **same** `i32` operands and `(+ x y)` postcondition expect `Unsupported` for `UNKNOWN` and `Assumed` for `SATISFIABLE` (`tests/Calor.Compiler.Tests/S2NumericDeterminismTests.cs:60`, `:65`, `:72`). A timeout can therefore still change the verdict. Mapping unknown to Unsupported correctly implements the registered refusal, but does not satisfy the separate requirement that **every decision of (b) depends only on operand types and literal values, never solver time or timeouts**. The determinism claim in `CHANGELOG.md:43` remains inaccurate.

The remaining checks pass on source inspection:

- **Scope:** `bdb430db` has exactly one parent, the full registered base `3f3016d2297491fca1734e931946747833def32d`. The diff of `WhileConditionAnalyzer.cs` against `ffa75e8b` is empty; `GetIntValue` is restored (`src/Calor.Compiler/Verification/Z3/KInduction/WhileConditionAnalyzer.cs:405`). The internal seam defaults to normal solver behavior and supports the required unknown test (`Z3Verifier.cs:77`, `:92`); the Microsoft.Z3 reference is confined to the test project (`tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj:26`).
- **Width rule and arm64 witness:** operand ranges conservatively cover nonliteral values, accepted literals use their exact values, and BigInteger endpoint calculations check the promoted result range (`src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:893`, `:908`, `:920`, `:950`). Promotion uses the existing width/signedness rules (`:772`). Every `i32 × u32` result fits signed 64 bits. Tests pin static safety without a probe and the resulting `Proven` outcome (`S2NumericDeterminismTests.cs:40`, `:54`; `tests/Calor.Verification.Tests/S2NumericRefusalTests.cs:143`, `:159`).
- **Previous MAJOR and D-NUM-WHILE-BOUND:** the whole-loop guard returns `Unsupported` before conjunction analysis or solver queries (`src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:165`, `:586`). Both the truncated-bound and dropped-conjunct witnesses assert that outcome (`S2NumericDeterminismTests.cs:77`, `:79`, `:98`).
- **Counts and size:** compiler additions are **3 + 2 + 2 = 7** theory rows; verification additions are **3** rows. Manifest totals correctly change **12642 → 12649** and **433 → 436** (`eng/test-manifest.json:8`, `:106`). Whole-PR non-test changes against `ffa75e8b` are **+204/-15 = 219**, below 600.
- **Proof safety:** no false `Proven` or `Discharged`, or strengthening outside the sound static safety replacement, was identified within the change.

Source-only verification; no dotnet or native Z3 execution. No files changed. Under condition 5, this MAJOR prevents approval and triggers the specified R-NUM `BLOCKED`/stopping-rule-1 disposition.
