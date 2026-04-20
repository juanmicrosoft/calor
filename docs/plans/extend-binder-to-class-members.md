# Plan: Extend Binder to Class Members

**Version:** 3 (revised after critique, pushback, and soundness review)

## Problem Statement

The Calor analysis pipeline (dataflow, bug patterns, taint tracking, Z3 verification) currently only analyzes **top-level `§F` functions**. The `Binder.Bind()` method iterates over `module.Functions` exclusively, ignoring all class members. Since converted C# code is almost entirely class-based, the analysis pipeline reports **"0 functions analyzed"** for the vast majority of the 262,150 converted `.calr` files across 51 open-source projects.

This means the Z3-powered bug detectors (division by zero, integer overflow, index out of bounds), dataflow analysis (uninitialized variables, dead stores), and taint tracking (SQL injection, command injection, XSS) cannot examine any class method, constructor, property accessor, operator, indexer, or event accessor body.

### Evidence

Running `calor --analyze` on Newtonsoft.Json (940 files, 2,232 methods, 255 constructors, 669 properties):
- **0 functions analyzed**, **0 bug patterns**, **0 taint issues** — consistently across all files.

The sole exception is Humanizer's `ByteSize.calr`, which has 2 module-level helper functions (`has`, `output`) that the Binder can reach — yielding 5 Calor0900 (uninitialized variable) findings.

### Corpus Characteristics (from early scan)

Surveyed Newtonsoft.Json (940 files) to understand what the Binder will encounter:

| Pattern | Occurrences | Implication |
|---------|------------|-------------|
| `§MT{}` (methods) | 2,232 | Primary target — most executable code lives here |
| `§PROP{}` (properties) | 669 | Getter/setter bodies need binding |
| `§CTOR{}` (constructors) | 255 | Field assignment + initializer patterns |
| `§OP{}` (operators) | 72 | Arithmetic — prime overflow checker targets |
| `§ASSIGN` | 146 files | Field assignment in ctors/methods — needs scope resolution |
| `§NEW{}` | 121 files | Object creation — needs `BindExpression` support |
| `§THIS` | 339 occurrences | `this` expression — needs `BindExpression` support |
| `§BASE` | 33 files | `base` calls — needs `BindExpression` support |
| Method overloads | 14x `ToString` in `JsonConvert` alone | Scope must handle name collisions |
| Generic classes | 14 files / 359 methods (15.5%) in Newtonsoft | Generic type params embedded in class name |

### Goal

Extend the Binder and analysis pipeline so that **all executable class member bodies** are bound and analyzed with sufficient semantic fidelity that the existing checkers produce meaningful results (not dominated by false positives from missing scope infrastructure).

---

## Scope

### In Scope — Member Types to Bind

| Priority | Member Type | AST Node | Body Field | Est. Count (Newtonsoft) |
|----------|------------|----------|------------|------------------------|
| **P0** | Methods | `MethodNode` | `IReadOnlyList<StatementNode> Body` | 2,232 |
| **P0** | Constructors | `ConstructorNode` | `IReadOnlyList<StatementNode> Body` | 255 |
| **P1** | Property accessors | `PropertyAccessorNode` | `IReadOnlyList<StatementNode> Body` | 568 (GET+SET) |
| **P1** | Operator overloads | `OperatorOverloadNode` | `IReadOnlyList<StatementNode> Body` | 72 |
| **P2** | Indexer accessors | `IndexerNode` → `PropertyAccessorNode` | `IReadOnlyList<StatementNode> Body` | 10 |
| **P2** | Event accessors | `EventDefinitionNode` | `IReadOnlyList<StatementNode>? AddBody/RemoveBody` | 8 |

### In Scope — Statement Types to Handle

The Binder's `BindStatement` switch currently throws on unrecognized statement types. Several statement types produced by the C# migration pipeline are not handled:

| Statement Type | Frequency in Converted Code | Needed For |
|---------------|---------------------------|------------|
| `AssignmentStatementNode` | Very high (every `x = y`) | Methods, constructors, property setters |
| `CompoundAssignmentStatementNode` | High (`+=`, `-=`, etc.) | Methods — overflow checker target |
| `ForeachStatementNode` | High | Methods with collection iteration |
| `UsingStatementNode` | Medium | Methods with IDisposable |
| `ThrowStatementNode` | Medium | Error paths |
| `ExpressionStatementNode` | Medium | Standalone expressions |
| `DoWhileStatementNode` | Low | Loop analysis |
| `YieldReturnStatementNode` | Low | Iterator methods |
| `YieldBreakStatementNode` | Low | Iterator methods |
| `RethrowStatementNode` | Low | Catch blocks |
| `SyncBlockNode` | Low | Concurrency |

Currently, hitting any of these causes the Binder to throw `InvalidOperationException`, aborting analysis for the entire function. This is why `SqlMapper.calr` fails with "Unknown statement type: AssignmentStatementNode".

### In Scope — Expression Types to Handle

Class member bodies use expression types that `BindExpression` currently routes to `BindFallbackExpression` → `BoundIntLiteral(0)`. This silently corrupts analysis (every `this.field = value` becomes assignment to literal `0`). These must be handled:

| Expression Type | Frequency | Current Behavior | Required Behavior |
|----------------|-----------|-----------------|-------------------|
| `ThisExpressionNode` | 339 in Newtonsoft | → `BoundIntLiteral(0)` | → `BoundThisExpression` (opaque) |
| `FieldAccessNode` | High (via `this.field`) | → `BoundIntLiteral(0)` | → `BoundFieldAccess` or resolved variable |
| `NewExpressionNode` | 121 files | → `BoundIntLiteral(0)` | → `BoundNewExpression` (opaque) |
| `BaseExpressionNode` | 33 files | → `BoundIntLiteral(0)` | → `BoundBaseExpression` (opaque) |
| `CastExpressionNode` | Common | → `BoundIntLiteral(0)` | → bind inner expression, preserve type |
| `MemberAccessNode` | Common | → `BoundIntLiteral(0)` | → `BoundCallExpression` or opaque |

### Out of Scope

