# S2 #1413 fix-obligation-residuals — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review at `b0f532b3` against stacked base `674e3bdf`; no `dotnet` execution. Witnesses below are derived from the source paths.

Compiler filenames below are relative to `src/Calor.Compiler/`; test filenames are relative to `tests/Calor.Compiler.Tests/`.

Round 1 verification:

| Earlier finding | Current disposition and evidence |
|---|---|
| 1. Inserted refinement guards | Explicitly refined bindings and ordinary assignments are covered (`FactCollector.cs:156–162`, `300–301`). **Incomplete:** unannotated mutable rebindings still bypass the check; finding 1 below. |
| 2. Overloaded operators | `OperatorOverloadNode` declarations now make binary/unary operators possibly throwing (`FactCollector.cs:296–299`, `319–320`; `ObligationSolver.cs:352`, `380`). **Incomplete:** migrated operator declarations and implicit conversions remain uncovered; findings 2–3 below. |
| 3. Nested properties | **Resolved:** property discovery traverses the module recursively (`FactCollector.cs:316–317`, `529–535`). |
| 4. Entry property obligations | **Resolved:** the SAT property check is outside the `!isEntry` block (`ObligationSolver.cs:300`, `318–320`). |
| 5. Earlier operand in a return expression | The original distinct-span division witness is covered (`FactCollector.cs:311–313`; `ObligationSolver.cs:315–316`). **Incomplete:** overlapping spans from multi-argument operators defeat the repair; finding 4 below. |
| 6a. Later elseif demotion | The original dividing-elseif witness is repaired (`FactCollector.cs:345–361`). **Incomplete:** later calls still demote earlier bodies through `negationsUsable`; finding 6 below. |
| 6b. Global property-name matching | **Retained, as acknowledged:** matching remains global (`FactCollector.cs:327–331`), with the limitation documented (`CHANGELOG.md:58–59`). |
| 7. Tests | **Partially resolved:** `AssertWithheld` checks exact Unsupported and no counterexample (`S2ObligationResidualTests.cs:36–39`), and one case checks emitted code (`62`). Entry/nested tests and diagnostic coverage remain weaker than claimed; finding 7 below. |

