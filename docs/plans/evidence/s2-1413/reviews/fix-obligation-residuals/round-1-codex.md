# S2 #1413 fix-obligation-residuals — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review against stacked base `674e3bdf`; no `dotnet` execution. The following residual witnesses are derived from the source paths, not executed results.

1. **MAJOR — Refinement guards inserted by code generation remain invisible to `MayThrow`.**

   `FactCollector.cs:275–280` treats a plain reference initializer, binding, or assignment as non-throwing. It never resolves the binding’s named refinement predicate. However, `CSharpEmitter.cs:1372–1383` inserts a guard after a refined binding, and `CSharpEmitter.cs:6192–6194` makes that guard throw.

   With an ordinary `i32:x` parameter, this body still marks the proof exact:

   ```calor
   §RTYPE{r1:NZ:i32} (> §C{Math.Abs} §A # §/C INT:0)
   ```
   ```calor
   §B{q:NZ} x
   §PROOF{p1:claim} (!= x INT:0)
   ```

   The subtype obligation becomes Unsupported because calls are untranslated (`ContractTranslator.cs:471`). Its retained guard rejects `x = 0`, but the later proof can still report that unreachable input as Failed. Refined assignments have the same omission (`CSharpEmitter.cs:6834–6846`, `6934–6937`). The throwing analysis must include compiler-inserted guards.

2. **MAJOR — Comparisons are not necessarily non-throwing: overloaded operators execute arbitrary code.**

   `FactCollector.cs:277–278` classifies every comparison as non-throwing, without checking operand types or operator resolution. The emitter preserves the C# operator (`CSharpEmitter.cs:3990–3998`) and supports user-defined operators (`CSharpEmitter.cs:6775–6782`).

   Define paired `Box < i32` / `Box > i32` operators, with `<` computing `10 / right > 0`. Then:

   ```calor
   §B{q:bool} (< box x)
   §PROOF{p1:claim} (!= x INT:0)
   ```

   `MayThrow` returns false for the binding’s entire AST. The proof remains exact and can report Failed at `x = 0`, although the overloaded comparison throws first. Opcode-only classification is insufficient for comparisons and other overloadable operators.

3. **MAJOR — Nested-class properties bypass the property demotion.**

   `ObligationSolver.cs:34–37` collects only immediate `module.Classes` and `module.Interfaces` properties. By contrast, the translator recursively registers nested classes (`ContractTranslator.cs:349–361`) and copies inherited fields into their registries (`243–249`).

   Move `Box` from the supplied property-hiding witness inside an `Outer` class and change the function parameter to `Outer.Box`. The translator still models inherited `Base.Trigger`, but `_propertyNames` contains no `Trigger`. The proof can therefore remain Failed with a fabricated inherited-field value even though the nested getter always returns zero.

   Property discovery must traverse the same declaration graph as field registration.

4. **MAJOR — `RefinementEntry` obligations skip the property check entirely.**

   The new check at `ObligationSolver.cs:318–319` is inside `status == SATISFIABLE && !isEntry` (`303`). Private parameter refinements remain pending entry obligations (`ObligationGenerator.cs:440–463`), so they reach the solver without this protection.

   Reuse the supplied top-level `Base` / `Box` declarations, but replace the proof with a private parameter refinement:

   ```calor
   §F{f1:Probe:priv}
     §I{Box:box} | (== box.Trigger INT:0)
     §O{void}
     §E{}
   ```

   The getter always returns zero. Nevertheless, the inherited-field model can refute the entry predicate, and the new name check is bypassed. Property-model exactness must be checked independently of body-path exactness.

5. **MAJOR — An implicitly throwing predecessor inside the same return expression is still missed.**

   `FactCollector.cs:252–260` records the whole simple statement as exact **before** considering whether it throws. That makes every obligation within a return expression exact, regardless of earlier operand evaluation. Index obligations retain the individual access span (`ObligationGenerator.cs:637–642`) and model only their bounds condition.

   For a private function taking `Sized:items`, `i32:n`, and `i32:i`, with `§ITYPE{it1:Sized:i32[]:n}`:

   ```calor
   §Q (== n INT:1)
   §Q (&& (>= i INT:0) (<= i INT:1))
   §R (+ (/ INT:10 (- i INT:1)) §IDX items i)
   ```

   The bounds query’s violating model is `i = 1, n = 1`. Runtime evaluation throws at the left-hand division before evaluating the indexed access; `i = 0` reaches an in-bounds access. The solver nevertheless has an exact access span and no inexactness reason. Statement-level tracking must account for evaluation order within expressions.

