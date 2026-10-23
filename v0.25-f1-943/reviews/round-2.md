# F1 #943 — Codex adversarial review, round 2

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 2 of the budget.
- Input: `git diff origin/main...HEAD` at `98d68d7a` (round-1 fixes), with the round-1 record.
  Codex replayed the seven round-1 reproducers (all fixed) and re-ran the 57 registered Calor cases
  (all match). It could not run xUnit (sandbox socket permissions) and modified no repository file.
- **Verdict: REQUEST CHANGES** (3 BLOCKING, 5 MAJOR).

## Findings and dispositions

| # | Finding (Codex, with reproducer) | Severity | Disposition |
|---|---|---|---|
| R2-1 | `MayChangeHeap` omitted `BoundExpressionCall`: `§B{i32:z} §C f §/C` (an expression-target call of a delegate that clears the aliased slot) kept the narrowing of a `ref` parameter. | BLOCKING | Fixed. `MayChangeHeap` is now a whitelist: only literals, unary/binary operators, lambdas and plain local/parameter reads are free of heap effects; every other bound kind counts. Witness: `AliasNarrowing_IsInvalidatedOnlyWhenStorageMayBeShared("(?str:a:ref, Func<i32>:f)", "§B{i32:z} §C f §/C", false)`. |
| R2-2 | `Make(ref x).Use()`: chain decomposition checked modifiers only on member-access steps; the unqualified root call was dropped and `this.Use()` emitted (returned `0:2`, not `7:1`). | BLOCKING | Fixed. `CollectChainSteps` preserves the chain when any argument anywhere in it (root included, lambdas excluded) is by reference. Witness: `R2-chain-root`. |
| R2-3 | `Use(ref Get().X)` with `Get` returning `ref S`: the emitter hoisted `Get()` into a struct temp and passed the copy (returned 0, not 7). | BLOCKING | Fixed. `ExtractArgumentModifiers` preserves any by-reference operand that runs code (a call, construction, assignment or increment anywhere in it), so no receiver is hoisted or copied (RO-COPY-1/2, RO-ORD-1). Witness: `R2-ref-return-receiver`. |
| R2-4 | `Use(Before(), ref Get().N)`: the emitter hoisted `Get()` ahead of `Before()` (log `gb`, not `bg`). | MAJOR | Fixed by the R2-3 rule. Witness: `R2-receiver-call-order`. |
| R2-5 | `§A{ref} s.Length` (a scalar metadata property) reached C# (CS0206). | MAJOR | Fixed. The binder keeps the metadata member on its unresolved scalar path, and a dotted unresolved name rooted at a built-in scalar or string is refused (a member of a built-in type is never a variable). Witness: `InvalidOperand_...("scalar metadata property")`. |
| R2-6 | `(§IDX{a} 0).X.Y` with readonly `X`: only the terminal field was checked (CS1649). | MAJOR | Fixed. Every intermediate field of a dotted struct path must be writable. Witness: `...("readonly intermediate field of a struct path")`. |
| R2-7 | `§A{ref} other.n` in `K`'s constructor, `n` readonly: allowed because only the constructor's class was checked (CS0192). | MAJOR | Fixed. The constructor exception applies only to `this` or the bare name (and to a type receiver for static fields); a dotted unresolved `other.n` is classified through `other`'s class. Witness: `...("readonly field of another instance in a constructor")`. |
| R2-8 | `out var` inside a block lambda was lowered by the lambda body's own `ConvertBlock`. | MAJOR | Fixed. `LowerOutDeclarations` never lowers under a lambda; the declaration escalates and the member is preserved. Witness: `R2-block-lambda`. |

## Effect on the corpora

R2-3 and R2-8 preserve one more FluentValidation member (`MessageFormatter.BuildMessage`, an
`out var` in a block lambda), so its `value?.ToString()` carrier moved to `PreservedCases` in
`binder-source-carrier-evidence.json` (68 guarded carriers plus 4 preserved). Ledgers were
regenerated with their documented switches; the counts are in the README.