1. **MAJOR — Unannotated mutable rebindings still hide an inserted refinement guard.**

   `FactCollector.cs:300` checks only the binding’s explicit `TypeName`, even though `_refinedNames` already includes refined parameters. The emitter instead retrieves the existing constraint for a mutable rebind (`CSharpEmitter.cs:3836–3838`) and emits its throwing guard even without a type annotation (`3876–3892`, `6934–6937`).

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub}
       §I{i32:q} | (!= # INT:0)
       §I{i32:x}
       §O{i32}
       §E{}
       §B{~q} x
       §PROOF{p1:claim} (!= x INT:0)
       §R q
   ```

   The rebind counts as non-throwing. Writing `q` drops its entry fact, but neither that dropped fact nor the assignment constrains the proof’s separate variable `x`. The proof therefore remains exact and permits Failed at `x = 0`, although the inserted rebind guard throws first. Parameter rebinding is supported (`Binding/Binder.cs:2930–2933`).

   Include mutable bindings to already-refined names in the throwing analysis.

2. **MAJOR — The overload scan misses the representation emitted by C# migration.**

   `FactCollector.cs:319–320` recognizes only `OperatorOverloadNode`. The migration visitor creates a **`MethodNode`** with a CIL operator name instead (`Migration/RoslynSyntaxVisitor.cs:3955–3983`). Those methods emit actual C# operators (`CSharpEmitter.cs:6050–6058`, `6280–6282`).

   Represent the round 1 paired `Box < i32` / `Box > i32` witness as `§MT{...:op_LessThan:pub:stat}` and `§MT{...:op_GreaterThan:pub:stat}`, with `<` evaluating `10 / right`. Then:

   ```calor
   §B{q:bool} (< box x)
   §PROOF{p1:claim} (!= x INT:0)
   ```

   With no `OperatorOverloadNode`, `OperatorsMayBeOverloaded` remains false. The comparison predecessor remains non-throwing under `FactCollector.cs:296–298`, permitting Failed at `x = 0` despite the operator throwing.

   Scan every declaration representation that the emitter turns into an operator.

3. **MAJOR — Implicit conversions remain non-throwing even when an operator overload is detected.**

   The overload flag affects only binary and unary nodes (`FactCollector.cs:296–299`). An ordinary binding with a plain-reference initializer still counts as non-throwing (`294`, `300`).

   Define a `Box` field `Value` and an implicit conversion to `i32` returning `10 / value.Value`. The emitter supports this conversion declaration (`CSharpEmitter.cs:6775–6778`). Then use:

   ```calor
   §B{q:i32} box
   §PROOF{p1:claim} (!= box.Value INT:0)
   ```

   The binding emits `int q = box;` (`CSharpEmitter.cs:3916–3919`), which invokes the conversion. Its AST contains no call or arithmetic node. The proof can consequently remain Failed with modeled `box.Value = 0`, although conversion throws before it. This path can be exercised with the existing helper’s type-checking-off mode if necessary; the emitted C# conversion is valid.

   Throwing analysis must account for implicit conversions at receiving boundaries.

4. **MAJOR — Multi-argument operators defeat the new source-span ordering check.**

   The parser folds `(+ a b c)` into nested binary operations but assigns **every intermediate operation the entire expression’s span** (`Parsing/Parser.cs:3722–3726`). `ThrowsEarlierInStatement` recognizes a predecessor only when its span ends before the access starts (`FactCollector.cs:311–313`).

   In the existing sized-array witness, retain `n = 1` and `0 <= i <= 1`, but replace the return with:

   ```calor
   §R (+ INT:2147483647 i §IDX items i)
   ```

   At the violating model `i = 1`, the intermediate `2147483647 + i` throws before evaluating the indexed access. The emitter generates checked additions (`CSharpEmitter.cs:3947–3952`). However, that intermediate addition’s span encloses the access, so the new check misses it.

   The bounds condition contains only comparisons (`ObligationGenerator.cs:628–642`); arithmetic safety examines that condition alone (`ObligationSolver.cs:235–244`). Nothing else excludes this unreachable model from Failed.

   Determine evaluation order structurally, or give intermediate operations accurate spans.

5. **MAJOR — Throwing precondition guards are still absent from body reachability.**

   Preconditions are asserted whenever translation succeeds (`ObligationSolver.cs:175–179`), but body traversal starts exact without inspecting their throwing behavior (`FactCollector.cs:196`). Arithmetic safety covers only the obligation condition (`ObligationSolver.cs:235–236`).

   With `ContractMode.Debug` and default checked arithmetic:

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub} (i32:x) -> i32
       §E{}
       §Q (|| (== x INT:0) (== (+ x INT:1) INT:-2147483648))
       §PROOF{p1:claim} (< x INT:2147483647)
       §R x
   ```

   Bit-vector addition makes `x = 2147483647` satisfy the modeled precondition and violate the proof (`Verification/Z3/ContractTranslator.cs:621–622`). Runtime evaluation overflows in the precondition before entering the body. Only `x = 0` completes the precondition successfully, and it satisfies the proof.

   Preconditions execute before the body (`CSharpEmitter.cs:2917–2923`) and retain their runtime guards (`4987–5005`). Their implicit throws must therefore participate in SAT reachability.

6. **MINOR — A later elseif call still suppresses a legitimate then-body refutation.**

   Although `MayThrow` now follows condition order, `negationsUsable` still examines **all** conditions (`FactCollector.cs:337–345`). `ChangesState` classifies every call as changing state (`401–403`).

   Replace the later dividing guard in `ThenBodyIgnoresALaterThrowingElseIfCondition` with:

   ```calor
   §EI (> §C{Math.Abs} §A x §/C INT:1)
   ```

   The initial then-body becomes inexact, even though `x = 1` reaches and violates its proof without evaluating that call. Apply the state-change check in evaluation order too.

7. **MINOR — The tests still do not establish every claimed visible demotion.**

   `EntryRefinementReadingAProperty_IsNotRefuted` accepts Discharged (`S2ObligationResidualTests.cs:84`). The nested-property test accepts any status except Failed/Discharged (`216–217`). Neither uses `AssertWithheld`, contrary to the author response and manifest wording.

   The helper still discards diagnostics (`28–30`), so no case asserts `Calor1124` or the absence of the erroneous failure diagnostic. Guard-emission coverage exists for only one witness (`62`).

   Use exact Unsupported assertions for both property cases and verify the relevant diagnostics and retained guards.

8. **NIT — The CHANGELOG gives the wrong diagnostic code for failed proofs.**

   `CHANGELOG.md:47–48` identifies failed proof/refinement obligations collectively as `Calor1121`. Proof failures use `Calor1140` (`Diagnostics/Diagnostic.cs:1104`; `Program.cs:976–977`).

The manifest’s `+11` matches eleven `[Fact]` cases. I found no newly introduced false Discharged or changed UNSAT handling; the added checks remain SAT-only. The throwing-coverage claims in `CHANGELOG.md:50–54` still exceed the implementation.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR unannotated mutable rebind of a refined name | Fixed. A binding of any name already refined (a parameter or binding), annotated or not, counts as possibly throwing. Regression: `ProofAfterAMutableRebindOfARefinedParameter_IsWithheld` (your witness). |
| 2 MAJOR migrated `op_*` methods | Fixed. `OverloadsOperators` also detects `MethodNode`s named `op_*`. Regression: `MigratedOperatorMethods_CountAsOperatorOverloads`. |
| 3 MAJOR implicit conversions | Fixed conservatively. When the module declares any operator or conversion, every non-literal node counts as possibly throwing, because names, bindings, and comparisons may all invoke user code. Regression: `ProofAfterAConversionWithAUserDefinedConversionInTheModule_IsWithheld`. |
| 4 MAJOR folded multi-operand spans defeat ordering | Fixed by giving up span ordering. Inside the obligation's statement, any possibly-throwing expression node outside the obligation's own span makes the result inexact. That includes an enclosing node evaluated afterwards, which is over-demotion and is documented. Statement-level nodes are excluded, because a binding's own guard is that statement's obligation. Regression: `ObligationInsideAFoldedMultiOperandOperation_IsWithheld` (your `(+ 2147483647 i §IDX items i)` witness). |
| 5 MAJOR throwing preconditions | Fixed. A precondition, or a parameter refinement (entry guard), that may throw makes every non-entry SAT result inexact. Regression: `ProofAfterAPreconditionThatMayThrow_IsWithheld` (your witness). `MayThrow` treats `#`, array lengths, and the string queries that cannot throw on a non-null value as non-throwing. Solver models never hold null, so a null receiver cannot make a model unreachable. Substring, padding, formatting, regexes, and `IndexOf` stay possibly throwing. |
| 6 MINOR later elseif call suppresses the then-body | Fixed. The state-change check follows evaluation order, like the throwing check. Control: `ThenBodyIgnoresALaterElseIfCall` stays Failed. |
| 7 MINOR tests | Fixed. Both property cases use `AssertWithheld` (exactly `Unsupported`, no counterexample). The hiding-property witness also asserts `Calor1124`, the absence of `Calor1140`, and that the runtime check is still emitted. |
| 8 NIT diagnostic code | Fixed. The CHANGELOG names `Calor1140` for `§PROOF` and `Calor1121` for refinements. It also states the over-demotion openly ("This can withhold counterexamples that were real"). |
| Contract (amendment 1.3.0) | The PR demotes only the two discoveries. Every existing obligation test keeps its outcome, including `Control_GenuineSubtypeViolation_StillRefuted` and `NestedSelfSubtypeObligation_IsTranslatedRatherThanUnsupported`, so no other finding is re-dispositioned. Each discovery has a witness that is Failed on #1496 and Unsupported with this PR. |
| Tests | `S2ObligationResidualTests`: 17 cases. 6 of the new ones failed before this round. Full Compiler 12660 and Verification 411 pass. Non-test lines against #1496: 162. This is round 2 of the 3 allowed. |
