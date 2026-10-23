# S2 #1413 fix-obligation-residuals — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review at `e6260b1b` against the updated stacked base `1706c8b4`; no `dotnet` execution. The witnesses below are source-derived, not executed results.

Compiler filenames are relative to `src/Calor.Compiler/`; test filenames are relative to `tests/Calor.Compiler.Tests/`.

Verification of earlier findings:

| Earlier finding | Current disposition and evidence |
|---|---|
| R1-1 / R2-1: inserted refinement guards, including unannotated rebindings | **Original witnesses resolved.** Refined parameters and bindings populate `_refinedNames`; binding and assignment checks consult it (`FactCollector.cs:156–162`, `310–311`). |
| R1-2 / R2-2: overloaded operators and migrated `op_*` methods | **Specified representations resolved.** Both declaration forms activate the conservative throwing flag (`FactCollector.cs:339–341`, `303`; `ObligationSolver.cs:354`, `382`). Raw member declarations remain uncovered; finding 5. |
| R2-3: implicit conversions | **Original witness covered conservatively.** With a detected overload/conversion, bindings and references count as throwing (`FactCollector.cs:301–310`). |
| R1-3: nested properties | **Resolved.** Property discovery recursively traverses the module (`FactCollector.cs:335–336`, `549–555`). |
| R1-4: entry obligations bypass property checking | **Resolved for recognized property-read forms.** The check is outside `!isEntry` (`ObligationSolver.cs:300`, `320–322`). Another read representation escapes it; finding 4. |
| R1-5 / R2-4: same-statement operands and folded spans | **Both original witnesses resolved.** Throwing expression spans are collected, and overlapping/enclosing spans count (`FactCollector.cs:165–166`, `329–332`; `ObligationSolver.cs:315–316`). |
| R2-5: throwing preconditions | **Body witness resolved; entry coverage incomplete.** The check remains inside `!isEntry` (`ObligationSolver.cs:300`, `317–318`); finding 1. |
| R1-6a / R2-6: later elseif conditions demote earlier bodies | **Resolved.** Both state-change and throwing checks follow condition order (`FactCollector.cs:364–375`). |
| R1-6b: global property-name matching | **Retained, as acknowledged.** Matching remains global (`FactCollector.cs:348–352`), and the limitation is documented (`CHANGELOG.md:65–67`). |
| R1-7 / R2-7: weak status and production assertions | **Exact property statuses corrected.** Entry and nested cases use `AssertWithheld` (`S2ObligationResidualTests.cs:86`, `217`; helper `37–40`). The hiding-property case checks diagnostics and emitted code (`63–65`). New fixture weaknesses remain; finding 6. |
| R2-8: failed-proof diagnostic code | **Resolved.** `CHANGELOG.md:48` correctly distinguishes `Calor1140` and `Calor1121`. |

The manifest increase from 12643 to 12660 matches seventeen new `[Fact]` cases.

1. **MAJOR — Throwing preconditions still permit unreachable counterexamples for entry refinements.**

   `ObligationSolver.cs:317–318` checks throwing preconditions only within `status == SATISFIABLE && !isEntry` (`300`). Entry queries nevertheless assert those preconditions (`168–179`).

   Move the round 2 precondition witness’s claim into a private parameter refinement:

   ```calor
   §M{m1:M}
     §F{f1:Probe:priv}
       §I{i32:x} | (< # INT:2147483647)
       §O{void}
       §E{}
       §Q (|| (== x INT:0) (== (+ x INT:1) INT:-2147483648))
   ```

   The modeled precondition admits `x = 2147483647`, which violates the entry refinement. Runtime evaluation overflows in the precondition before reaching that refinement guard. Only `x = 0` completes the precondition, and it satisfies the refinement.

   Preconditions emit before parameter-refinement guards (`CSharpEmitter.cs:2917–2923`). Private entry obligations remain solver candidates (`ObligationGenerator.cs:440–463`). This query therefore still permits Failed with an unreachable model.

   Apply precondition reachability to entry obligations too, independently of body-path exactness.

