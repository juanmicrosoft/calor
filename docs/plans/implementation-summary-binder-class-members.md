# Implementation Summary: Extend Binder to Class Members

**Date:** 2026-04-16
**Branch:** main (uncommitted)
**Design doc:** `docs/plans/extend-binder-to-class-members.md` (v3)

---

## What Changed

### Problem

The Calor analysis pipeline (dataflow, bug patterns, taint tracking, Z3 verification) only analyzed top-level `§F` functions. Class members (`§MT`, `§CTOR`, `§PROP`, `§OP`, `§IXER`, `§EVT`) were ignored. The pipeline reported "0 functions analyzed" for 262,150 converted `.calr` files across 51 open-source C# projects.

### Solution

Extended the Binder to walk `module.Classes` and bind all executable class member bodies as `BoundFunction` instances, making them visible to the full analysis suite.

---

## Files Modified (12 files)

### Binding Layer (3 files)

**`src/Calor.Compiler/Binding/BoundNodes.cs`**
- Added `BoundMemberKind` enum (11 values: TopLevelFunction, Method, Constructor, PropertyGetter, PropertySetter, PropertyInit, OperatorOverload, IndexerGetter, IndexerSetter, EventAdd, EventRemove)
- Extended `BoundFunction` with `MemberKind` and `ContainingTypeName` properties; added 7-arg constructor; existing constructors default to `TopLevelFunction`/`null`
- Added `BoundUnsupportedStatement` — opaque placeholder for statement types the Binder can't fully bind
- Added 7 new bound statement types: `BoundAssignmentStatement`, `BoundCompoundAssignment`, `BoundForeachStatement`, `BoundUsingStatement`, `BoundThrowStatement`, `BoundDoWhileStatement`, `BoundExpressionStatement`
- Added 4 new bound expression types: `BoundThisExpression`, `BoundBaseExpression`, `BoundFieldAccessExpression`, `BoundNewExpression`

**`src/Calor.Compiler/Binding/Binder.cs`**
- Added `ScopeRestorer` RAII pattern (`PushScope`/`Dispose`); converted all 6 existing scope save/restore sites
- Added `_currentClassName` field for `this` expression binding
- Extracted `BindParameters` shared helper from `BindFunction`
- Refactored `ExtractEffects` → `ExtractMethodEffects(EffectsNode?)` for reuse by method binders
- Extended `BindStatement` switch from 13 cases + throw to 28 cases + `BoundUnsupportedStatement` default (added: Assignment, CompoundAssignment, Foreach, Using, Throw, Rethrow, DoWhile, ExpressionStatement, YieldReturn, YieldBreak, SyncBlock, Print, FallbackComment, RawCSharp, PreprocessorDirective, EventSubscribe, EventUnsubscribe)
- Extended `BindExpression` switch with 4 new cases: `ThisExpressionNode`, `BaseExpressionNode`, `FieldAccessNode`, `NewExpressionNode`
- Added `BindFieldAccess` — resolves `this.field` via class scope lookup
- Added `BindNewExpression` — binds constructor arguments
- Updated `BindCallExpression` for arity-aware overload resolution (`LookupByArity` first, fall back to `Lookup`)
- Added `CreateClassScope` — creates child scope with class fields as `VariableSymbol`s
- Added `RegisterClassMethods` — registers method overloads via `DeclareOverload` for arity-aware resolution
- Added `BindClassMembers` — walks Methods, Constructors, Properties, Operators, Indexers, Events, nested classes
- Added `TryBindMember` — resilient per-member binding with two-severity error handling (NotSupportedException → Calor0930 warning; other → Calor0932 ICE error); failed members not added to functions list
- Added `BindMethod` — binds method body with effects extraction, returns `BoundFunction` with `BoundMemberKind.Method`
- Added `BindConstructor` — binds constructor body (initializer skipped per design), returns with `BoundMemberKind.Constructor`
- Added `BindPropertyAccessor` — implicit `value` parameter for Set/Init, returns with PropertyGetter/Setter/Init
- Added `BindOperator` — effects marked as `*:*` (unknown) since `OperatorOverloadNode` has no Effects field, returns with OperatorOverload
- Added `BindIndexerAccessor` — declares indexer parameters + implicit `value` for setters
- Added `BindEventAccessor` — implicit `value` parameter of delegate type
- Added 10 statement binder methods: `BindAssignmentStatement`, `BindCompoundAssignment`, `BindForeachStatement`, `BindUsingStatement`, `BindDoWhileStatement`, `BindSyncBlock`, `BindUnsupportedStatement` (with per-NodeTypeName deduplication)
- Extended `Bind(ModuleNode)` with third pass: `foreach (var cls in module.Classes) BindClassMembers(cls, functions)`

**`src/Calor.Compiler/Binding/Scope.cs`**
- Added `_overloadSets` dictionary (`Dictionary<string, List<FunctionSymbol>>`)
- Added `DeclareOverload(FunctionSymbol)` — stores overloads per name, first overload also in `_symbols` for backward compat
- Added `LookupByArity(string name, int argCount)` — prefers exact arity match, falls back to first overload, walks parent chain

