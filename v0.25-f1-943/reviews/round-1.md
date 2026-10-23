# F1 #943 — Codex adversarial review, round 1

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 1 of the budget (3 rounds
  plus 1 verification pass).
- Input: `git diff origin/main...HEAD` at `8036316c` (implementation plus regenerated ledgers), with
  the D1 contract as the specification. Codex ran probes through the Debug compiler and MCP stdin with
  HOME and CALOR_HOME set to `/tmp`; it modified no repository file.
- **Verdict: REQUEST CHANGES** (3 BLOCKING, 4 MAJOR).

## Findings and dispositions

| # | Finding (Codex, with reproducer) | Severity | Disposition |
|---|---|---|---|
| R1-1 | `Bump(ref a[0].X.Y)` (nested struct fields in an array element): the Calor emitter hoisted `(§IDX{a} 0).X` into a temp, so the call wrote a copy; returned 0, not 7. | BLOCKING | Fixed. `CalorEmitter.Visit(FieldAccessNode)` never hoists a field path rooted in an element (`IsElementFieldPath`); the parser accepts `(§IDX{a} 0).X.Y`. The binder resolves dotted struct field paths (`ResolveStructFieldPath`). Witness: `R1-nested-struct-path` in `Conversion_KeepsByReferenceIdentity_OrPreservesTheShape` (all 5 surfaces, run-compared). |
| R1-2 | Loop back-edge: a narrowing made before a loop survives a later by-reference or aliased write in the body, so the next iteration reads null. | BLOCKING | Fixed for by-reference writes and aliased by-reference parameters (RO-NULL-2): the loop discovery pass (`PrepareLoopCallableState`) collects ref/out-written names and aliased-storage changes and ends those narrowings before the body binds. Witness: `LoopBackEdge_EndsNarrowingBeforeTheBody` (2 refusals, 2 controls). The same gap for a plain `§ASSIGN` in a loop body (no by-reference argument) exists on main and is not changed here; see the README. |
| R1-3 | `b.Next().Use(ref x)` with a by-value overload: chain decomposition dropped the modifier and called `Use(x)`; returned 2, not 71. | BLOCKING | Fixed. `CollectChainSteps` preserves (escalates) a chain step with a by-reference argument. Witness: `R1-chain-overload`. |
| R1-4 | `Use(ref a[0], new Bomb())`: the later argument was hoisted ahead of the element reference's bounds check. | MAJOR | Fixed. `HoistComplexArguments` preserves the call when any by-reference argument other than a plain local or parameter precedes a hoisted argument (RO-ORD-1). Witness: `R1-element-before-hoist`. |
| R1-5 | `Use(int.TryParse("1", out var n), 2)` with an omitted `in`: the lowered binding was emitted and then the statement was preserved, declaring `n` twice (CS0128). | MAJOR | Fixed. `ConvertBlock` checks each statement with lowered out declarations; if any part of it was kept as C#, the member is preserved. Witness: `R1-out-var-preserved-statement`. |
| R1-6 | `§A{ref} s.X` with `s` an `in` struct parameter reached C# (CS8330). | MAJOR | Fixed. A field of a value-type receiver is accepted only when the receiver is writable storage (resolved struct fields, metadata value-type fields and unresolved dotted names rooted at an in-module struct). Witness: `InvalidOperand_IsRefusedBeforeCodegen_AndItsControlCompiles("field of an in struct parameter")`. |
| R1-7 | `§A{ref} §IDX{xs} 0` with `xs` an `ArrayList` reached C# (CS0206). | MAJOR | Fixed. Any resolved nominal or in-module class receiver of `§IDX` is an indexer and is refused with Calor0212. Witness: `...("non-generic indexer element")`. |

## Implementer additions in the same round

- `§IDX2D` operands (`BoundMultiDimArrayAccess`) are accepted as storage and count as heap reads for
  alias narrowing; D1-CS-01 regressed on the first draft of R1-6 and is pinned by the conversion rows.
- `EffectInferrerCorpusDepthTests` pinned that the Serilog corpus still reaches two effect-inference
  cycles. Both cyclic members (`Logger.cs`, `BatchingSink.cs`) are now preserved as C# because their
  `out var` types do not resolve in a one-file conversion, so the pin is now 0; cycle detection stays
  covered by `EffectInferrerRecursionTests`.
