# Dependent Types M4 — Backlog

Deferred items from M2 gap analysis. These are known limitations, not bugs.

## 1. Scoped fact collection for if-guards

**Problem**: `FactCollector` adds if-condition facts globally rather than scoping them to the then-body. This is *sound* (adds assumptions, never removes them) but overly permissive — it could let the solver discharge an obligation that lives in the else-branch where the guard does not hold.

**Example**:
```
§IF{if1} (< i n)
  §R §IDX items i       // guard holds — should discharge
§/I{if1}
§EL
  §R §IDX items i       // guard does NOT hold — should fail
§/I{if1}
```

Currently both branches would be discharged because `(< i n)` is asserted globally.

**Fix**: Introduce per-obligation fact scoping. The `FactCollector` would return a tree of scoped fact sets rather than a flat list. The solver would then intersect the scope chain for each obligation's location.

**Effort**: Medium-high — requires architectural change to fact storage and solver integration.

---

## 2. Effect enforcement blocks `§Q` with indexed type size params

**Problem**: `Program.Compile` runs effect enforcement (and other checks) before obligation generation. When a function uses `§Q (< i n)` where `n` is a size parameter from `§ITYPE` (not an explicit function parameter), the effect enforcement or contract inheritance checker may report errors, causing an early pipeline return before obligation generation runs.

**Workaround**: Z3 tests currently set `EnforceEffects = false`.

**Fix**: Either:
- (a) Move obligation generation earlier in the pipeline (before effect enforcement), or
- (b) Teach the effect enforcement pass to recognize indexed type size params as valid identifiers (similar to what was done for `ContractVerifier`), or
- (c) Allow the pipeline to continue past non-fatal errors and still generate obligations.

**Effort**: Low-medium — option (b) is the most surgical fix.

---

## 3. Runtime bounds check emission for failed IndexBounds obligations

**Problem**: The `CSharpEmitter` erases `§ITYPE` definitions (correct) but does not emit runtime bounds checks for `IndexBounds` obligations that fail Z3 verification. By contrast, `ProofObligationNode` already emits `throw new InvalidOperationException(...)` for failed proof obligations.

**Expected behavior**: When an `IndexBounds` obligation is `Failed`, the emitter should inject:
```csharp
if (index < 0 || index >= n)
    throw new IndexOutOfRangeException($"Index {index} out of bounds [0, {n})");
```

**Fix**: In `CSharpEmitter.Visit(ReturnStatementNode)` (and similar statement visitors), check the obligation tracker for `IndexBounds` obligations matching the current array access. If the obligation is `Failed`, emit a guard before the access.

**Effort**: Medium — requires matching obligations to AST nodes by span, and generating the guard expression from the obligation condition.
