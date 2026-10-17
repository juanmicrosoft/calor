# Codex review — round 1 (0.25 F3, #847)

- **Reviewer:** OpenAI Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family. Reduced independence; not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `0ca88e46` (generated evidence, `results.json` and the scorecard baseline excluded), with the adversarial prompt (name resolution, captures, effect laundering, verifier soundness, silent rebinding, validation gaps).
- **Verdict:** REQUEST-CHANGES (5 BLOCKING, 2 NON-BLOCKING).

## Dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Emission qualified bare calls/method groups naming a local function to a same-named module function; also defeated the CS8421 capture backstop | **Fixed.** `CSharpEmitter.EmitCallableBody` keeps a set of names that must reach C# bare: the body's local functions and, inside a local body, every parameter/binding/loop name of the enclosing callable. `QualifyCrossModuleTarget` never qualifies them. Tests `Review1_LocalShadowingAModuleFunction_IsNotQualifiedAway`, `Review1_CaptureOfAnEnclosingName_IsNeverQualifiedToAModuleFunction`. |
| 2 | BLOCKING | A lambda calling a local function got a pure row (local calls charged `Empty`) | **Fixed.** `LocalFunctionUseCharge`: outside lambdas a use charges nothing (the body is charged at the declaration); inside a lambda a call, delegate invocation or method-group argument charges the union of the callable's local bodies (cached per inference, `Unknown` while one is still being inferred). Test `Review1_LambdaUsingALocalFunction_CarriesItsEffects` (with and without the lambda's `§E{}`). |
| 3 | BLOCKING | A same-named delegate field shadowed the local function in the row checker | **Fixed.** The row checker records member (field/property) entries separately; a local function shadows them, a parameter or binding shadows it. The local-function source row is consulted first. The inferrer's `IsLocalFunctionName` now treats only parameters/bindings as shadowing. Test `Review1_ASameNamedDelegateField_DoesNotShadowTheLocalFunctionRow`. |
| 4 | BLOCKING | A parameter shadowing a sibling local function fell through to class-member call lookup | **Fixed** in the binder for all callables: `GetCallLookupNames` yields nothing when the innermost declaration of a bare target is a local or parameter (C# simple-name lookup), so the call is a delegate invocation (row-less parameter: `Unknown`, fail closed). Test `Review1_AParameterShadowingASiblingLocal_IsInvokedAsAValue`. |
| 5 | BLOCKING | Local functions inside lambdas bypassed Calor0211 | **Fixed.** `WalkYields` (which enters expressions) validates any local function `Walk` did not see; such a function is misplaced. Test `Review1_LocalFunctionInsideALambda_IsCalor0211`. |
| 6 | NON-BLOCKING | Unresolved names (other partial-class files) were taken as no capture | **Fixed.** An unresolved simple name that is not a qualified member name counts as a possible capture; the member is preserved as `local-function`. Test `Review1_UnresolvedNames_AreNotProofOfNoCapture`. |
| 7 | NON-BLOCKING | `§F{f:L<T>}` bypassed the generic check and was emitted as `LT` | **Fixed.** Calor0211 rejects an embedded type-parameter spelling. Test `Review1_EmbeddedGenericLocalName_IsCalor0211`. |

Separately found during the same pass (not a Codex finding): `void M() => Log += s;` was converted to `Log = s` (pre-existing on main for methods); fixed in `ConvertMethodBody`, test `ExpressionBodiedCompoundAssignment_KeepsItsOperator`.

## Codex output (final answer, verbatim)

REQUEST-CHANGES

Reproduced using the existing Release MCP binary, with compilation `autoFix:false`.

1. **BLOCKING — Emission silently replaces local calls with module calls.** `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:2983` and `:3098`

   ```calor
   §M{m001:Shadow}
     §F{f001:Add:pub} (i32:x) -> i32
       §E{cw}
       §P "wrong"
       §R (+ x 1000)
     §CL{c001:Probe:pub:stat}
       §MT{m001:Run:pub:stat} () -> i32
         §E{}
         §R §C{Add} §A 2 §/C
         §F{f002:Add} (i32:x) -> i32
           §R (+ x 1)
   ```

   Compilation succeeds without diagnostics, but emits `return global::Shadow.ShadowModule.Add(2);`. `Run` prints and returns **1002**, although binding/effects selected the pure local returning **3**. Method-group references use the same faulty qualifier.

   **Fix:** Track local-function names throughout the enclosing callable and sibling bodies, including before declarations, and prevent qualification past their resolved identities.

   The same mechanism defeats the capture backstop: give `Run` an enclosing delegate parameter named `Add`, and let local `L` call `Add(1)`. Emission qualifies it to the module function, so C# never sees the forbidden capture.

2. **BLOCKING — Lambdas invoking local functions acquire a falsely pure row.** `src/Calor.Compiler/Effects/EffectEnforcementPass.cs:3423`

   ```calor
   §M{m001:Escape}
     §CL{c001:Probe:pub:stat}
       §MT{m001:Get:pub:stat} () -> Func<i32, i32> §E{}
         §E{cw}
         §F{f001:Help} (i32:x) -> i32
           §P "hidden"
           §R x
         §R §LAM{l001:x:i32} §E{} §C{Help} §A x §/C §/LAM{l001}
   ```

   Compilation succeeds without diagnostics. The returned “pure” lambda prints when invoked. Removing the lambda’s explicit `§E{}` also succeeds: its inferred body row is empty.

   Charging the local declaration to `Get` does not establish the escaping lambda’s row. The unconditional `EffectSet.Empty` answer loses that distinction.

   **Fix:** Include the local function’s transitive effects when computing lambda/delegate body rows. Apply this to indirect delegate invocations too; declaration charging may remain an enclosing-body over-approximation.

3. **BLOCKING — A same-named delegate field launders an escaping local function’s row.** `src/Calor.Compiler/Effects/EffectEnforcementPass.cs:2004` and `:2588`

   ```calor
   §M{m001:Escape}
     §CL{c001:Probe:pub:stat}
       §FLD{Func<i32, i32>:Help:priv:stat} §E{}
       §MT{m002:Get:pub:stat} () -> Func<i32, i32> §E{}
         §E{cw}
         §F{f001:Help} (i32:x) -> i32
           §P "hidden"
           §R x
         §R Help
   ```

   Compilation succeeds without `Calor0424`. C# returns the printing local function, but `SourceRow` reads the pure field from `_scope` first. `IsLocalFunctionName` also treats that field entry as shadowing the local function.

   **Fix:** Resolve value rows by bound declaration identity. At minimum, distinguish member entries from lexical parameters/bindings so local functions shadow fields and properties.

4. **BLOCKING — A local parameter shadowing a sibling function resolves to a class method.** `src/Calor.Compiler/Binding/Binder.cs:6086`

   ```csharp
   public class C
   {
       public static string Add(string x) => x;

       public static int M()
       {
           int Add(int x) => x;
           int Other(System.Func<int, int> Add) => Add(1);
           return Other(Add);
       }
   }
   ```

   The converted Calor fails with `Calor0208`: candidates are `C.Add(str)`. Inside `Other`, C# invokes the delegate parameter. `GetOverloads` correctly stops at that parameter, but the new lookup branch then falls through to class-member lookup.

   With a same-signature pure class method, compilation instead succeeds and effects are attributed to that method rather than the parameter’s unknown row.

   **Fix:** A lexical value must stop member/module call lookup as well as local-function lookup. Bind and analyze its invocation as a delegate call.

5. **BLOCKING — Local functions inside lambdas bypass placement and contract validation.** `src/Calor.Compiler/Analysis/ReturnValidationPass.cs:158`

   ```calor
   §M{m001:BadPlacement}
     §F{f001:Make:pub} () -> Func<i32, i32> §E{}
       §E{}
       §R §LAM{l001:x:i32} §E{}
         §F{f002:L} (i32:y) -> i32
           §E{}
           §Q (> y 0)
           §R y
         §R x
       §/LAM{l001}
   ```

   Compilation succeeds without diagnostics. The emitted lambda contains `static int L(int y)`, and its precondition is dropped. `Walk` uses `GetChildren`, which excludes expression subtrees; `WalkYields` reaches the declaration but never calls `CheckLocalFunction`.

   **Fix:** Traverse expression-contained statement bodies for local-function validation, maintaining separate return-owner contexts. This example must report `Calor0211`.

6. **NON-BLOCKING — Unresolved symbols are treated as evidence of no capture.** `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:4592`

   ```csharp
   // A.cs
   public partial class C { private int _f = 7; }

   // B.cs — converted independently
   public partial class C
   {
       public int M() { int L() => _f; return L(); }
   }
   ```

   Without the other semantic tree, `_f` has no resolved symbol. The converter produces a native nested `§F`, reports zero unsupported features, and fails later under `roundtrip-csharp-validation`; it never reports `local-function`. In the complete project, `L` captures `this`.

   **Fix:** Fail closed when an unresolved expression name could denote enclosing state, or require sufficient semantic context before declaring the function non-capturing. Preserve the member with a `local-function` report.

7. **NON-BLOCKING — Embedded generic syntax bypasses `Calor0211` and is silently erased.** `src/Calor.Compiler/Analysis/ReturnValidationPass.cs:106`

   ```calor
   §M{m001:Bad}
     §F{f001:Outer:pub} () -> i32
       §F{f002:L<T>} (i32:x) -> i32
         §R x
       §R 0
   ```

   Compilation succeeds and emits `static int LT(int x)`. Validation checks `TypeParameters`, but the supported embedded spelling leaves `<T>` in `Name`; local emission sanitizes it away.

   **Fix:** Normalize embedded type parameters before validation, or explicitly reject this spelling with `Calor0211`.