2. **MAJOR — Constructor initializers execute before proofs but are absent from throwing analysis.**

   Constructor collection passes only parameters and `constructor.Body` to the collector (`ObligationSolver.cs:463–469`). Parsing stores the initializer separately (`Parsing/Parser.cs:11198`, `11214–11217`, `11247`).

   For a base constructor accepting an integer, use a derived constructor with:

   ```calor
   §CTOR{ct1:pub}
     §I{i32:x}
     §BASE §A (/ INT:10 x)
     §PROOF{p1:claim} (!= x INT:0)
   ```

   The emitter places the division in `: base(...)` (`CSharpEmitter.cs:6682–6686`, `6723–6729`), before emitting body statements (`6740–6742`). At `x = 0`, initialization throws before the proof. The collector sees a body containing only the proof, marks it exact, and permits Failed at zero.

   Include initializer argument evaluation and the invoked base/this constructor in constructor reachability.

3. **MAJOR — The new array/string whitelist overlooks default-null locals.**

   `FactCollector.cs:315–319` now classifies array lengths and several string operations as non-throwing because “solver models never hold null.” That does not establish anything about locals whose values the query never models.

   Using the same `typeCheck: false` compilation mode already exercised by this suite:

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub} (i32:x) -> void
       §E{}
       §B{items:i32[]}
       §B{length:i32} §LEN items
       §PROOF{p1:claim} (> x INT:0)
   ```

   An initializer-free binding emits `int[] items = default;` (`CSharpEmitter.cs:3922–3927`). `§LEN items` emits `items.Length` (`5180–5183`), which throws before every execution of the proof.

   Both bindings nevertheless count as non-throwing (`FactCollector.cs:304`, `310`, `315`). The query models only parameter `x` (`ObligationSolver.cs:83–100`), so it can report Failed for `x <= 0` despite no execution reaching the proof.

   The analogous string case uses a default-null local as the receiver—or as the argument to `Contains`/`StartsWith`. Retain throwing classification unless receiver and argument safety are actually established.

4. **MAJOR — `§LEN` property reads bypass property demotion entirely.**

   `ReadsProperty` recognizes only `FieldAccessNode` and dotted `ReferenceNode` (`FactCollector.cs:348–352`). However, `ArrayLengthNode` emits an ordinary `.Length` member access (`CSharpEmitter.cs:5180–5183`).

   With the suite’s `typeCheck: false` mode:

   ```calor
   §M{m1:M}
     §CL{c1:Box:pub}
       §PROP{pr1:Length:i32:pub}
         §GET
           §R INT:0
     §F{f1:Probe:pub} (Box:box) -> void
       §E{}
       §PROOF{p1:claim} (== §LEN box INT:0)
   ```

   The emitted getter always returns zero. Nevertheless, `TranslateArrayLength` creates a free `box$length` variable without checking the receiver’s type or resolving its member (`Verification/Z3/ContractTranslator.cs:1968–1984`). The solver can choose a nonzero length.

   Although `_propertyNames` contains `Length`, this expression contains neither recognized read form. The proof remains eligible for Failed. Recognize the emitted member behind `§LEN`, or refuse translating it as array length without establishing an array receiver.

5. **MAJOR — Raw C# member declarations bypass both declaration scans.**

   Class-level `§CSHARP` members are supported and retained separately (`Parsing/Parser.cs:9361–9365`), then emitted verbatim (`CSharpEmitter.cs:5818–5821`, `9107–9111`).

   In the supplied property-hiding witness, replace `Box`’s native property declaration with:

   ```text
   §CSHARP{public new int Trigger => 0;}§/CSHARP
   ```

   `PropertyNames` sees no `PropertyNode` (`FactCollector.cs:335–336`). The translator still copies `Base.Trigger` into Box’s field registry (`Verification/Z3/ContractTranslator.cs:243–249`). Thus the original fabricated-field counterexample remains eligible for Failed even though the emitted getter always returns zero.

   Raw operator/conversion declarations have the corresponding omission: `OverloadsOperators` recognizes only `OperatorOverloadNode` and `MethodNode` (`FactCollector.cs:339–341`). Valid paired raw comparison operators containing `10 / right` reproduce the earlier throwing-comparison witness.

   Account for opaque member declarations when deciding whether field modeling and operator classification are exact.

6. **MINOR — Two new regression fixtures do not exercise the reported throwing behavior.**

   The conversion fixture declares a conversion from `Box`, but its predecessor is `§B{q:i32} x`; `box` is unused (`S2ObligationResidualTests.cs:246–254`). No conversion executes. It tests the documented global over-demotion.

   The migrated-operator fixture similarly compares primitive `x` (`271–274`). Its `op_LessThan` declaration has one parameter and no static modifier (`267–270`), producing an invalid binary operator declaration through the emitter (`CSharpEmitter.cs:6050–6058`, `6280–6282`).

   Keep these as scan controls if desired, but add valid fixtures that actually invoke the throwing conversion and paired comparison operators. Check emitted-code validity and the relevant production diagnostics.

7. **MINOR — The ordered guard fix can create new Discharged outcomes, contradicting the CHANGELOG.**

   `CHANGELOG.md:67` says, “No obligation becomes proven or discharged.” The elseif repair changes which facts are asserted, not merely SAT classification.

   Change the existing later-call control’s proof to match its initial guard:

   ```calor
   §IF{if1} (> x INT:0)
     §PROOF{p1:claim} (> x INT:0)
   §EI (> §C{Math.Abs} §A x §/C INT:1)
     §R INT:2
   ```

   Previously, the later call made `negationsUsable` false and prevented adding the initial guard. Now `UsableUpTo(1)` adds it (`FactCollector.cs:364–369`). The solver asserts that fact (`ObligationSolver.cs:194–202`), making its conjunction with the negated proof UNSAT and permitting Discharged (`252–257`, `335–336`).

   This is a legitimate restored proof, not a false discharge. Narrow the documentation claim to the demotion checks and add a discharge control covering the changed assumption handling. I found no new false Discharged outcome.

## Response

| Finding | Disposition |
|---|---|
| 1 MAJOR throwing preconditions for entry obligations | Fixed. The precondition and entry-guard throwing check applies to every SAT result, entry obligations included. Regression: `EntryRefinementAfterAThrowingPrecondition_IsWithheld` (your witness). |
| 2 MAJOR constructor initializers | Fixed. A constructor with a `§BASE`/`§THIS` initializer marks its entry as possibly throwing, so a SAT result in its body is inexact. Regression: `ProofAfterAThrowingConstructorInitializer_IsWithheld` (`§BASE §A (/ 10 x)`). |
| 3 MAJOR the array/string whitelist and default-null locals | Fixed. `§LEN` and the null-only string queries count as non-throwing only when every operand is a parameter, a literal, or `#`, which the solver models and which is never null in a model. A local may be default null, so over a local they count as throwing. Regression: `ProofAfterALengthOfADefaultNullLocal_IsWithheld` (your witness). |
| 4 MAJOR `§LEN` property reads | Fixed. An `ArrayLengthNode` counts as a property read when the module declares a `Length` property. Regression: `ProofReadingALengthProperty_IsWithheld`. |
| 5 MAJOR raw C# member declarations | Fixed conservatively. A module with raw C# type members (`InteropBlocks` on a class or interface) is treated like one with operator overloads: every non-literal node may throw, and every member read counts as a property read. Regression: `ProofWithRawCSharpMembersInTheModule_IsWithheld` (your `§CSHARP{public new int Trigger => 0;}` witness). |
| 6 MINOR fixtures do not invoke the user code | Fixed. The conversion fixture binds `§B{q:i32} box`, which invokes the conversion, and reads `box.Value`. The `op_*` fixture declares valid static `op_LessThan`/`op_GreaterThan(Box, i32)` and compares `(< box x)`. |
| 7 MINOR ordered guards can discharge | Accepted, and documented. The CHANGELOG now says these checks never make an obligation proven or discharged. It also says that ordering the state-change check restores an `if` body's guard fact, so a claim the guard implies is discharged, as it should be. Control: `Control_ThenBodyGuardStillDischargesTheSameClaim`. Both before and after this round it is Discharged on the current branch; you confirmed no false Discharged. |
| Tests and rounds | `S2ObligationResidualTests`: 23 cases. 5 of the round-3 witnesses failed before this round. Full Compiler and Verification suites pass. Round 3 was the last allowed review round (contract 1.3.0 exception A: at most 3). One verification-only pass follows. If it asks for changes, stopping rule 1 applies. |
