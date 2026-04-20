# Follow-Up PRs: Binder Class Members Extension

These items were identified during the class member binding implementation but require multi-PR architectural work. Each is scoped with an estimated effort and design sketch.

---

## 1. Generic Type Parameter Resolution

**Priority:** High (15.5% of methods in Newtonsoft.Json live in generic classes)
**Effort:** 3-5 days
**Prerequisite:** None

### Problem
`BoundThisExpression.TypeName` carries the raw class name including type parameters (e.g., `JsonConverter<T>`). Method parameters reference `T` (e.g., `?T:value`). No current checker resolves `T`, so type-dependent analysis is degraded for generic class members.

### Design Sketch
1. Parse type parameters from `ClassDefinitionNode.TypeParameters` (already present in AST)
2. Add `TypeParameterSymbol` to `Scope` — declared in class scope alongside fields
3. When binding method bodies in generic classes, resolve `T` references to `TypeParameterSymbol`
4. For Z3 type translation, treat unresolved type parameters as unconstrained (widest bit-vector or opaque sort)
5. Where constraints exist (`§WHERE T : IComparable`), propagate the constraint type

### Key Files
- `Binding/Scope.cs` — new `TypeParameterSymbol` type
- `Binding/Binder.cs` — `CreateClassScope` declares type parameters
- `Verification/Z3/ContractTranslator.cs` — handle type parameter in `CreateVariableForType`

---

## 2. Cross-Class Type Resolution (Module-Level Type Registry)

**Priority:** Medium (affects `new Class2()` in `Class1`, method return type resolution)
**Effort:** 3-5 days
**Prerequisite:** None

### Problem
`new Class2()` inside `Class1` cannot resolve `Class2` to a class definition. `BindNewExpression` returns `BoundNewExpression` with the type name as a string. No checker resolves type names to class definitions for constructor signatures or field types.

### Design Sketch
1. In `Binder.Bind()`, before Pass 2, create a module-level type registry:
   ```csharp
   var typeRegistry = new Dictionary<string, ClassDefinitionNode>();
   foreach (var cls in module.Classes)
       typeRegistry[cls.Name] = cls;
   ```
2. `BindNewExpression` looks up the type in the registry to get constructor signatures
3. `BindCallExpression` for qualified calls (`Class2.Method()`) resolves through the registry
4. Store as `_typeRegistry` field on `Binder`

### Key Files
- `Binding/Binder.cs` — add `_typeRegistry`, populate in `Bind()`, use in `BindNewExpression`/`BindCallExpression`

---

## 3. Interprocedural Analysis

**Priority:** Low-Medium (needed for constructor initializer field tracking, cross-method taint)
**Effort:** 2-4 weeks
**Prerequisite:** Cross-class type resolution (#2)

### Problem
Constructor initializer chains (`: this()` → `: base()`) initialize fields that the constructor body later reads. Cross-method field flows (method A writes field, method B reads it) are invisible to intraprocedural analysis. Taint can flow through method calls that aren't currently tracked.

### Design Sketch
This is a research-grade feature. Two approaches:

**A. Summary-based (cheaper, less precise):**
1. For each method, compute a summary: which parameters flow to return, which fields are read/written
2. At call sites, apply the summary to propagate taint/types through the call
3. Iterate to fixed point for recursive calls

**B. Context-sensitive (more precise, expensive):**
1. Build a call graph from `BoundCallStatement`/`BoundCallExpression` targets
2. Inline callee analysis at each call site with the caller's context
3. Use k-CFA or object-sensitivity for precision

Recommendation: start with summary-based for taint analysis only (highest ROI).

### Key Files
- New `Analysis/Interprocedural/` directory
- `Analysis/Security/TaintAnalysis.cs` — apply summaries at call sites
- `Binding/BoundNodes.cs` — `BoundFunction.Summary` field

---

## 4. Property-Based / Fuzz Testing

**Priority:** Medium (catches edge cases in binding, improves robustness)
**Effort:** 2-3 days
**Prerequisite:** None

### Problem
The Binder has complex state management (scope, class scope, static context, nested classes). Edge cases like deeply nested generics, circular references, or adversarial AST shapes are untested. Current tests are example-based, not exhaustive.

### Design Sketch
1. Add FsCheck (or CsCheck for C#) dependency to test project
2. Create AST generators:
   - `Arbitrary<ModuleNode>` — random modules with classes, methods, fields
   - `Arbitrary<ExpressionNode>` — random expressions (including `this`, field access, `new`)
   - `Arbitrary<StatementNode>` — random statements
3. Property tests:
   - "Binding never throws" — `Binder.Bind(randomModule)` completes without exception
   - "All bound functions have non-null Symbol" — structural invariant
   - "MemberKind matches ContainingTypeName" — `TopLevelFunction` ↔ `null`, others ↔ non-null
   - "Scope RAII is restored" — after `Bind()`, `_scope` is back to module scope
4. Shrinking: FsCheck automatically shrinks failing inputs to minimal reproduction

### Key Files
- New `tests/Calor.Compiler.Tests/Analysis/ClassMemberPropertyTests.cs`
- Test project `.csproj` — add FsCheck/CsCheck package reference
