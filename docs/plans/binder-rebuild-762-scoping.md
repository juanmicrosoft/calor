# #762 binder rebuild — scoping (v0.13 spine)

**Date:** 2026-08-10
**Status:** Draft v1 — for adversarial review before any binder code merges
**Governing inputs:** issue #762 (DoD: 100% of accepted expression kinds structurally bind),
`v0.13-freeze-registrations.md` F-1 (the 61-class tier assignment — 55 registered / 6 residual —
frozen before this work; the denominator of roadmap §2.5 gate 1) and F-2 (the measurement corpus),
`roadmap-v0.13-v0.15.md` §2.1/§2.5. Per the freeze discipline, this doc merges before the work it
scopes.

## 1. Current state (measured, this branch)

- `Binder.BindExpression` (`Binding/Binder.cs:297`) has **19 arms** over **61** concrete
  `ExpressionNode` classes; everything else reaches `BindFallbackExpression` (`Binder.cs:515`),
  a **zero-child** `BoundCallExpression` named `<unsupported:TypeName>` typed `"OBJECT"` — the
  exact shape #762's evidence section describes (nested calls, division, indexing invisible to
  every analysis). The fallback's own comment records the class of harm: an earlier fallback
  (`BoundIntLiteral(0)`) made the div-by-zero checker false-positive on every unhandled divisor.
- **Types are strings** (`"INT"`, `"OBJECT"`, …) throughout `BoundNodes.cs` (~25 bound classes);
  `DecimalLiteralNode` binds as `BoundFloatLiteral((double)value)` — #762 item 4's defect,
  visible in the switch.
- **Consumers of the bound tree are the analysis layer only**: dataflow/CFG, the five bug-pattern
  checkers, taint, contract inference, verification analysis, k-induction
  (`Analysis/**`, `Verification/Z3/KInduction`). The emitters are `IAstVisitor` over the AST —
  codegen is NOT in this rebuild's blast radius.
