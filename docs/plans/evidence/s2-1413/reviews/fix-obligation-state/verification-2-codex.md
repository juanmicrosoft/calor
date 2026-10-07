# S2 #1413 fix-obligation-state — Codex second verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static verification only; I did not run `dotnet`. The original BLOCKING defect is resolved, but the new exactness exception can admit a fabricated subtype refutation.

**MAJOR — The same-span exemption extends beyond proof conditions.**

[FactCollector.cs:80](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:80) ignores heap reads contained within the obligation’s span. [ObligationSolver.cs:304](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:304) applies that exception to subtype obligations too.

A direct static witness is:

```calor
§M{m1:M}
  §RTYPE{r1:Zero:i32} (== # INT:0)
  §CL{c1:Base:pub}
    §FLD{i32:Trigger:pub}
  §CL{c2:Box:pub}
    §EXT{Base}
    §PROP{pr1:Trigger:i32:pub}
      §GET
        §R INT:0
  §F{f1:Probe:pub} (Box:box) -> void
    §E{}
    §B{value:Zero} box.Trigger
```

The getter always returns zero, so the binding satisfies `Zero`. However:

- The field registry inherits `Base.Trigger` without removing it when `Box` declares the hiding property: [ContractTranslator.cs:243](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:243).
- The subtype condition substitutes `box.Trigger` for `#` and uses the binding’s span: [ObligationGenerator.cs:255](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/ObligationGenerator.cs:255).
- Translation models `Trigger` as an uninterpreted field accessor, permitting `Box_Trigger(box) = 1`: [ContractTranslator.cs:533](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Z3/ContractTranslator.cs:533).
- The only body heap read lies inside the binding span. No other exactness reason applies, so SAT reaches `SolverVerdict` at [ObligationSolver.cs:324](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:324), producing a false `Failed`.

In reviewed commit `59a7fa68`, this initializer was outside `§PROOF`; its heap read made `IsStaleAfterEntry` true and demoted SAT to Unsupported. The final change newly permits this refutation. Restricting the exception to established pure reads would avoid it; add a subtype-initializer regression.

The other requested confirmations hold:

| Item | Verification |
|---|---|
| Sequential getter blocker | Resolved. All proof-condition reads enter `MayChangeHeap` (`FactCollector.cs:75`), trigger alias expansion (`158–162`), and kill the precondition (`ObligationSolver.cs:168`). Regression remains at `S2ObligationStateTests.cs:287`. |
| Opaque entry predicate | Still resolved: opacity recorded at `FactCollector.cs:176`; early Unsupported at `ObligationSolver.cs:71`. |
| Original getter alias witness | Still resolved by global heap classification and alias expansion; regression at `S2ObligationStateTests.cs:261`. |
| Dropped-refinement dependency | Still resolved: preconditions, applicable facts, and condition are checked at `ObligationSolver.cs:306`; SAT demotion remains at `312`. |
| Shared inventories | Whole-body and scoped guard collection both use `MayChangeHeap` (`FactCollector.cs:204`, `429`); multidimensional reads remain included at `101`. |
| Existing residual/control | Throwing-predecessor limitation remains documented at `CHANGELOG.md:60` and `FactCollector.cs:120`; explicit throws are tracked at `340`. Named subtype control retains its non-Unsupported assertion at `ObligationTests.cs:1645`. |

I found no new false proof or false Discharged in the focused verification. The author’s same-condition getter caveat is candid, but it does not establish counterexample soundness, and the exception also affects subtype initializers.

Counts are consistent: **23 Facts + 17 theory rows = 40 cases**; **12,587 + 40 = 12,627**, matching `eng/test-manifest.json:8`. Verification.Tests remains **411** at line `106`. Passing runs and the regression’s pre-fix failure remain author-provided evidence.

## Response

| Finding | Disposition |
|---|---|
| MAJOR same-span exemption extends to subtype initializers | Fixed. Only `ProofObligation` counterexamples use the same-span rule (`IsStaleBefore`). Every other obligation kind uses `IsStaleAfterEntry`, so a heap read in a binding initializer keeps its SAT result inexact (`Unsupported`). Regression: `GetterReadInASubtypeInitializer_IsNotRefuted` (the reviewer's hiding-property witness), which failed before the change. Calor.Compiler.Tests 12628 and Calor.Verification.Tests 411 pass (the G3 oracle included). The PR has 591 non-test lines. |
| Residual (not re-reviewed; review budget exhausted) | A `§PROOF` condition that reads a property hiding an inherited field is still translated as the inherited field, a pre-existing `ContractTranslator` registry behavior shared by every verifier channel. Its SAT model can then be a spurious refutation (a compile error), but never a false Discharged, because facts use the global heap rule. This is recorded in the disposition record and left to the maintainer. |
