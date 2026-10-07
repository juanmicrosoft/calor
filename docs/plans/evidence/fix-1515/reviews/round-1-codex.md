# Fix #1515 (named refinement arithmetic): Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial
prompt (refinement checks skipped on the result, wrong result types, verifier unsoundness, missed
operators, weak tests), diff `origin/main...HEAD` on stdin. Reviewed commit `2407781f`.

## Dispositions

| # | Finding | Disposition |
|---|---------|-------------|
| 1 | Unannotated `§B{~x} a` from a refined value gets no obligation or guard on later `§ASSIGN` | **Pre-existing, not changed here.** Reproducible on `origin/main` without arithmetic: `§B{~x} a` then `§ASSIGN x INT:-5` emits `x = -5;` unguarded. The emitter and `ObligationGenerator` track refinements by declared type name only; the type checker's inferred type never fed them. This PR does not widen it: an unannotated local was never a refined variable for the guard machinery. Reported as a follow-up in the PR body. |
| 2 | `decimal` arithmetic typed as `i32` | **Fixed.** `InferBinaryOperationType` returns `decimal` when either operand is `decimal` (after the float check). Test `DecimalRefinementArithmetic_IsDecimal` covers `Option<decimal>` via `§SM`, the narrowing error, and the nested `decimal`/`f64` mix. |
| 3 | Bitwise/shift on `f64` accepted; on `bool` rejected | **Pre-existing parity, not changed.** Identical behaviour for plain `f64`/`bool` operands on `origin/main`; this PR's contract is "a named refinement behaves as its base type". Roslyn validation still rejects `double & double`. |
| 4 | Width matrix asserted success with output validation off; `i16`/`u8` results returned as narrow types | **Fixed.** Test helper now keeps generated-output (Roslyn) validation on; matrix uses the C#-promoted result type (`i16`/`u8` -> `i32`). Refined-`char` unary minus keeping `char` is pre-existing behaviour for plain `char`. |
| 5 | Tests that pass without the fix | **Fixed.** Added `UnaryMinus_ResultIsTheBaseType_NotTheRefinementNorAnErrorType` (fails pre-fix: error type was assignable to `Nat`). Comparison test now compares arithmetic results. Obligation test asserts `n1` is not `Discharged` and that both named guards are emitted; `(dec x)` runtime guard added. Compound assignment has no Calor surface syntax (only produced by C# migration), so it is covered only by the existing generator code path. |

## Review output (verbatim)

1. **major — Inferred refined locals lose obligations and runtime guards.**  
   `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:3900`; `src/Calor.Compiler/Verification/Obligations/ObligationGenerator.cs:239`.

   ```calor
   §M{m:R}
     §RTYPE{r1:Nat:i32} (>= # INT:0)
     §F{f1:Bad:pub} (Nat:a) -> i32
       §B{~x} a
       §B{d:i32} (- x INT:5)
       §ASSIGN x d
       §R x
   ```

   The checker infers `x` as `RefinedType`, but both protection paths require an explicit binding annotation. Compilation succeeds and emits `x = d;` without a refinement guard; `Bad(0)` returns `-5`. The tracking gap predates this patch, but the newly accepted binary arithmetic exposes it. Changing the binding to `§B{~x:Nat} a` restores protection.

2. **major — Decimal arithmetic produces the wrong inferred type.**  
   `src/Calor.Compiler/TypeChecking/TypeChecker.cs:4090`, reached through the new erasure at `4029`.

   With `Money` refining `decimal` and parameters `Money:a, Money:b`:

   ```calor
   §B{boxed:Option<decimal>} §SM (+ a b)
   ```

   This valid expression fails with `Calor0202: Cannot assign Option<i32> to variable of type Option<decimal>`. After erasure, decimal arithmetic falls through to `PrimitiveType.Int`.

   Conversely, `§B{x:i32} (+ a b)` passes type checking and fails only during generated-C# validation. Nested `(+ (+ a b) f)` with `f:f64` also evades the decimal/float prohibition. Preserve `decimal` when either arithmetic operand is decimal.

3. **minor — Bitwise and shift operators inherit inappropriate arithmetic operand rules.**  
   `src/Calor.Compiler/TypeChecking/TypeChecker.cs:4075`.

   For `Pos` refining `f64`, `§R (& a b)` with two `Pos` parameters now passes type checking. Generated-C# validation subsequently reports CS0019 for `double & double`. Decimal operands and floating-point shift counts have the same problem.

   The opposite case remains broken: `(& a b)` with `Yes:bool` refinement operands produces `Calor0202`, although C# supports Boolean `&`, `|`, and `^`. These operators need their own operand and result rules.

4. **minor — The width matrix asserts success for programs that do not compile.**  
   `tests/Calor.Compiler.Tests/Analysis/NamedRefinementArithmeticTests.cs:28`, `:47`, `:62`.

   The helper disables generated-output validation. The `i16` and `u8` cases return arithmetic results directly into their narrow base type, although C# promotes those results to `int`. For example, the supplied `i16` addition case fails actual compilation with CS0266 (`int` → `short`).

   Validate representative emitted programs and use the promoted result types. Unary negation also needs promotion coverage: `TypeChecker.cs:1456` currently retains `char` for a refined-char operand, although C# unary minus returns `int`.

5. **minor — Several tests do not detect removal of the relevant fix.**  
   `tests/Calor.Compiler.Tests/Analysis/NamedRefinementArithmeticTests.cs:136`, `:154`, `:375`, `:415`.

   - Unary-minus tests pass without unary erasure: inference returns `ErrorType`, assignment accepts it, and surrounding arithmetic tolerates it. Assert the inferred result or require rejection of `§B{y:Nat} (- a)`.
   - Comparison tests already pass before this patch because comparisons return `bool` before checking operand families.
   - Assignment guard and obligation tests also pass without binary erasure because `CheckStatement` does not type-check assignment RHS expressions.
   - Obligation-status equality permits both sides to be `Unsupported` or `Pending`; it does not establish successful solving or guard retention. Check expected outcomes and emitted protection, including compound assignments and all increment/decrement forms.

REQUEST CHANGES
