Source-level verification only; runtime tests could not run because the read-only sandbox rejected temporary PDB creation. No files changed.

1. **VERIFIED.** `CreationSourceText` expands resolved target-typed `new()` to explicit C# construction. Both implicit-creation paths store it in `CSharpSource`, and visitor preservation uses the same helper. The emitter therefore receives `new P() { … }`, removing the witness’s CS8754 cause. The recorded pre-existing `§CS` argument typing limitation remains separate.

2. **VERIFIED.** `PendingStatementsChanged` ignores only newly appended `BindStatementNode`s whose initializer is null. Initialized binds, assignments, and other evaluating statements still trigger preservation; queue shrinkage or a changed prefix also triggers it. The out-variable declaration consequently survives native conversion, fixing the undefined-`x` witness without weakening evaluating-hoist detection.

3. **VERIFIED.** Both block lambdas and anonymous methods use `ConvertNestedBodyBlock`. It copies and clears the enclosing queue, converts the body, then clears the inner queue and restores the saved statements once, in order, through `finally`. Body hoists are flushed into the lambda’s statement list. The constructor-argument hoist survives long enough for rollback to detect and preserve the creation, fixing the missing-temp witness without duplicating enclosing statements.

4. **VERIFIED.** `ConvertDefaultExpression` dispatches on the semantic model’s resolved `SpecialType`. Thus `Single = System.Int32` produces integer zero, while actual `float`, `double`, and `decimal` retain their respective literal forms. Spelling is used when semantic type information is unavailable. No regression found in the changed dispatch.

APPROVE