6. **MINOR — Two new demotions suppress legitimate refutations for unrelated reasons.**

   - `FactCollector.cs:235–236` lets **any later** throwing `elseif` condition make the initial then-body inexact. For `if x > 0`, then proof `x > 1`, followed by a dividing `elseif` guard, `x = 1` genuinely reaches and violates the proof. The later guard is never evaluated.
   - `ObligationSolver.cs:34–37` and `FactCollector.cs:289–293` match names globally. Adding an unrelated class with a property named `Value` demotes the supplied ordinary `Box.Value` field control, despite that field having no getter.

   Apply throwing conditions in evaluation order and identify properties by receiver/member resolution.

7. **MINOR — The tests do not establish the promised visible demotion and guard retention.**

   `S2ObligationResidualTests.cs:26–27` discards compilation diagnostics and generated code. Assertions at `52–53`, `71–72`, and `88` accept statuses other than Unsupported; the third even accepts Discharged. None checks `Calor1124`, absence of a counterexample, or retained guard emission.

   Moreover, the earlier-proof fixture’s `p1` remains a genuine Failed obligation, so that fixture does not demonstrate successful compilation with a retained earlier guard. Assert the exact outcome and production diagnostics/output, and add the residual witnesses above.

The manifest’s `+5` matches five new `[Fact]` cases. I found no new false Discharged or changed UNSAT handling: the new exactness and property checks affect SAT classification only. However, `CHANGELOG.md:47–55` overstates coverage given the remaining throwing-guard and property bypasses.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR compiler-inserted refinement guards | Fixed. A binding whose type is a refinement type counts as possibly throwing, because its guard can throw. So does an assignment to a refined name (a parameter with an inline or named refinement, or a refined binding). Regression: `ProofAfterARefinedBindingGuard_IsWithheld`. |
| 2 MAJOR overloaded operators | Fixed. When the module declares any operator overload, every binary and unary operator counts as possibly throwing (`OperatorsMayBeOverloaded`). Regression: `ComparisonsAfterOperatorOverloads_CountAsPossiblyThrowing`. |
| 3 MAJOR nested-class properties | Fixed. Property names are collected from the whole module tree (`FactCollector.PropertyNames`). Regression: `ProofReadingANestedClassProperty_IsWithheld` (your `Outer.Box` witness). |
| 4 MAJOR entry obligations skip the property check | Fixed. The property check applies to every SAT result, entry obligations included. Regression: `EntryRefinementReadingAProperty_IsNotRefuted`. |
| 5 MAJOR throwing operand in the same statement | Fixed. Each obligation inside a simple statement is checked against the possibly-throwing nodes that end before it starts (`ThrowsEarlierInStatement`). Operands evaluate left to right, and a node that encloses the obligation is evaluated after it. Synthetic zero-length spans are ignored. Regression: `ObligationAfterAThrowingOperandInTheSameStatement_IsWithheld` (your index witness). |
| 6a MINOR later elseif demotes the then-body | Fixed. Conditions apply in evaluation order. The then-body depends only on the `if` condition, each `elseif` body on the conditions up to its own, and the `else` body on all of them. Control: `ThenBodyIgnoresALaterThrowingElseIfCondition` stays Failed. |
| 6b MINOR global property-name matching | Retained, as documented over-demotion. A field that shares a name with any property in the module is also withheld. That is conservative: it only withholds a counterexample, keeps the guard, and never adds a claim. The CHANGELOG states it. Receiver resolution would need type information that the obligation solver does not have. |
| 7 MINOR tests | Fixed. Withheld cases assert exactly `Unsupported` with no counterexample, and the hiding-property case asserts that the runtime check is still emitted. `ProofAfterAnEarlierProofGuard` asserts that p2 is withheld. p1 is a genuine violation and stays Failed by design. |
| Tests | `S2ObligationResidualTests`: 11 cases. 6 of the new ones failed before this round. Full Compiler 12639 and Verification 411 pass, including the type-name ratchet and the G3 oracle. Non-test lines against #1496: 121. |