### Diagnostics (1 file)

**`src/Calor.Compiler/Diagnostics/Diagnostic.cs`**
- Added `Calor0930` (AnalysisSkipped) — Warning: member binding skipped due to known limitation
- Added `Calor0931` (AnalysisUnsupportedNode) — Info: statement type not supported, treated as opaque (deduplicated per NodeTypeName per file)
- Added `Calor0932` (AnalysisICE) — Error: internal compiler error during member analysis

### Downstream Analysis (8 files)

**`src/Calor.Compiler/Analysis/Dataflow/BoundNodeHelpers.cs`**
- `GetUsedVariables(BoundExpression)`: added cases for `BoundFieldAccessExpression`, `BoundNewExpression`
- `GetDefinedVariable(BoundStatement)`: added cases for `BoundAssignmentStatement` (target variable), `BoundCompoundAssignment` (target variable), `BoundForeachStatement` (loop variable), `BoundUsingStatement` (resource)
- `GetUsedVariables(BoundStatement)`: added 7 cases for all new statement types
- `GetAllDefinedVariablesInStatement`: added `BoundForeachStatement` (yield + recurse), `BoundDoWhileStatement` (recurse), `BoundUsingStatement` (yield + recurse), `BoundTryStatement` (recurse — pre-existing gap fix)