- **Virtual dispatch resolution, inheritance chains** — methods are analyzed as standalone units.
- **Cross-method analysis** (inter-procedural dataflow) — each method/accessor is analyzed independently.
- **Field initializer binding** — `ClassFieldNode.DefaultValue` is a single expression, not a statement body.
- **Interface method signatures** — no bodies to bind.
- **Abstract/extern methods** — no bodies.
- **Generic type parameter resolution** — 15.5% of methods in Newtonsoft.Json live in generic classes (359/2320). Generic type parameters are embedded in class names (e.g., `§CL{c007:JsonConverter<T>:pub:abs}`) and method parameters reference `T` (e.g., `?T:value`). `BoundThisExpression.TypeName` carries the raw class name including type parameters. No current checker resolves `T`; if a future checker needs to, type parameter binding will be required.
- **Cross-class type resolution** — `new Class2()` inside `Class1` cannot resolve `Class2` to a class definition. `NewExpressionNode` binds to `BoundNewExpression` with the type name as a string. No current checker resolves type names to class definitions. A module-level type registry would be needed for this.

### Documented Known Gaps (Deferred)

- **Constructor initializers** (`: base(...)` / `: this(...)`): `ConstructorNode.Initializer` is **not bound**. The v2 plan attempted half-binding (bind args, discard results) but this adds crash surface without analytical value. Constructor body analysis starts *after* the initializer — fields set by chained constructors are not tracked. This is a subset of the broader inter-procedural analysis gap.
- **Full overload resolution**: Intra-class call resolution uses arity-aware lookup (match by argument count), not full signature-aware dispatch. When multiple overloads share the same arity, the first registered wins. Return types from ambiguous overloaded sibling calls may be imprecise.

---

## Design

### Core Principle: Reuse `BoundFunction` with Metadata

All bindable members are represented as `BoundFunction` with two new fields:

```csharp
public sealed class BoundFunction : BoundNode
{
    public FunctionSymbol Symbol { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public Scope Scope { get; }
    public IReadOnlyList<string> DeclaredEffects { get; }

    // NEW: member classification
    public BoundMemberKind MemberKind { get; }
    public string? ContainingTypeName { get; }

    // ... constructors updated with optional params, defaults to TopLevelFunction/null
}

public enum BoundMemberKind
{
    TopLevelFunction,
    Method,
    Constructor,
    PropertyGetter,
    PropertySetter,
    PropertyInit,
    OperatorOverload,
    IndexerGetter,
    IndexerSetter,
    EventAdd,
    EventRemove
}
```

**Why we're doing it this way:**