- Top-level function registration ignores `TryDeclare` failures and bypasses the overload-set
  machinery class members already use (#762 items 5–6).
- Five AST classes still carry no-op `default!` `Accept`s (the #874 null-injection hazard class);
  two expression-node members (`GenericTypeNode`, `KeywordArgNode`) are F-1 Tier B pending the
  item-8 disposition.

## 2. Design decisions

**D1 — One authoritative dispatch table.** The `switch` becomes a
`IReadOnlyDictionary<Type, Func<Binder, ExpressionNode, BoundExpression>>` built once. A
**reflection completeness test** asserts every concrete `ExpressionNode` subclass has exactly one
entry — Tier A classes map to real binders, Tier B classes map to an *explicit*
`BindIncomplete` with a stated reason. A new class with no entry fails CI by construction (the
F-1 bidirectional completeness check's code-side half). Parser expression-start classification
(#762 item 7) derives from the same source of truth in the final PR — one table, two projections.

**D2 — `BoundIncompleteExpression` replaces the fallback.** A dedicated bound node that (a)
**binds and retains all child expressions** (the subtree stays analyzable — #762 item 3's "never
silently erase"), (b) carries the node-type name and an explicit stable type, and (c) reports an
*analysis incomplete* diagnostic (new code in the 0200 semantic band) at Warning severity.
Checkers treat it as an opaque non-constant value — preserving the div-by-zero lesson — but can
now traverse *through* it to the children they previously lost. The `<unsupported:` string
disappears from the tree; the F-2 instrument greps for the new node's marker instead (one
instrument update, in the same PR).

**D3 — Types stay strings in 0.13; they become explicit and non-null.** The 0.14 typed semantic
representation (roadmap §3.2) replaces the *representation*; this rebuild fixes the *structure*.
Every bound expression gets a non-null type string assigned through a single constructor choke
point (no more implicit `"INT"` defaults scattered through binders). Widening this into
metadata-backed typing is the 0.14 entry spike's job — an explicit non-goal here (§4).

**D4 — SymbolIds and exact spans (#762 item 9).** Declarations already carry validated syntax ids
(`§F{f001}`, `§MT{mt001}`, …); `SymbolId` = `<module-id>/<declaration-id>` for declarations,
`<module-id>/<function-id>/<name>#<ordinal>` for locals and parameters. `VariableSymbol` /
`FunctionSymbol` gain `SymbolId` + the **exact identifier token span** (not the whole
declaration). This is the substrate roadmap gates 3 (index correctness) and 4 (SymbolId-addressed
rename) instrument against, and what retires the #879/#893 mutable-cursor class properly: contract
emission can key on the symbol in hand instead of a cursor the emitter must remember to maintain
(the emitter change itself is follow-up, not this rebuild).

**D5 — Overload sets for top-level functions (#762 items 5–6).** Reuse the class-member
overload-set machinery for module-level functions; `TryDeclare` failures become diagnostics
(duplicate signature, ambiguity, no-match at call sites). Diagnosed, never silently first-wins.

**D6 — Decimal and conversion fidelity (#762 item 4).** `BoundDecimalLiteral` (no double
downcast); `BoundConversionExpression` preserving the target type; `is`/pattern tests bind
structurally instead of folding to `true`.

**D7 — Item-8 disposition lands in the final PR** with its F-1 amendment: `GenericTypeNode` and
`KeywordArgNode` either become non-`ExpressionNode` helper types (subtractive amendment with
supersession entry) or get real binders (additive promotion to Tier A). Until then they stay
residual and explicitly incomplete — never silently absent.

## 3. PR slicing (each PR: suite green, adversarial review, merge; family-by-family)

| PR | Scope | Exit criterion |
|---|---|---|
| **B1 — rails** | Dispatch table + reflection completeness test; `BoundIncompleteExpression` + diagnostic; Tier B explicit-incomplete; **the F-2 instrument** (CI leg over the pinned corpus publishing the incomplete-fraction, with the revert-the-fix discriminating pin) | All 61 classes flow through the table; baseline fraction measured and published; zero checker behavior change (fallback semantics preserved for not-yet-bound classes) |
| **B2 — core 9** | `Ok/Err/Some`, `ExpressionCallNode`, `AnonymousObjectCreationNode`, `RecordCreationNode`, `WithExpressionNode`, `ThrowExpressionNode` (+`SelfRefNode` per its F-1 dormant rule) | F-1 core row fully live |
| **B3 — arrays/indexes + collections** | 13 classes; the biggest checker payoff (index-OOB, off-by-one see real structure) | Family binds; checker deltas disclosed |
| **B4 — string family** | `StringOperationNode`, `InterpolatedStringNode`, `StringBuilderOperationNode`, `CharOperationNode` | Family binds |
| **B5 — conversion + type/pattern + decimals** | `TypeOperationNode`, `IsPatternNode`, `TypeOfExpressionNode`, D6 | Casts keep operand+target; patterns stop folding to `true` |
| **B6 — control-value forms** | `NullCoalesceNode`, `NullConditionalNode`, `MatchExpressionNode`, `LambdaExpressionNode`, `AwaitExpressionNode` | Family binds |
| **B7 — quantifiers** | `ForallExpressionNode`, `ExistsExpressionNode`, `ImplicationExpressionNode` (contract-adjacent; verify no interaction with the verification pipeline's own AST path) | Family binds |
| **B8 — interop + overloads + closure** | `RawCSharpExpressionNode`/`FallbackExpressionNode` structural binding; D5 overload sets; expression-start unification (#762 item 7); D7 item-8 disposition + F-1 amendment; **gate 1 evaluation** | Zero Tier A `incomplete` on the F-2 corpus; DoD met |

Ordering rationale: B1 makes the gap *visible and measured* before any of it closes (the freeze
discipline applied to code); B2–B7 order by checker payoff and risk; B8 carries the items that
touch the parser and the frozen registration.

## 4. Explicitly out of scope

- **Metadata-backed .NET typing, overload resolution against BCL signatures, nullable
  annotations** — the 0.14 entry spike (roadmap §3.1). This rebuild must not grow into it.
- **Effect-resolution changes**: `BoundCallExpression.Target`-string consumers
  (`MapShortTypeNameToFullName`, `NullDereferenceChecker`'s `.unwrap` suffix convention) keep
  their contracts until 0.14/0.15; new bound nodes must not break the string surface they read.
- **Emitter/codegen changes** (AST-based; unaffected), except the follow-up noted in D4.
- **Rebuilding checkers on the richer tree** (#786, scheduled 0.15) — checkers get *more* input,
  their logic is not rewritten here.

## 5. Instrumentation and gates

- **Incomplete-fraction**, published per release from B1 on: incomplete-marked bound nodes ÷
  total bound expressions over the F-2 corpus (both legs). Reported-not-adjudicated, per the
  registration; the *gate* (roadmap §2.5.1) is zero Tier A fallbacks at release.
- **Discriminating pin** (F-2's requirement): a fixture whose Tier A construct is temporarily
  routed to `BindIncomplete` must turn the CI leg red; pinned as a test the same way the PP-S4
  self-test pins run continuously.
- **Checker-delta disclosure**: each family PR states which checker verdicts changed on the
  corpus and why (structure became visible), with the D-S2.3 negative-corpus precedent applied —
  new findings must be real, not fallback-shape artifacts.

## 6. Risks

1. **Checker behavior movement** is the big one: five checkers currently key on the fallback's
   opaque shape; every family PR changes their input distribution. Mitigation: B1 freezes a
   baseline checker-verdict snapshot over the F-2 corpus; family PRs diff against it and
   disclose.
2. **Parser coupling** (`IsExpressionStart` vs dispatch) — deferred to B8 deliberately; touching
   it early risks destabilizing the whole expression grammar while binding is mid-flight.
3. **Golden/E2E churn**: bound-tree changes shouldn't move emitted C#, but diagnostics counts
   will move (new incomplete warnings). E2E goldens are diagnostics-blind; lint/analyze
   snapshots may not be — audited in B1.
4. **Scope creep toward 0.14** — §4 is the fence; reviews should police it.