**`src/Calor.Compiler/Analysis/Dataflow/ControlFlowGraph.cs`**
- `ProcessStatement` switch: added 8 new cases — Assignment/CompoundAssignment/ExpressionStatement (non-branching), ThrowStatement (terminates block with exit edge), ForeachStatement/DoWhileStatement/UsingStatement (dedicated process methods), UnsupportedStatement (fall-through + exit edge)
- Added `ProcessForeachStatement` — loop structure like ForStatement (condition → body → back edge)
- Added `ProcessDoWhileStatement` — body-first loop (body → condition → back edge or exit)
- Added `ProcessUsingStatement` — simplified try/finally (body → after)

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/DivisionByZeroChecker.cs`**
- `CheckStatement`: added 7 cases (Assignment, CompoundAssignment, Foreach, DoWhile, Using, ExpressionStatement, Throw) — checks expressions and recurses into bodies

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/OverflowChecker.cs`**
- Same 7 new cases as DivisionByZeroChecker

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/IndexOutOfBoundsChecker.cs`**
- Same 7 new cases

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/NullDereferenceChecker.cs`**
- Same 7 new cases (with `checkedVariables` parameter threading)

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/OffByOneChecker.cs`**
- Added 3 cases (Foreach, DoWhile, Using) — body recursion only (this checker only looks for for-loop patterns)

**`src/Calor.Compiler/Analysis/BugPatterns/Patterns/PreconditionSuggester.cs`**
- Same 7 new cases (adapted to different method signature with `paramNames`/`guardedParams`)

**`src/Calor.Compiler/Analysis/Security/TaintAnalysis.cs`**
- `AnalyzeStatement`: added 8 new cases — Assignment (taint propagation value→target via `GetTaintLabelsFromExpression`/`AddTaint`), CompoundAssignment, Foreach (taint from collection→loop variable), DoWhile, Using, ExpressionStatement, Throw, TryStatement (pre-existing gap fix — recurse into try/catch/finally bodies)

**`src/Calor.Compiler/Analysis/VerificationAnalysisPass.cs`**
- `ExtractPreconditionGuardedParams`: added loop over `module.Classes` → methods/constructors/operators with preconditions, using qualified names matching the Binder's naming convention

**`src/Calor.Compiler/Analysis/ContractInference/ContractInferencePass.cs`**
- Added `BoundMemberKind` guard: `if (boundFunc.MemberKind != BoundMemberKind.TopLevelFunction) continue;` to prevent spurious contract inference on class members
- `FindDivisorParamsInStatement`: added 7 new cases (Assignment, CompoundAssignment, ExpressionStatement, Foreach, DoWhile, Using, Try)

---

## Design Decisions Made

1. **Reuse `BoundFunction` with `BoundMemberKind` enum** — pragmatic trade-off over a proper ADT. All downstream consumers work unchanged; `ContractInferencePass` filters via `MemberKind`.

2. **`ScopeRestorer` RAII pattern** — eliminates scope corruption risk from 14+ try/finally blocks. All scope push/pop now uses `using var _ = PushScope(...)`.

3. **Arity-aware overload resolution** — `Scope.DeclareOverload`/`LookupByArity` stores overload sets per name, matches by argument count. Eliminates the class of type-based false findings where the wrong overload's return type flows into Z3.

4. **Class fields in scope** — `CreateClassScope` declares all class fields as `VariableSymbol`s, visible to all member bodies. This prevents Calor0900 false positives on field references.

5. **Constructor initializer skipped** — `: base()`/`: this()` not bound. Documented known gap. Fields set by chained constructors are not tracked.

6. **Operator effects `*:*`** — `OperatorOverloadNode` has no `Effects` field in the AST. Marked as unknown rather than empty to avoid making the effect system lie.

7. **`BoundUnsupportedStatement`** — best-effort, not sound. CFG models it with fall-through + exit edge. Deduplicated diagnostic per NodeTypeName per file.

8. **Two-severity `TryBindMember`** — `NotSupportedException` → Calor0930 warning; other exceptions → Calor0932 ICE error. Failed members not added to functions list.

9. **Taint propagation through assignments** — `BoundAssignmentStatement` and `BoundForeachStatement` propagate taint labels from value/collection to target/loop variable using existing `GetTaintLabelsFromExpression`/`AddTaint` API.

---

## What Was NOT Changed

- **No new AST node types** — all existing AST nodes were already present (`ThisExpressionNode`, `FieldAccessNode`, `NewExpressionNode`, `BaseExpressionNode`, all statement types)
- **No Parser changes** — the Parser already produces `ModuleNode.Classes` with all member types
- **No CSharpEmitter/CalorEmitter changes** — code generation is unrelated to analysis
- **No new test files** — all validation was done via existing test suite + manual corpus scan
- **`Scope.cs` structure unchanged** — `_symbols` dictionary kept; `_overloadSets` is additive

---

## Verification Results

### Test Suite
- **6,208 tests pass**, 0 failures, 288 skipped (Z3-dependent)
- **0 warnings, 0 errors** in build (`TreatWarningsAsErrors` enabled)

### Corpus Validation (sampled)

| Project | Files Sampled | Members Analyzed | Bug Patterns | Taint Findings |
|---------|--------------|-----------------|-------------|----------------|
| Newtonsoft.Json | 20 | 157 | 22 (div-by-zero, missing preconditions) | 0 |
| Dapper | 154 | — | 7 (div-by-zero, missing preconditions) | 4 (command injection) |
| Humanizer | 227 | — | 98 (3 proven div-by-literal-zero) | 0 |
| Mapster | 268 | — | 5 (unsafe unwrap) | 0 |
| v2rayN | 40 | — | 0 | 10 (path traversal) |
| PowerShell | 50 | — | 6 (div-by-zero) | 20 (command injection) |
| semantic-kernel | 50 | — | 0 | 6 (command injection, path traversal) |
| Bitwarden server | 40 | — | 1 (false positive — DEC:100 misparse) | 0 |

### Verified High-Impact Findings

| Finding | Project | File | Description |
|---------|---------|------|-------------|
| Zip slip (CWE-22) | v2rayN | `UpgradeApp.cs` | Zip entry `FullName` used to construct extraction path without sanitization — attacker-controlled zip writes outside target directory |
| Path traversal (6x) | v2rayN | `FileUtils.cs` | `fileName` parameter flows to `File.Delete`, `File.WriteAllTextAsync` without path sanitization |
| Command injection (14x) | PowerShell | `WSManPlugin.cs` | Remote user input from WS-Management flows to command execution in the PowerShell remoting plugin |
| Command injection (4x) | PowerShell | `ast.cs` | User-supplied script content flows to command execution in the parser |
| Command injection (2x) | semantic-kernel | `FlowExecutor.cs` | User input flows to `ExecuteFlowAsync`/`ExecuteStepAsync` in AI orchestration |
| Division by zero | Newtonsoft.Json | `JsonValidatingReader.cs` | `FloatingPointRemainder(dividend, divisor)` called with `DivisibleBy.GetValueOrDefault()` which is 0 when `DivisibleBy` is set to 0 |
| Division by literal zero (3x) | Humanizer | `HebrewNumberToWordsConverter.cs` | `(/ number (cast i32 group))` where `group` enum value is 0 |

### Known False Positives

| Finding | Project | Reason |
|---------|---------|--------|
| Calor0920 on `DEC:100` | Bitwarden | Checker misinterprets decimal literal as zero |
| Calor0927 off-by-one | PowerShell `EventManager.cs` | Code correctly adjusts index with `counter - 1` |
| Calor0982 command injection | PowerShell `Attributes.cs` | `ValidateTrustedDataAttribute.Validate` is designed to handle untrusted input |

---

## Known Gaps / Deferred Work

1. **Constructor initializers** — `: base()`/`: this()` not bound. Fields set by chained constructors not tracked.
2. **Generic type parameter resolution** — 15.5% of methods live in generic classes. `T` is not resolved.
3. **Cross-class type resolution** — `new Class2()` in `Class1` can't resolve `Class2` to a definition.
4. **`BoundUnsupportedStatement` CFG model** — documented as best-effort, not sound. May-define-all/may-use-all model described in v3 plan not fully implemented; current implementation uses fall-through + exit edge only.
5. **No new unit tests** — validation was via existing test suite + corpus scan. Dedicated tests for binding/analysis of class members should be added.
6. **`DEC:` literal misparse in div-by-zero checker** — `DEC:100` incorrectly flagged as literal zero.
7. **Full corpus scan** — only sampled 40-50 files per project. Full 262K file scan not yet run.
8. **Overload resolution** — arity-aware only. Same-arity overloads use first-match.
