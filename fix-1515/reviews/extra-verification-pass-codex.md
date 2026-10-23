# Fix #1515 (named refinement arithmetic): extra verification pass (Codex)

- **Exception:** `EXC-1520-EXTRA-VERIFICATION` in the R0 scope packet
  (`docs/plans/evidence/v0.25-r0-1426/scope.json`, `capacity.exceptions`, merged at `ed4bc1909`).
  It allows exactly one verification-only Codex pass and no further adversarial round.
- **Maintainer confirmation:** @juanmicrosoft confirmed this exception on 2026-10-12. The packet
  still records it as `confirmed: false`. This record cites the 2026-10-12 confirmation.
- **Reviewer:** Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`).
  This was the one allowed pass. No other Codex invocation was made on this PR under the exception.
- **Scope:** `e60773a08` ("Address #1515 verification pass: inc/dec of non-variables gets base
  type"). That commit was made after the pass recorded in `verification-codex.md`, and Codex had
  never reviewed it. Also in scope was any code in this PR's files that changed in the merge of
  `origin/main` (merge commit `24a4fecb7`). Main did not modify `TypeChecking/TypeChecker.cs` or
  `NamedRefinementArithmeticTests.cs`, so the merge added no code in scope. Codex was asked to check
  the fix against the emitter and obligation-generator guards at the merged HEAD.
- **Reviewed HEAD:** `24a4fecb7`, a merge of `origin/main` (`ed4bc1909`) into the branch. That merge
  commit failed `CandidateManifestTests`, which rejects merge commits in the candidate range. So
  the branch was rebased onto `ed4bc1909` instead. The rebased head `fcb5e229e` has a tree
  identical to `24a4fecb7` (an empty `git diff 24a4fecb7 fcb5e229e`), so the review applies to it
  unchanged. After the rebase, the commit that was `e60773a08` is `fcb5e229e`.

## Result

**VERDICT: APPROVE.** No BLOCKING, MAJOR, MINOR, or NIT findings. The recorded MAJOR (inc/dec on
an unguarded collection element kept the refined type) is **VERIFIED** fixed. The new test would
fail if the fix were reverted.

Codex restated one known gap that this fix does not change. An unannotated mutable local
(`§B{~y} a`) still keeps an inferred refinement without a guard. That is round-1 finding 1, which
was dispositioned as pre-existing. It is out of scope here.

## Review output (verbatim)

No new BLOCKING, MAJOR, MINOR, or NIT findings within the requested scope.

Recorded MAJOR: **VERIFIED** at HEAD `24a4fecb7`.

[TypeChecker.cs:1461](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/TypeChecking/TypeChecker.cs:1461) applies the narrowing to all four inc/dec operators:

| Operand | Result and protection at HEAD |
|---|---|
| Named-refined parameter | Keeps the refinement. The emitter checks the candidate before committing; the generator produces a mutation Subtype obligation when verification is enabled. |
| Explicitly annotated refined local | Same protection as a parameter. Postfix forms return the original value after validating the candidate mutation. |
| Inline-refined parameter | Typed as its base already; mutation still receives the runtime check and obligation. |
| `§IDX` collection element | Gets the erased base type. Any index-bounds check protects the index, not the element refinement. |
| Field/member access | A modeled `FieldAccessNode` result is erased. Dotted `ReferenceNode` member paths are unmodeled and infer `ErrorType`; they do not acquire a refinement through this exception. |
| `this.x` / `§THIS.x` | Unmodeled member access; no refined result is retained by this rule. |
| Parenthesized/wrapped reference | Parentheses around a valid nested expression preserve its AST shape. `(a)` is not ordinary reference grouping in this parser. An actual wrapper node, such as a cast, takes the non-reference branch and loses its refinement. |
| Unannotated mutable local, `§B{~y} a` | Still retains an inferred refinement without protection. This is the explicitly dispositioned round-1 gap, unchanged by this fix. |

Thus, `ReferenceNode` is **not a universal guarantee** that both protections apply. Within the recorded finding’s scope, however, the fix correctly removes refinement retention from unguarded non-variable operands. The protection paths remain at [CSharpEmitter.cs:4012](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/CodeGen/CSharpEmitter.cs:4012) and [ObligationGenerator.cs:331](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/Verification/Obligations/ObligationGenerator.cs:331).

The [new test:254](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/tests/Calor.Compiler.Tests/Analysis/NamedRefinementArithmeticTests.cs:254) exercises the fix. With it, decrement yields `i32`, `x` infers `i32`, and `§SM x` yields `Option<i32>`, producing the asserted mismatch against `Option<i32{#i32}>`. Reverting the narrowing makes `x` refined and the option types equal, removing that diagnostic and failing the assertion.

The merge leaves the checker, test, emitter, obligation generator, parser, AST walker, simplification pass, and compilation pipeline unchanged. Its binder changes affect bound-state bookkeeping, not the AST shapes used by these protections. The merged test manifest preserves main’s total plus this PR’s 119 cases.

This was static verification; I did not execute builds or tests under the no-file-writes restriction.

VERDICT: APPROVE
