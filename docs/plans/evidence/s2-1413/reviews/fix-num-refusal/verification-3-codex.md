# S2 #1413 fix-num-refusal — Codex verification-only pass under amendment 1.3.2 (condition 5)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`). Change commit `c04b97b3` on base `bdb430db`; the 1.3.2 text (from main), the previous (1.3.1) verification pass, and the change diff were in the prompt.

## Review output (verbatim)

VERDICT: APPROVE

No BLOCKING, MAJOR, MINOR, or NIT findings. The previous pass’s MAJOR is resolved.

1. **One change only.** Git confirms `c04b97b3157beabd82f06d53b32c580347cfaa0c` is the sole commit above, and has sole parent, `bdb430db1c1dfdbcd578c5b1b74110841be95a2d`. Overflow classification only constructs and simplifies terms (`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:81`). The simplifier calls `Expr.Simplify`, with no satisfiability check or timeout (`:1666`). The probe and `OverflowProbeStatusForTesting` are removed. The KInduction diff is empty; (a) and (c) remain unchanged.

2. **Tests and permitted scope pass.**
   - Five classification rows directly exercise the rule without creating a solver; two rows assert the resulting checked-arithmetic assumption (`tests/Calor.Compiler.Tests/S2NumericDeterminismTests.cs:38`, `:60`).
   - Exactly three guarded runtime rows now require `Assumed` with only checked-arithmetic, retaining the unselected-branch execution checks (`tests/Calor.Compiler.Tests/ProductionOverflowRuntimeTests.cs:312`, `:331`, `:336`). The theory rename accurately reflects this authorized demotion and is within condition 2.
   - The allowance names exactly the four authorized forms, requires provable postconditions, and spans three depths: exactly 12 cells (`tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs:27`, `:34`, `:39`, `:117`). Exact assumption matching remains enforced (`:360`). Arrays retain their reference-model assumption (`DifferentialFormRegistry.cs:72`).
   - `VerifierTests.cs` is byte-for-byte identical to `3f3016d2`, including the 1 ms test (`tests/Calor.Verification.Tests/VerifierTests.cs:2448`).
   - CHANGELOG changes are confined to the rule and its determinism explanation (`CHANGELOG.md:36`). Theory rows increase **7 → 9**, matching the manifest’s **12649 → 12651**; only that count and its explanatory note change (`eng/test-manifest.json:8`, `:10`).

3. **Report pins pass.** Both files equal their committed blobs, contain no CR bytes, and `shasum -a 256` returns:
   - JSON: `aa1f86f9ae5e6e9c6eedea1970b4080f8e347271c0ba99434a1afa3203b9c9e9`
   - Markdown: `24901f2176fb3157dedcb79f40564f2bfe9bcd10db94018f5c8942c56c20b2d5`

   These match the registered generator-output pins. Totals are **429 Proven / 156 Assumed / 585 Refuted** (`bench/phase0-agent-native/verifier-runtime-differential.json:29`). Only the two scalar status summaries change; array statuses remain Assumed. The generator still checks emitted guards and exact generated report bytes (`DifferentialGate.cs:280`, `VerifierRuntimeDifferentialTests.cs:82`).

4. **No strengthening identified.** A classification simplifying to true already bypassed the old probe. Every other classification now caps successful verification at Assumed with checked-arithmetic (`Z3Verifier.cs:509`, `:552`, `:558`). Normal postcondition verification remains unchanged (`:519`). No new false Proven or Discharged was identified within the change.

5. **Size and discovery condition pass.** My whole-PR non-test tally against `ffa75e8b`, including reports, CHANGELOG, and manifest, is **+204/-27 = 231**, rather than the supplied 226; it remains below 600. D-NUM-WHILE-BOUND remains demoted by the unchanged whole-loop refusal (`src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs:165`, `:586`), with both witnesses retained (`S2NumericDeterminismTests.cs:79`, `:81`, `:100`).

Source-only verification; no dotnet or native Z3 execution. HEAD remained `c04b97b3`, the working tree remained clean, and no files or ceiling values changed.
