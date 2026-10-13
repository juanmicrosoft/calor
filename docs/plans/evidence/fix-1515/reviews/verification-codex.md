# Fix #1515 (named refinement arithmetic): Codex verification pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), asked to
verify the round-2 fixes and look for new issues. Reviewed commit `5625a438`. (A first attempt
stopped at the Codex usage limit without a verdict; this is the retry after the reset.)

## Dispositions

| # | Finding | Disposition |
|---|---------|-------------|
| 1 | Inc/dec on a collection element (`(dec §IDX a INT:0)` on `Dictionary<i32,Nat>`) kept the refined type, but no guard covers non-variable operands | **Fixed.** The refined result is kept only when the operand is a `ReferenceNode`, the one shape the emitter and `ObligationGenerator` guard. Any other operand gets the base type. Test `IncrementDecrement_OfUnguardedElement_GetsTheBaseType` uses the reviewer's program. An unannotated local (`§B{~y} a`) is still typed refined without a guard; that is round-1 finding 1, pre-existing, and inc/dec on it is no worse than `y` itself. |

The reviewer confirmed: the decimal fallback is correct; all four scalar mutation forms keep guards and obligations for refined parameters and annotated mutables; the assignment tests reach the changed typing code; there is no Z3 skip; `check_test_quality.py` passes.

Review rounds are capped at 3 plus verification, so this fix was not sent to another Codex round. It is a narrowing (fewer programs typed refined), covered by the new test and the full compiler suite.

## Review output (verbatim)

1. **major — Inc/dec retain refinements for unguarded collection elements.**  
   `src/Calor.Compiler/TypeChecking/TypeChecker.cs:1465`

   Reproduction:

   ```calor
   §M{m:R}
     §U{Nat:System.Int32}
     §RTYPE{r1:Nat:i32} (>= # INT:0)
     §F{f1:Bad:pub} (Dictionary<i32,Nat>:a) -> Option<Nat>
       §B{x} (dec §IDX a INT:0)
       §B{boxed:Option<Nat>} §SM x
       §R boxed
   ```

   This compiles and emits `var x = checked(--a[0]);`, with no refinement guard or subtype obligation. Starting with `a[0] = 0`, it returns `Some(-1)` typed as `Option<Nat>`.

   Both protection paths require a `ReferenceNode` operand (`CSharpEmitter.cs:4016`, `ObligationGenerator.cs:337`). The new typing rule also preserves refinements for modeled collection elements. Before this change, the result was `i32` in round 2—or `ErrorType` on main—and the `Option<Nat>` binding was rejected. This newly permits an invalid refinement at initialization, beyond the dispositioned later-assignment gap.

   Preserve the refined result only when mutation protection applies; otherwise return the erased base type.

The decimal fallback is correct. All four scalar mutation forms retain guards and obligations for refined parameters and annotated mutables. The strengthened assignment tests reach the changed typing code; no Z3 skip remains, and `check_test_quality.py` passes. Targeted compilation probes succeeded; VSTest was blocked by sandbox socket permissions.

REQUEST CHANGES