This is a pragmatic trade-off, not a principled design. An 11-variant enum pattern-matched across every analysis pass is a code smell — every new analysis must remember to handle all kinds, and forgetting one silently degrades results. A proper ADT (`BoundCallable = TopLevel of ... | Method of ... | PropertyAccessor of ...`) would enforce exhaustiveness at the type level and let kind-specific fields live where they belong (setter's implicit `value`, indexer's param list, event's delegate type).

We choose the enum because it's cheap to ship and the immediate need is narrow:
- `ContractInferencePass.Infer()` matches `boundModule.Functions` against `astModule.Functions` by name (line 42). Without `MemberKind`, class members would silently get spurious contract inference. The pass must filter on `MemberKind == TopLevelFunction`.
- Diagnostic reporting needs to distinguish "2,232 methods analyzed" from "2 functions analyzed."

**Retrofit trigger:** If `BoundMemberKind` grows beyond ~15 values or we need kind-specific fields beyond what `FunctionSymbol` carries, refactor to an ADT.

### Change 0: Class Scope Infrastructure (PREREQUISITE)

**Files:** `Binding/Scope.cs`, `Binding/Binder.cs`

The current `Scope` is `Dictionary<string, Symbol>` keyed by name only (Scope.cs:55). This has two problems:

1. **Overloaded methods collide** — JsonConvert has 14 `ToString` overloads. `TryDeclare` rejects duplicates.
2. **Class fields not in scope** — `_writer`, `DateTimeKindHandling`, etc. are unresolvable.

#### A. Arity-aware overload registration for method resolution

For intra-class call resolution, register methods as overload sets keyed by name. At call sites, disambiguate by argument count. This matters because the overflow checker uses `TypeName` to determine Z3 bit-vector widths (`OverflowChecker.cs:311`, `ContractTranslator.cs:485-505`) — a wrong return type produces wrong SMT encodings, not just imprecise results.

**Scope change:** Add `TryDeclareOverload` / `LookupByArity` to `Scope`:

```csharp
// In Scope.cs — new overload-aware storage alongside existing _symbols
private readonly Dictionary<string, List<FunctionSymbol>> _overloadSets = new(StringComparer.Ordinal);

public void DeclareOverload(FunctionSymbol symbol)
{
    if (!_overloadSets.TryGetValue(symbol.Name, out var list))
    {
        list = new List<FunctionSymbol>();
        _overloadSets[symbol.Name] = list;
    }
    list.Add(symbol);
    // Also register in _symbols if first of this name (for existing Lookup callers)
    _symbols.TryAdd(symbol.Name, symbol);
}

public FunctionSymbol? LookupByArity(string name, int argCount)
{
    if (_overloadSets.TryGetValue(name, out var list))
    {
        // Prefer exact arity match
        var match = list.FirstOrDefault(f => f.Parameters.Count == argCount);
        if (match != null) return match;
        // Fall back to first overload
        return list[0];
    }
    // Walk parent scopes
    return (Parent as Scope)?.LookupByArity(name, argCount);
}
```

**Registration pass:**
```csharp
// In BindClassMembers, before binding bodies:
foreach (var method in cls.Methods)
{
    var parameters = method.Parameters
        .Select(p => new VariableSymbol(p.Name, p.TypeName, isMutable: false, isParameter: true))
        .ToList();
    var returnType = method.Output?.TypeName ?? "VOID";
    classScope.DeclareOverload(new FunctionSymbol(method.Name, returnType, parameters));
}
```

**Call resolution update** in `BindCallExpression`:
```csharp
// Try arity-aware lookup first
var funcSymbol = _scope.LookupByArity(callExpr.Target, callExpr.Arguments.Count);
var returnType = funcSymbol?.ReturnType ?? "INT"; // INT only for truly unresolvable calls
```

This is ~20 lines more than first-match and eliminates the class of type-based false findings where the wrong overload's return type flows into Z3 bit-vector width selection. When multiple overloads share the same arity, the first registered wins — this is the remaining imprecision, documented in Known Gaps.

#### B. Class fields in scope

When binding a member body, class fields must be declared in the scope so that:
- `§ASSIGN _writer writer` resolves `_writer` as a known mutable variable
- The uninitialized variable checker (Calor0900) doesn't flag field references as uninitialized
- The dead store checker doesn't misreport field assignments

```csharp
private Scope CreateClassScope(ClassDefinitionNode cls)
{
    var classScope = _scope.CreateChild();
    foreach (var field in cls.Fields)
    {
        var isMutable = !field.Modifiers.HasFlag(MethodModifiers.Readonly);
        var fieldSymbol = new VariableSymbol(field.Name, field.TypeName, isMutable: isMutable);
        classScope.TryDeclare(fieldSymbol);
    }
    return classScope;
}
```

Each member binding creates a child scope from the class scope, so fields are visible to all members.

#### D. Scope push RAII pattern

The existing Binder has 6 sites with the `previousScope = _scope; _scope = child; try { ... } finally { _scope = previous; }` pattern. This plan adds ~8 more. One missed `finally` and scope corruption persists across files. Replace with a disposable:

```csharp
private IDisposable PushScope(Scope newScope)
{
    var previous = _scope;
    _scope = newScope;
    return new ScopeRestorer(this, previous);
}

private sealed class ScopeRestorer : IDisposable
{
    private readonly Binder _binder;
    private readonly Scope _previous;
    public ScopeRestorer(Binder binder, Scope previous) { _binder = binder; _previous = previous; }
    public void Dispose() => _binder._scope = _previous;
}
```

Usage in all bind methods:
```csharp
private BoundFunction BindMethod(MethodNode method, string className)
{
    using var _ = PushScope(_scope.CreateChild());
    var parameters = BindParameters(method.Parameters);
    // ... rest of method
}
```

This eliminates the entire class of scope-corruption bugs from missed `finally` blocks.

#### C. `this` and `base` expression handling

`§THIS` appears 339 times in Newtonsoft.Json alone. Currently hits `BindFallbackExpression` → `BoundIntLiteral(0)`, which corrupts every `this.field` pattern.

Add `BindExpression` cases:

```csharp
ThisExpressionNode thisExpr => new BoundThisExpression(thisExpr.Span, _currentClassName ?? "UNKNOWN"),
BaseExpressionNode baseExpr => new BoundBaseExpression(baseExpr.Span),
FieldAccessNode fieldAccess => BindFieldAccess(fieldAccess),
NewExpressionNode newExpr => BindNewExpression(newExpr),
```

Where `BoundThisExpression` and `BoundBaseExpression` are opaque expression nodes that carry the type name but don't pretend to be integer literals. `BindFieldAccess` resolves the field name against class scope when the target is `this`.

New bound expression types needed:

```csharp
public sealed class BoundThisExpression : BoundExpression
{
    public override string TypeName { get; }
    public BoundThisExpression(TextSpan span, string className) : base(span) { TypeName = className; }
}

public sealed class BoundBaseExpression : BoundExpression
{
    public override string TypeName => "OBJECT";
    public BoundBaseExpression(TextSpan span) : base(span) { }
}

public sealed class BoundFieldAccessExpression : BoundExpression
{
    public BoundExpression Target { get; }
    public string FieldName { get; }
    public override string TypeName { get; }
    // ...
}

public sealed class BoundNewExpression : BoundExpression
{
    public string TypeName { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    // ...
}
```

### Change 1: Extend `Binder.Bind()` to Walk Class Members

**File:** `src/Calor.Compiler/Binding/Binder.cs`

```csharp
public BoundModule Bind(ModuleNode module)
{
    var functions = new List<BoundFunction>();

    // Pass 1: register top-level function symbols (existing)
    foreach (var func in module.Functions) { ... }

    // Pass 2: bind top-level function bodies (existing)
    foreach (var func in module.Functions)
        functions.Add(BindFunction(func));

    // Pass 3: register + bind class member bodies
    foreach (var cls in module.Classes)
        BindClassMembers(cls, functions);

    return new BoundModule(module.Span, module.Name, functions);
}
```

New method `BindClassMembers`:
```csharp
private void BindClassMembers(ClassDefinitionNode cls, List<BoundFunction> functions)
{
    var className = cls.Name;

    // Create class-level scope with fields and method symbols
    var classScope = CreateClassScope(cls);
    RegisterClassMethods(cls, classScope);

    var previousScope = _scope;
    _scope = classScope;
    var previousClassName = _currentClassName;
    _currentClassName = className;

    try
    {
        // Methods (P0)
        foreach (var method in cls.Methods)
        {
            if (method.IsAbstract || method.IsExtern || method.Body.Count == 0)
                continue;
            var bound = TryBindMember(() => BindMethod(method, className), className, method.Name);
            if (bound != null) functions.Add(bound);
        }

        // Constructors (P0)
        foreach (var ctor in cls.Constructors)
        {
            if (ctor.Body.Count == 0) continue;
            var bound = TryBindMember(() => BindConstructor(ctor, className), className, ".ctor");
            if (bound != null) functions.Add(bound);
        }

        // Property accessors (P1)
        foreach (var prop in cls.Properties)
        {
            if (prop.Getter is { IsAutoImplemented: false })
            {
                var bound = TryBindMember(
                    () => BindPropertyAccessor(prop.Getter, className, prop.Name, prop.TypeName),
                    className, $"{prop.Name}.get");
                if (bound != null) functions.Add(bound);
            }
            if (prop.Setter is { IsAutoImplemented: false })
            {
                var bound = TryBindMember(
                    () => BindPropertyAccessor(prop.Setter, className, prop.Name, prop.TypeName),
                    className, $"{prop.Name}.set");
                if (bound != null) functions.Add(bound);
            }
            if (prop.Initer is { IsAutoImplemented: false })
            {
                var bound = TryBindMember(
                    () => BindPropertyAccessor(prop.Initer, className, prop.Name, prop.TypeName),
                    className, $"{prop.Name}.init");
                if (bound != null) functions.Add(bound);
            }
        }

        // Operator overloads (P1)
        foreach (var op in cls.OperatorOverloads)
        {
            if (op.Body.Count == 0) continue;
            var bound = TryBindMember(() => BindOperator(op, className), className, $"op_{op.Kind}");
            if (bound != null) functions.Add(bound);
        }

        // Indexer accessors (P2)
        foreach (var ixer in cls.Indexers)
        {
            if (ixer.Getter is { IsAutoImplemented: false })
            {
                var bound = TryBindMember(
                    () => BindIndexerAccessor(ixer.Getter, ixer.Parameters, className, ixer.TypeName),
                    className, "this[].get");
                if (bound != null) functions.Add(bound);
            }
            if (ixer.Setter is { IsAutoImplemented: false })
            {
                var bound = TryBindMember(
                    () => BindIndexerAccessor(ixer.Setter, ixer.Parameters, className, ixer.TypeName),
                    className, "this[].set");
                if (bound != null) functions.Add(bound);
            }
        }

        // Event accessors (P2)
        foreach (var evt in cls.Events)
        {
            if (evt.AddBody != null && evt.AddBody.Count > 0)
            {
                var bound = TryBindMember(
                    () => BindEventAccessor(evt.AddBody, className, evt.Name, "add", evt.DelegateType),
                    className, $"{evt.Name}.add");
                if (bound != null) functions.Add(bound);
            }
            if (evt.RemoveBody != null && evt.RemoveBody.Count > 0)
            {
                var bound = TryBindMember(
                    () => BindEventAccessor(evt.RemoveBody, className, evt.Name, "remove", evt.DelegateType),
                    className, $"{evt.Name}.remove");
                if (bound != null) functions.Add(bound);
            }
        }

        // Recurse into nested classes
        foreach (var nested in cls.NestedClasses)
            BindClassMembers(nested, functions);
    }
    finally
    {
        _scope = previousScope;
        _currentClassName = previousClassName;
    }
}
```

### Change 2: Resilient Per-Member Binding

```csharp
private BoundFunction? TryBindMember(Func<BoundFunction> bind, string className, string memberName)
{
    try
    {
        return bind();
    }
    catch (NotSupportedException ex)
    {
        // Known-unsupported: a recognized limitation (e.g., unsupported AST shape)
        _diagnostics.ReportWarning(
            Parsing.TextSpan.Empty,
            DiagnosticCode.AnalysisSkipped,
            $"Skipped analysis of '{className}.{memberName}': {ex.Message}");
        return null;
    }
    catch (Exception ex)
    {
        // Unexpected: internal compiler error — surface with stack info for bug reports
        _diagnostics.ReportError(
            Parsing.TextSpan.Empty,
            DiagnosticCode.AnalysisICE, // NEW dedicated ICE code
            $"Internal error analyzing '{className}.{memberName}': {ex.GetType().Name}: {ex.Message}");
        return null;
    }
}
```

Two severity levels:
- **Known-unsupported** (`NotSupportedException`): Warning with `Calor0930`. The Binder recognizes the limitation.
- **Unexpected** (any other exception): Error with `Calor0932` (ICE). This is an internal compiler invariant violation, not a user-facing issue. The message includes the exception type so it can be triaged.

In both cases, failed members are **not added** to the functions list.

### Change 3: Add Bind Methods for Each Member Type

**File:** `src/Calor.Compiler/Binding/Binder.cs`

Each method follows the same pattern as the existing `BindFunction`:
1. Create child scope from **class scope** (so fields are visible)
2. Declare parameters as `VariableSymbol` with `isParameter: true`
3. Call `BindStatements(body)`
4. Extract effects (if applicable)
5. Return `BoundFunction` with qualified name + `BoundMemberKind`

```csharp
private BoundFunction BindMethod(MethodNode method, string className)
{
    using var _ = PushScope(_scope.CreateChild()); // RAII — fields visible from class scope

    var parameters = BindParameters(method.Parameters);
    var returnType = method.Output?.TypeName ?? "VOID";
    var qualifiedName = $"{className}.{method.Name}";
    var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
    var boundBody = BindStatements(method.Body);
    var declaredEffects = ExtractMethodEffects(method.Effects);
    return new BoundFunction(method.Span, functionSymbol, boundBody, _scope,
        declaredEffects, BoundMemberKind.Method, className);
}

private BoundFunction BindConstructor(ConstructorNode ctor, string className)
{
    using var _ = PushScope(_scope.CreateChild());

    var parameters = BindParameters(ctor.Parameters);
    var qualifiedName = $"{className}..ctor";
    var functionSymbol = new FunctionSymbol(qualifiedName, "VOID", parameters);

    // Constructor initializer (: base(...) / : this(...)) is NOT bound.
    // Half-binding (bind args, discard results) adds crash surface without analytical value.
    // Body analysis starts AFTER the initializer — fields set by chained constructors
    // are not tracked. See Documented Known Gaps.

    var boundBody = BindStatements(ctor.Body);
    return new BoundFunction(ctor.Span, functionSymbol, boundBody, _scope,
        Array.Empty<string>(), BoundMemberKind.Constructor, className);
}

private BoundFunction BindPropertyAccessor(
    PropertyAccessorNode accessor, string className, string propName, string propType)
{
    using var _ = PushScope(_scope.CreateChild());

    var parameters = new List<VariableSymbol>();
    var memberKind = BoundMemberKind.PropertyGetter;

    // Setters/initers have an implicit 'value' parameter
    if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
        or PropertyAccessorNode.AccessorKind.Init)
    {
        var valueParam = new VariableSymbol("value", propType, isMutable: false, isParameter: true);
        _scope.TryDeclare(valueParam);
        parameters.Add(valueParam);
        memberKind = accessor.Kind == PropertyAccessorNode.AccessorKind.Set
            ? BoundMemberKind.PropertySetter : BoundMemberKind.PropertyInit;
    }

    var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? propType : "VOID";
    var qualifiedName = $"{className}.{propName}.{accessor.Kind.ToString().ToLowerInvariant()}";
    var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
    var boundBody = BindStatements(accessor.Body);
    return new BoundFunction(accessor.Span, functionSymbol, boundBody, _scope,
        Array.Empty<string>(), memberKind, className);
}

private BoundFunction BindOperator(OperatorOverloadNode op, string className)
{
    using var _ = PushScope(_scope.CreateChild());

    var parameters = BindParameters(op.Parameters);
    var returnType = op.Output?.TypeName ?? "VOID";
    var qualifiedName = $"{className}.op_{op.Kind}";
    var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
    var boundBody = BindStatements(op.Body);
    // OperatorOverloadNode has no Effects field in the AST (unlike MethodNode).
    // Mark as unknown effects rather than empty — operators over reference types with
    // mutable state can have real effects, and stamping empty makes the effect system lie.
    var declaredEffects = new List<string> { "*:*" }; // unknown effects — conservative
    return new BoundFunction(op.Span, functionSymbol, boundBody, _scope,
        declaredEffects, BoundMemberKind.OperatorOverload, className);
}

private BoundFunction BindIndexerAccessor(
    PropertyAccessorNode accessor, IReadOnlyList<ParameterNode> indexerParams,
    string className, string indexerType)
{
    using var _ = PushScope(_scope.CreateChild());

    // Declare indexer parameters (e.g., int index)
    var parameters = BindParameters(indexerParams);

    // Setter also has implicit 'value'
    var memberKind = BoundMemberKind.IndexerGetter;
    if (accessor.Kind is PropertyAccessorNode.AccessorKind.Set
        or PropertyAccessorNode.AccessorKind.Init)
    {
        var valueParam = new VariableSymbol("value", indexerType, isMutable: false, isParameter: true);
        _scope.TryDeclare(valueParam);
        parameters.Add(valueParam);
        memberKind = BoundMemberKind.IndexerSetter;
    }

    var returnType = accessor.Kind == PropertyAccessorNode.AccessorKind.Get ? indexerType : "VOID";
    var qualifiedName = $"{className}.this[].{accessor.Kind.ToString().ToLowerInvariant()}";
    var functionSymbol = new FunctionSymbol(qualifiedName, returnType, parameters);
    var boundBody = BindStatements(accessor.Body);
    return new BoundFunction(accessor.Span, functionSymbol, boundBody, _scope,
        Array.Empty<string>(), memberKind, className);
}

private BoundFunction BindEventAccessor(
    IReadOnlyList<StatementNode> body, string className, string eventName,
    string accessorKind, string delegateType)
{
    using var _ = PushScope(_scope.CreateChild());

    // Event accessors have an implicit 'value' parameter of the delegate type
    var valueParam = new VariableSymbol("value", delegateType, isMutable: false, isParameter: true);
    _scope.TryDeclare(valueParam);
    var parameters = new List<VariableSymbol> { valueParam };

    var memberKind = accessorKind == "add" ? BoundMemberKind.EventAdd : BoundMemberKind.EventRemove;
    var qualifiedName = $"{className}.{eventName}.{accessorKind}";
    var functionSymbol = new FunctionSymbol(qualifiedName, "VOID", parameters);
    var boundBody = BindStatements(body);
    return new BoundFunction(_scope.Parent!.Span, functionSymbol, boundBody, _scope,
        Array.Empty<string>(), memberKind, className);
}
```

Shared helper:
```csharp
private List<VariableSymbol> BindParameters(IReadOnlyList<ParameterNode> parameters)
{
    var result = new List<VariableSymbol>();
    foreach (var param in parameters)
    {
        var paramSymbol = new VariableSymbol(param.Name, param.TypeName, isMutable: false, isParameter: true);
        if (!_scope.TryDeclare(paramSymbol))
        {
            var suggestedName = GenerateUniqueName(param.Name);
            _diagnostics.ReportDuplicateDefinitionWithFix(param.Span, param.Name, suggestedName);
        }
        result.Add(paramSymbol);
    }
    return result;
}
```

### Change 4: Handle Missing Statement Types in `BindStatement`

**File:** `src/Calor.Compiler/Binding/Binder.cs`

```csharp
private BoundStatement? BindStatement(StatementNode stmt)
{
    return stmt switch
    {
        // Existing handlers...
        CallStatementNode call => BindCallStatement(call),
        ReturnStatementNode ret => BindReturnStatement(ret),
        ForStatementNode forStmt => BindForStatement(forStmt),
        WhileStatementNode whileStmt => BindWhileStatement(whileStmt),
        IfStatementNode ifStmt => BindIfStatement(ifStmt),
        BindStatementNode bind => BindBindStatement(bind),
        BreakStatementNode => new BoundBreakStatement(stmt.Span),
        ContinueStatementNode => new BoundContinueStatement(stmt.Span),
        GotoStatementNode gotoStmt => new BoundGotoStatement(gotoStmt.Span, gotoStmt.Label),
        LabelStatementNode labelStmt => new BoundLabelStatement(labelStmt.Span, labelStmt.Label),
        TryStatementNode tryStmt => BindTryStatement(tryStmt),
        MatchStatementNode matchStmt => BindMatchStatement(matchStmt),
        ProofObligationNode proof => BindProofObligation(proof),

        // New handlers for class member bodies:
        AssignmentStatementNode assign => BindAssignmentStatement(assign),
        CompoundAssignmentStatementNode compound => BindCompoundAssignment(compound),
        ForeachStatementNode forEach => BindForeachStatement(forEach),
        UsingStatementNode usingStmt => BindUsingStatement(usingStmt),
        ThrowStatementNode throwStmt => BindThrowStatement(throwStmt),
        RethrowStatementNode => new BoundCallStatement(stmt.Span, "rethrow", Array.Empty<BoundExpression>()),
        DoWhileStatementNode doWhile => BindDoWhileStatement(doWhile),
        ExpressionStatementNode exprStmt => BindExpressionStatement(exprStmt),
        YieldReturnStatementNode yieldRet => BindYieldReturn(yieldRet),
        YieldBreakStatementNode => new BoundBreakStatement(stmt.Span),
        SyncBlockNode sync => BindSyncBlock(sync),

        // Passthrough nodes — no executable semantics
        FallbackCommentNode => null,
        RawCSharpNode => null,
        PreprocessorDirectiveNode => null,

        // Unknown — explicit unsupported node, NOT null
        _ => BindUnsupportedStatement(stmt)
    };
}
```

### Change 5: `BoundUnsupportedStatement` (Not Null)

**File:** `src/Calor.Compiler/Binding/BoundNodes.cs`

```csharp
/// <summary>
/// Placeholder for statement types the Binder cannot fully bind.
/// Preserved in the bound tree so the CFG and dataflow analyses can account for it.
///
/// IMPORTANT: This is a best-effort model, not a sound one. An opaque statement may
/// throw, return, goto, or mutate arbitrary variables. The CFG models it with both
/// fall-through and function-exit edges. Dataflow treats it as may-define-all-visible
/// and may-use-all-visible. This is conservative: it may suppress dead-store warnings
/// and miss uninitialized-variable reports, but won't produce phantom findings.
///
/// Functions containing BoundUnsupportedStatement have their findings annotated with
/// reduced confidence so downstream consumers can filter accordingly.
/// </summary>
public sealed class BoundUnsupportedStatement : BoundStatement
{
    public string NodeTypeName { get; }
    public BoundUnsupportedStatement(TextSpan span, string nodeTypeName) : base(span)
    {
        NodeTypeName = nodeTypeName;
    }
}
```

And the binder method:
```csharp
private BoundStatement BindUnsupportedStatement(StatementNode stmt)
{
    _diagnostics.ReportInfo(stmt.Span, DiagnosticCode.AnalysisUnsupportedNode,
        $"Statement type '{stmt.GetType().Name}' is not fully supported in analysis; treated as opaque");
    return new BoundUnsupportedStatement(stmt.Span, stmt.GetType().Name);
}
```

`Calor0931` is emitted as **Info** (not Warning) and **deduplicated per NodeTypeName per file** to avoid spam. If a file has 20 `ForeachStatementNode` unsupported nodes, one diagnostic is emitted, not 20.

**Why not null:** Returning `null` removes the statement entirely — `BindStatements` filters nulls. The CFG never sees it, dataflow analyses skip over it, and results are wrong in both directions. `BoundUnsupportedStatement` stays in the tree.

**CFG model for `BoundUnsupportedStatement`:** Two successors — fall-through (normal) and function-exit (may throw/return). This is more conservative than v2's non-branching model, which was incorrectly described as "sound."

**Dataflow model:** May-define-all-visible, may-use-all-visible. This means:
- Dead-store analysis won't call a store dead when the opaque statement might read it
- Uninitialized-variable analysis won't miss defs the opaque statement might perform
- The cost is reduced precision — some real dead stores and uninitialized uses will be missed

This is **best-effort with known limitations**, not sound. Functions containing `BoundUnsupportedStatement` should have their findings flagged with lower confidence.

### Change 6: Add Bound Nodes for New Statement Types

**File:** `src/Calor.Compiler/Binding/BoundNodes.cs`

```csharp
public sealed class BoundAssignmentStatement : BoundStatement
{
    public BoundExpression Target { get; }
    public BoundExpression Value { get; }
    // ...
}

public sealed class BoundCompoundAssignment : BoundStatement
{
    public BoundExpression Target { get; }
    public CompoundAssignmentOperator Operator { get; }
    public BoundExpression Value { get; }
    // ...
}

public sealed class BoundForeachStatement : BoundStatement
{
    public VariableSymbol LoopVariable { get; }
    public BoundExpression Collection { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    // ...
}

public sealed class BoundUsingStatement : BoundStatement
{
    public VariableSymbol? Resource { get; }
    public BoundExpression ResourceExpression { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    // ...
}

public sealed class BoundThrowStatement : BoundStatement
{
    public BoundExpression? Expression { get; }
    // ...
}

public sealed class BoundDoWhileStatement : BoundStatement
{
    public BoundExpression Condition { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    // ...
}

public sealed class BoundExpressionStatement : BoundStatement
{
    public BoundExpression Expression { get; }
    // ...
}
```

### Change 7: Update `VerificationAnalysisPass.ExtractPreconditionGuardedParams`

**File:** `src/Calor.Compiler/Analysis/VerificationAnalysisPass.cs`

Extend to walk class members:

```csharp
private static Dictionary<string, HashSet<string>> ExtractPreconditionGuardedParams(ModuleNode module)
{
    var result = new Dictionary<string, HashSet<string>>();

    // Existing: top-level functions
    foreach (var func in module.Functions)
        ExtractFromPreconditions(func.Name, func.Parameters, func.Preconditions, result);

    // New: class members with preconditions
    foreach (var cls in module.Classes)
    {
        foreach (var method in cls.Methods)
            if (method.Preconditions.Count > 0)
                ExtractFromPreconditions($"{cls.Name}.{method.Name}", method.Parameters, method.Preconditions, result);

        foreach (var ctor in cls.Constructors)
            if (ctor.Preconditions.Count > 0)
                ExtractFromPreconditions($"{cls.Name}..ctor", ctor.Parameters, ctor.Preconditions, result);

        foreach (var op in cls.OperatorOverloads)
            if (op.Preconditions.Count > 0)
                ExtractFromPreconditions($"{cls.Name}.op_{op.Kind}", op.Parameters, op.Preconditions, result);
    }

    return result;
}
```

### Change 8: Guard `ContractInferencePass`

**File:** `src/Calor.Compiler/Analysis/ContractInference/ContractInferencePass.cs`

The pass at line 40-43 iterates `boundModule.Functions` and matches by name against `astModule.Functions`. After this change, `boundModule.Functions` includes class members which have no match in `astModule.Functions`. Without a guard, the pass would run inference on all class members (even those with existing contracts, since the name lookup fails).

```csharp
foreach (var boundFunc in boundModule.Functions)
{
    // Only run contract inference on top-level functions (not class members)
    if (boundFunc.MemberKind != BoundMemberKind.TopLevelFunction)
        continue;

    if (functionsWithContracts.Contains(boundFunc.Symbol.Name))
        continue;

    contractsInferred += InferForFunction(boundFunc);
}
```

### Change 9: Update Downstream Analysis for New Statement Types

#### A. `ControlFlowGraph.cs` — CFG Builder

Add cases in `ProcessStatement`:
- `BoundAssignmentStatement` / `BoundCompoundAssignment` / `BoundExpressionStatement` / `BoundThrowStatement`: Add to current basic block (non-branching). `BoundThrowStatement` terminates the block (no fallthrough).
- `BoundForeachStatement`: Loop structure — header block → body → back edge (like `BoundForStatement`).
- `BoundUsingStatement`: Body block → implicit finally (like `BoundTryStatement` without catch).
- `BoundDoWhileStatement`: Body block first → condition check → back edge to body or exit.
- `BoundUnsupportedStatement`: Add to current basic block with **two successors** — fall-through (normal) and function-exit (may throw/return). Dataflow treats it as may-define-all-visible, may-use-all-visible. This is the conservative model described in Change 5.

#### B. `BoundNodeHelpers.cs` — Variable use/def tracking

`GetDefinedVariable` (currently only handles `BoundBindStatement`) must also handle:
- `BoundAssignmentStatement` → target variable is defined (if target resolves to a variable)
- `BoundCompoundAssignment` → target is both used and defined
- `BoundForeachStatement` → loop variable is defined

`GetUsedVariables` must handle all new statement types by recursively extracting variable references from their sub-expressions.

#### C. Bug Pattern Checkers

The 6 bug pattern checkers in `Analysis/BugPatterns/Patterns/` switch on statement types when walking function bodies. Each needs cases for:
- `BoundAssignmentStatement` — check RHS for division, overflow
- `BoundCompoundAssignment` — check for overflow (especially `/=`)
- `BoundForeachStatement` — walk body
- `BoundDoWhileStatement` — check condition + walk body
- `BoundExpressionStatement` — check expression
- `BoundUnsupportedStatement` — skip (opaque)

#### D. `TaintAnalysis.cs`

`AnalyzeStatement` must handle:
- `BoundAssignmentStatement` — taint propagation from value to target
- `BoundForeachStatement` — taint from collection to loop variable
- `BoundThrowStatement` — taint sink (error messages may leak sensitive data)

### Change 10: New Diagnostic Codes

**File:** `src/Calor.Compiler/Diagnostics/Diagnostic.cs`

| Code | Name | Severity | Message | Dedup |
|------|------|----------|---------|-------|
| `Calor0930` | `AnalysisSkipped` | Warning | "Skipped analysis of '{member}': {reason}" | Per member |
| `Calor0931` | `AnalysisUnsupportedNode` | Info | "Statement type '{type}' not fully supported in analysis" | Per NodeTypeName per file |
| `Calor0932` | `AnalysisICE` | Error | "Internal error analyzing '{member}': {exception}" | Per member |

`Calor0931` is deduplicated per `NodeTypeName` per file to avoid spam. If a file has 20 unsupported `ForeachStatementNode` nodes, one diagnostic is emitted, not 20.

These replace the incorrect reuse of `DiagnosticCode.TypeMismatch` from v1.

---

## Files Changed

| File | Change Type | Description |
|------|-----------|-------------|
| `Binding/Scope.cs` | Small | Add `_overloadSets` dictionary, `DeclareOverload`, `LookupByArity` methods |
| `Binding/Binder.cs` | Major | Add `ScopeRestorer` RAII, `_currentClassName` field, `CreateClassScope`, `RegisterClassMethods`, `BindClassMembers`, `TryBindMember`, member bind methods, `BindParameters` helper; extend `BindStatement` + `BindExpression` switches; add statement/expression binders; convert existing scope save/restore to `PushScope` |
| `Binding/BoundNodes.cs` | Major | Add `BoundMemberKind` enum, `ContainingTypeName`/`MemberKind` to `BoundFunction`; add `BoundAssignmentStatement`, `BoundCompoundAssignment`, `BoundForeachStatement`, `BoundUsingStatement`, `BoundThrowStatement`, `BoundDoWhileStatement`, `BoundExpressionStatement`, `BoundUnsupportedStatement`, `BoundThisExpression`, `BoundBaseExpression`, `BoundFieldAccessExpression`, `BoundNewExpression` |
| `Analysis/VerificationAnalysisPass.cs` | Small | Extend `ExtractPreconditionGuardedParams` to walk class members |
| `Analysis/ContractInference/ContractInferencePass.cs` | Small | Guard iteration to skip non-TopLevelFunction members |
| `Analysis/Dataflow/ControlFlowGraph.cs` | Medium | Handle new bound statement types in CFG builder |
| `Analysis/Dataflow/BoundNodeHelpers.cs` | Medium | Handle new statement types in `GetDefinedVariable` + `GetUsedVariables` |
| `Analysis/Dataflow/Analyses/UninitializedVariablesAnalysis.cs` | Small | Handle new statement types in gen/kill set computation |
| `Analysis/Dataflow/Analyses/LiveVariablesAnalysis.cs` | Small | Handle new statement types in liveness computation |
| `Analysis/BugPatterns/Patterns/*.cs` | Small each | Add cases for new bound statement types in body walkers |
| `Analysis/Security/TaintAnalysis.cs` | Small | Handle new statement types in `AnalyzeStatement` |
| `Diagnostics/Diagnostic.cs` | Small | Add `Calor0930`, `Calor0931`, `Calor0932` codes |

---

## Implementation Order

### Phase 0: Infrastructure (PREREQUISITE)

**Goal:** Build the scope, expression, and metadata foundation that all member binding depends on.

1. Add `ScopeRestorer` RAII pattern to Binder; convert existing 6 scope save/restore sites to use `PushScope`.
2. Add `DeclareOverload` / `LookupByArity` to `Scope` for arity-aware overload sets.
3. Add `BoundMemberKind` enum and `ContainingTypeName`/`MemberKind` fields to `BoundFunction` (with backward-compatible defaults: `TopLevelFunction` / `null`).
4. Add `BoundUnsupportedStatement` node type with may-define-all/may-use-all/may-terminate CFG model.
5. Add `BoundThisExpression`, `BoundBaseExpression`, `BoundFieldAccessExpression`, `BoundNewExpression` expression types.
6. Add `BindExpression` cases for `ThisExpressionNode`, `BaseExpressionNode`, `FieldAccessNode`, `NewExpressionNode`.
7. Add `CreateClassScope` (field binding) and `RegisterClassMethods` (arity-aware method registration).
8. Add `_currentClassName` tracking field to Binder.
9. Add diagnostic codes `Calor0930`, `Calor0931`, `Calor0932` with deduplication for `Calor0931`.
10. Guard `ContractInferencePass` to skip non-TopLevelFunction members.

**Validation:** Existing tests pass. No behavioral change yet (no class members are bound). Scope RAII refactor is verified by existing test suite.

### Phase 1: Methods (P0)

**Goal:** Bind method bodies and run analysis on them.

1. Add missing statement type handlers to `BindStatement` — `AssignmentStatementNode`, `CompoundAssignmentStatementNode`, `ForeachStatementNode`, `ThrowStatementNode`, `ExpressionStatementNode`, `UsingStatementNode`, `DoWhileStatementNode`. Use `BoundUnsupportedStatement` for any remaining unrecognized types.
2. Add corresponding `BoundNode` types to `BoundNodes.cs`.
3. Add `BindMethod` to `Binder.cs`.
4. Add `BindClassMembers` (methods only) to `Binder.Bind()`.
5. Add `TryBindMember` for resilient per-member binding.
6. Update `ControlFlowGraph` builder for new bound statement types.
7. Update `BoundNodeHelpers` for new statement types.
8. Update dataflow analyses for new statement types.
9. Update `ExtractPreconditionGuardedParams` for class methods.

**Validation:** Run `calor --analyze --permissive-effects` on Newtonsoft.Json. Expect non-zero "functions analyzed" and the method count in diagnostics to reflect `BoundMemberKind.Method`.

### Phase 2: Constructors (P0)

**Goal:** Bind constructor bodies with field scope.

1. Add `BindConstructor` to `Binder.cs` (initializer is skipped — see Known Gaps).
2. Wire constructors into `BindClassMembers`.

**Validation:** Run on Newtonsoft.Json constructors. Verify that field assignments (`§ASSIGN _writer writer`) resolve without Calor0900 false positives (because `_writer` is in class scope).

### Phase 3: Properties + Operators (P1)

1. Add `BindPropertyAccessor` and `BindOperator`.
2. Wire into `BindClassMembers`.
3. Handle implicit `value` parameter for setters/initers.

**Validation:** Run on Humanizer's `ByteSize.calr` — property accessors with division should trigger the div-by-zero checker.

### Phase 4: Indexers + Events (P2)

1. Add `BindIndexerAccessor` and `BindEventAccessor`.
2. Wire into `BindClassMembers`.
3. Handle implicit `value` parameter for event accessors.

**Validation:** Confirm indexer/event bodies are analyzed without crashes.

### Phase 5: Downstream Analysis Updates

1. Update all 6 bug pattern checkers for new statement types.
2. Update `TaintAnalysis.AnalyzeStatement` for new statement types.
3. Run full test suite.

**Validation:** All existing tests pass. New tests for each statement type + member type added.

### Phase 6: Corpus Scan

1. Run `calor --analyze --permissive-effects` across all 51 projects (use `VerificationAnalysisOptions.Fast` first — no Z3).
2. Collect and categorize findings by code (Calor0900-0931 + Calor0980-0984).
3. Assess false positive rate — especially Calor0900 on field references.
4. Selectively run `.Thorough` (with Z3) on promising files.
5. Cross-reference findings with original C# to classify true positives.
6. Produce report: "X real bugs found across Y projects that C# alone missed."

---

## Risks and Mitigations

### Risk 1: False positives from field references (Calor0900)

Even with class fields in scope, the uninitialized variable checker may flag fields that were initialized in a parent constructor (via `: base(...)`) or via field initializers.

**Mitigation:** Fields declared with `DefaultValue` should be marked as initialized in the class scope. Fields without initializers that appear in constructor bodies should be treated as "possibly initialized" (not flagged). If false positive rate is high, consider disabling Calor0900 for class members in the initial release and enabling after tuning.

### Risk 2: Residual overload imprecision

Arity-aware lookup eliminates most overload mismatches, but when multiple overloads share the same arity, the first registered wins. The overflow checker uses `TypeName` to determine Z3 bit-vector widths (`OverflowChecker.cs:311`, `ContractTranslator.cs:485-505`), so a wrong return type produces wrong SMT encodings.

**Mitigation:** Same-arity collisions are less common than same-name collisions. Monitor false positive rate from residual type imprecision in corpus scan. If problematic, upgrade to type-signature-based resolution (requires Parser changes to `callExpr.Target`).

### Risk 3: Analysis performance at scale

262K files with Z3 verification could be slow. Each file now has many more functions to analyze.

**Mitigation:** Phase 6 uses `VerificationAnalysisOptions.Fast` (no Z3) for initial scan. The existing verification cache helps for repeated runs. Add a `--max-functions-per-file` option if needed.

### Risk 4: Binder crashes on unexpected AST patterns

Converted code may produce AST shapes the Binder doesn't expect.

**Mitigation:** `TryBindMember` catches exceptions per member, emits `Calor0930` diagnostic with member identity, and does NOT add partially-bound members to the functions list. This ensures one bad method doesn't prevent analyzing the rest of the class.

### Risk 5: `BoundUnsupportedStatement` in CFG

Opaque unsupported statements in the CFG with may-define-all/may-use-all/may-terminate semantics will reduce analysis precision. Dead-store detection is suppressed when an opaque node may read the store. Uninitialized-variable detection is suppressed when an opaque node may define the variable.

**Mitigation:** This is best-effort, not sound — findings from functions containing `BoundUnsupportedStatement` are annotated with reduced confidence. The `Calor0931` diagnostic (deduplicated per NodeTypeName per file) flags which statement types are unsupported so we can prioritize adding support. The goal is to drive the unsupported count to zero for common statement types before the corpus scan.

### Risk 6: Memory footprint at scale

Binding ~150K class members across the 51-project corpus means ~150K `BoundFunction` objects (each with a `Scope`, bound statements, and symbol tables) resident during analysis.

**Mitigation:** Analysis is per-file, not per-corpus — `BoundFunction` objects are GC-eligible after each file completes. The concern is per-file peak: a large file like `SqlMapper.calr` with hundreds of methods could create significant per-file allocations. Monitor peak memory during Phase 6 corpus scan. If problematic, bind and analyze methods one at a time rather than accumulating all into `BoundModule.Functions`.

---

## Success Criteria

1. **Quantitative:** Running `calor --analyze` on Newtonsoft.Json reports >0 methods analyzed, with per-member-kind counts in the output.
2. **Non-regression:** All existing tests in `Calor.Compiler.Tests`, `Calor.Semantics.Tests`, and `Calor.Verification.Tests` continue to pass.
3. **Precision:** Calor0900 (uninitialized variable) false positive rate on class members is <10% (measured by manual review of a 50-finding sample). If rate exceeds 10%, gate Calor0900 for class members behind `--analyze-class-members` opt-in flag before shipping.
4. **Scale:** Analysis completes on at least the top 10 projects (by file count) without crashing.
5. **Findings quality:** At least 20 verified true-positive bug findings across the top-10 projects that correspond to real issues in the original C# code.

---

## Testing Strategy

### Unit Tests

New tests in `Calor.Semantics.Tests` for:
- Binding a module with class methods → `BoundModule.Functions` includes them with `MemberKind == Method`
- Binding method with sibling method calls → call resolves with correct return type
- Binding constructors with field assignment → `_field` resolves from class scope, no Calor0900
- Binding constructor with `: base(...)` → initializer is skipped, body binds correctly
- Binding property getter/setter with implicit `value` parameter
- Binding operator overloads
- Binding indexer with parameters + implicit `value`
- Binding event add/remove accessors with implicit `value`
- Field references via bare name resolve to class field symbol
- `§THIS` expression binds to `BoundThisExpression`, not `BoundIntLiteral(0)`
- `§NEW{}` expression binds to `BoundNewExpression`
- Nested class qualified naming
- Overloaded methods → arity-aware lookup picks correct overload by arg count; same-arity falls back to first
- Each new statement type binder (assignment, compound assignment, foreach, using, throw, do-while, expression statement)
- Unknown statement type → `BoundUnsupportedStatement` + `Calor0931` diagnostic (not crash, not null)
- Failed member binding → `Calor0930` diagnostic, member NOT in functions list
- `ContractInferencePass` skips class members (only infers for top-level functions)
- Analysis counts distinguish methods/ctors/accessors from functions

### Analysis Regression Tests

- Assignment updates uninitialized-variable analysis correctly (defined variable tracked)
- Assignment affects live-variable/dead-store analysis correctly
- Throw/rethrow terminate CFG blocks (no fallthrough)
- Using body is modeled as try/finally in CFG
- Foreach/do-while loop back-edges are correct
- `BoundUnsupportedStatement` appears as opaque block in CFG with two successors (fall-through + exit)
- `BoundUnsupportedStatement` triggers may-define-all/may-use-all in dataflow (dead stores not falsely reported)

### Integration Tests

- Run `calor --analyze` on a `.calr` file with a class method containing obvious division by zero → assert Calor0920
- Run on a file with `§THIS` usage → no crash, correct binding
- Run on a file with overloaded methods → no crash, analysis completes

### Snapshot Tests

- No changes needed to existing snapshot tests (they test code generation, not analysis)
