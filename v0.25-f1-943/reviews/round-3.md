# F1 #943 — Codex adversarial review, round 3

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 3 of the budget, the last
  full round; only the verification-only pass remains.
- Input: `git diff origin/main...HEAD` at `7430607f`, with the round-1 and round-2 records. Codex
  replayed the earlier reproducers and the 57 registered Calor cases through MCP (all match); file
  builds, xUnit and runtime comparisons were unavailable in the read-only sandbox.
- **Verdict: REQUEST CHANGES** (2 BLOCKING, 5 MAJOR).

## Findings and dispositions

| # | Finding (Codex, with reproducer) | Severity | Disposition |
|---|---|---|---|
| R3-1 | `(+ x x)` on a user type with an overloaded `operator+` that clears the aliased slot kept the narrowing of a `ref` parameter. | BLOCKING | Fixed. A unary or binary operator counts as heap-free only when every operand is a built-in scalar, string or `null`; a built-in string or character operation (`len`) only when its operands are. Witness: `OverloadedOperator_EndsAliasedNarrowing` (refusal plus a built-in control). |
| R3-2 | A `§USE` (disposal) in a loop body did not reach the back-edge. | BLOCKING | Fixed. The loop discovery pass now counts every aliased-storage invalidation the body triggers (calls, stores, `§USE`, `§EACH`, events, any hook) instead of re-deriving them from bound nodes. Witness: `UsingInALoop_EndsAliasedNarrowingAtTheBackEdge`; a `(len b)` control keeps the narrowing. |
| R3-3 | `b.Use(ref x, new Bomb())`: the hoisted `new Bomb()` ran before the receiver `b` was read. | MAJOR | Fixed. A call with any by-reference argument whose arguments would be hoisted is preserved (RO-CALL-1, RO-ORD-1). Witness: `R3-receiver-before-hoist`. |
| R3-4 | Unresolved dotted operands checked only their first member: `s.F.N` with readonly `N`, a terminal property, and `Int32.MaxValue` leaked Calor1002. | MAJOR | Fixed. Dotted names (rooted at a variable, `this`, an in-module type or a metadata type) are classified along the whole member path (`MemberPath`, `MemberPathReason`, `StaticMemberReason`, `MetadataMemberReason`); static members of built-in types are refused. Witnesses: `...("readonly field at the end of a dotted struct path")`, `...("metadata constant")`. |
| R3-5 | `§IDX2D g 0 0` on a class with a two-index indexer was accepted. | MAJOR | Fixed. Multi-dimensional access applies the same non-array receiver refusal as `§IDX`. |
| R3-6 | Readonly and `in` restrictions propagated through a reference-typed field: `Bump(ref s.B.N)` with `in S s` was refused although C# accepts it. | MAJOR | Fixed. `MemberPathReason` stops at the first member whose owner is a reference type: from there the storage is on the heap. Witnesses: `...("readonly reference-typed field in an in struct")` and the conversion row `R3-reference-field-of-in-struct`. |
| R3-7 | `ArchitectureTests` still required `_ => false` in `MayChangeHeap`. | MAJOR | Fixed: the allowance names the conservative `_ => true` default. |

## Effect on the corpora

None. The binder baseline, carrier evidence, Calor0270 ledger and binder emission catalog
regenerate unchanged after these fixes.
