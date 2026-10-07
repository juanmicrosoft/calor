# Fix #1515 (named refinement arithmetic): Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial
prompt, diff `origin/main...HEAD` on stdin, round-1 record in the repo. Reviewed commit `27341a09`.

## Dispositions

| # | Finding | Disposition |
|---|---------|-------------|
| 1 | Decimal rule fires against an unmodeled operand (`decimal + Box` with a user-defined operator returning `f64`) | **Fixed.** `decimal` is inferred only when both operands are modeled; otherwise the result is `ErrorType` (defer to Roslyn). Test `DecimalWithUnmodeledOperand_DefersToUserDefinedOperator` uses the reviewer's program. |
| 2 | `§B{x:Nat} (inc a)` regressed (was accepted via the error type) | **Fixed.** Increment/decrement keep the operand's declared (refined) type. Sound: the write-back into the refined variable is itself guarded (Subtype obligation + runtime check), and the `§B{x:Nat}` bind gets its own Subtype obligation and guard regardless. Theory `IncrementDecrement_OfNamedRefinement_KeepsTheRefinedType` covers `inc`, `dec`, `post-inc`, `post-dec`. |
| 3 | New `Skip.IfNot(... "Z3 not available")` breaks `scripts/check_test_quality.py` skip inventory | **Fixed.** Test is a plain `[Fact]`; its assertions (never `Discharged`, guards emitted, named == inline) hold with or without Z3. `check_test_quality.py` passes. |
| 4 | Assignment guard/obligation tests insensitive to the fix (assignment RHS not type-checked) | **Fixed.** Both tests now compute the value in a typed bind `§B{d:i32} (- a INT:5)` and assign `d`; that bind is rejected with Calor0202 before the fix. |

## Review output (verbatim)

1. **major — The decimal rule rejects valid overloaded arithmetic.**  
   `src/Calor.Compiler/TypeChecking/TypeChecker.cs:4094`

   ```calor
   §M{m:R}
     §CL{c1:Box:pub}
       §PROP{p1:Value:Box:pub}
         §GET
           §R this
       §OP{op1:+:pub}
         §I{decimal:d}
         §I{Box:b}
         §O{f64}
         §R FLOAT:2.0
     §F{f1:Good:pub} (decimal:d,Box:b) -> f64
       §B{x:f64} (+ (+ d b.Value) FLOAT:1.0)
       §R x
   ```

   This passes the pre-fix checker and generated-C# validation. After the fix, it reports `Calor0202: Cannot mix decimal and f64`. The checker represents `b.Value` as `ErrorType`, but the new rule nevertheless declares the inner result `decimal`. C# selects `Box.operator +(decimal, Box)`, whose result is `double`.

   Only infer `decimal` when both operands have known built-in numeric types; otherwise preserve the unmodeled result and defer to Roslyn.

2. **major — Erasing increment/decrement results breaks previously valid, guarded programs.**  
   `src/Calor.Compiler/TypeChecking/TypeChecker.cs:1452`

   ```calor
   §M{m:R}
     §RTYPE{r1:Nat:i32} (>= # INT:0)
     §F{f1:Good:pub} (Nat:a) -> i32
       §B{x:Nat} (inc a)
       §R x
   ```

   This previously passed type checking and generated-C# validation. It now fails with `Cannot assign i32 to variable of type i32{#i32}`. Replacing `inc` with `dec`, `post-inc`, or `post-dec` produces the same regression.

   Unlike unrestricted subtraction, these mutation expressions already enforce the operand’s refinement before committing the mutation (`CSharpEmitter.cs:4012`). Their results can safely satisfy `Nat`. Preserve compatibility for guarded mutation results; the new tests only exercise decrement as a standalone statement.

3. **major — The new runtime skip fails the required test-quality gate.**  
   `tests/Calor.Compiler.Tests/Analysis/NamedRefinementArithmeticTests.cs:496`; `eng/test-manifest.json:160`

   Reproduction:

   ```text
   python3 scripts/check_test_quality.py
   ERROR: runtime skip site count changed for 'Z3 not available': expected 397, got 398
   ```

   The diff updates the test total but leaves the runtime-skip allowance unchanged. This check runs in `.github/workflows/test.yml`. Update the allowance or remove the new conditional skip.

4. **minor — The strengthened assignment tests remain insensitive to the operator fix.**  
   `tests/Calor.Compiler.Tests/Analysis/NamedRefinementArithmeticTests.cs:454`, `:494`

   Both `Emit_NamedAndInlineRefinement_GetTheSameAssignmentGuardShape` and `Verify_AssignmentObligationOnArithmeticResult_MatchesInlineRefinement` still pass with the pre-fix checker. `CheckStatement` does not inspect assignment RHS expressions, so their arithmetic never reaches the changed typing code. The added status and guard assertions verify existing protection, but do not establish integration with newly accepted arithmetic.

   Add a typed binding initialized by named-refinement arithmetic before the assignment, so removing binary erasure makes these cases fail.

All 113 supplied cases passed when invoked from the existing test assembly. Known decimal/int arithmetic, decimal unary minus, and `f32` arithmetic behaved correctly. I found no new bypass of explicit scalar assignment or return guards; round-1 findings 1 and 3 remain pre-existing.

REQUEST CHANGES
