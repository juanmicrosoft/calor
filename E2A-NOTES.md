# E2 slice a — effect-row SYNTAX (working notes)

Branch `feat/v0.15-e2a-row-syntax`, cut from `77f3a9c0`.
Scope: parse + AST + emit. **No** row checking, **no** binder rows, **no** lattice.
Governing doc: `docs/design/effect-rows-in-the-type-system.md` §3, §5.1–5.2, §7.1–7.3,
§13.1–13.2, §13.5(a).

## Measurements taken before writing any code

| Probe | Result |
|---|---|
| Committed `.calr` (886, `docs/design/spikes/` excluded) — `§O{…} §E{`, `§I{…} §E{`, `§FLD{…} §E{`, `§B{…} §E{`, `-> T §E{`, `( … §E{` on one line | **0, 0, 0, 0, 0, 0** — §3.2 reproduces exactly |
| Non-`.calr` sources holding a same-line row form | **1** — `tests/Calor.Evaluation/Tests/LlmEvaluationCalculatorTests.cs:385`, a single-line program `§M{…} §F{…} §I{str:msg} §O{void} §E{cw} §P msg`. Under the line rule the `§E{cw}` becomes the **return's** row. That test never compiles the string (it feeds an LLM question generator), so it is a risk to confirm green, not a break. Confirmed green — see gates below. |
| `eng/ast-schema.json` forces an edit? | **NO.** `ArchitectureTests.cs:158` compares `AstSchemaMetadata.Nodes[].ChildProperties` against `RecursiveAstWalker.GetAllChildProperties`, and **both sides are computed by reflection** (`Ast/AstNode.cs:633` `Create<TNode>`). The json carries only `{name, source}` (184 entries, no `childProperties` key). Adding a `Row` child to an existing node type therefore changes nothing there. Design-doc §3.3 and §9 claim this edit is "forced"; **that claim is wrong** and is corrected in the doc by this PR. A schema edit is forced only by a new node *type*, and this slice adds none. |
| `Calor0404` / `Calor0405` free in `Diagnostics/Diagnostic.cs`? | yes — 0400–0403 and 0410+ are taken, 0404/0405 unused |

## Storage decisions (and why)

**Rows.** `EffectsNode? Row` as an **optional trailing constructor parameter** on the four
existing classes — `OutputNode`, `ParameterNode` (`Ast/FunctionNode.cs`), `BindStatementNode`
(`Ast/ControlFlowNodes.cs`), `ClassFieldNode` (`Ast/ClassNodes.cs`). Immutable, no settable
AST state, no new node type, no visitor churn.

**`eff` binders — property name `EffectParameters`.** They are **NOT** stored as
`TypeParameterNode`s. Reason: G-CODEGEN (§12.2) requires `eff` to be fully erased at codegen,
and every consumer of `TypeParameters` (`CSharpEmitter`, binder, metadata, LSP) would have to
learn to skip them — seven places to get right, one to get wrong and emit `<T, e>` into C#.
Keeping them out of the list makes erasure the default and costs `CSharpEmitter` nothing.

Carrier: `EffectParameterInfo(string Name, int Ordinal, TextSpan Span)`, a plain record in
`Ast/GenericNodes.cs` — **not** an `AstNode`, following the `InlineRefinementInfo` precedent
(`Ast/RefinementNodes.cs:120`). `Ordinal` is the binder's index inside the original `<…>`
list, which is what lets `CalorEmitter` reconstruct `<T, eff e>` vs `<eff e, T>` byte-exactly
instead of assuming binders come last.

Homes: `FunctionNode` (§F/§AF), `MethodNode` (§MT/§AMT), `MethodSignatureNode` (interface
member — the position `MemberLevelEffOnInterfaceMember_Parses` pins, and the spelling
`spike-verdict.json.a3MiddlewareSpelling` chose).

**Effect variables inside a row.** `EffectsNode.EffectVariables` (`IReadOnlyList<string>`),
separate from the existing `Effects` dictionary. `AttributeHelper.InterpretEffectsAttributes`
resolves each reconstructed code against the in-scope binder set **before**
`EffectCodes.TryParseCompact`, exactly as §7.2(b) specifies.

## Deviations from the doc's literal wording, and why

1. **Recovery sites are 3, not "one per insertion point".** §3.1 puts the Calor0405 recovery
   on the non-adjacent branch of each of the six insertion points. Executed, the non-adjacent
   `§E` is *never seen* by `ParseParameter` / `ParseBindStatement` / `ParseClassField` — it is
   the next token when they return, and it surfaces in the enclosing loop. The recovery
   therefore lives where the token actually arrives: the **inline parameter list** (Z3), the
   **statement dispatch** (Z2), and the **class-member loop** (Z1). Same observable behaviour,
   same diagnostic, same 4→1 / 8→1 collapse.
2. **Positions 4 and 6 have no recovery**, by design — a later-line `§E` there falls through
   to the enclosing `§F`/`§MT`/`§DEL` loop's existing `§E` arm and is the declaration's row.
   That is P2(a), and it is what protects the 2948/471 two-line arrow corpus.
3. **`eff` at `§LAM` / `§DEL`.** Neither production has a type-parameter list at all (Z8b/Z8
   are `Calor0100`), so there is no place for a *binder* to be written. §7.3's rejection at
   positions 2 and 3 is implemented as the rejection it actually is: an effect **variable
   mentioned in the row** at those positions is Calor0404.
4. **The generic-argument cell is unreachable, not diagnosed.** `List<Func<i32,i32> §E{e}>`
   never reaches a row parse because inline types are read as strings
   (`ReadInlineTypeToken`). The pin asserts no row is produced there; it does not claim a
   Calor0404 the compiler cannot reach.
5. **Out-of-scope variable use is not diagnosed here.** Per the slice boundary, an `§E{e}`
   with no binder anywhere keeps today's `Calor0403: Unknown effect code 'e'`. Slice b/E3
   owns routing it to Calor0404. This also keeps X5b's transcript byte-stable.
6. **§3.5 / P6 (a row on a non-function-typed position → Calor0405) is NOT in this slice.**
   It needs the function-typedness predicate, which is checking, not syntax. Consequence:
   Z9/Z9b/Z9c move from "Compilation successful" to Calor0410 rather than to their final
   Calor0405. Recorded as slice b's debt, and listed in the PR body as an extra transcript
   divergence with this cause.

## Transcript divergences (§13.5(a) obligation table)

Filled in after the harness run — see the PR body for the final accounting.
