# OPAL Converter Gaps

This document tracks C# language features that the OPAL converter does not yet support.
The MigrationAnalyzer now detects these constructs and applies score penalties to filter
out files that use them.

## Unsupported C# Constructs

### High Priority

#### Switch Expressions (C# 8+)
```csharp
// Not supported:
var result = gender switch
{
    Gender.Male => "Mr.",
    Gender.Female => "Ms.",
    _ => ""
};
```
**Workaround:** Use switch statements instead (fully supported).

**Implementation Notes:** Requires OPAL syntax for inline switch expressions, possibly:
```opal
§X{sw001:gender}
  §WC Male => "Mr."
  §WC Female => "Ms."
  §WC _ => ""
§/X{sw001}
```

#### Relational Patterns (C# 9+)
```csharp
// Not supported:
if (value is > 1000 and < 2000) { ... }
if (age is >= 18) { ... }
```
**Workaround:** Use explicit comparison operators: `if (value > 1000 && value < 2000)`

**Implementation Notes:** Requires OPAL pattern syntax for relational operators.

#### Compound Patterns (C# 9+)
```csharp
// Not supported:
if (obj is string { Length: > 0 } or int { } n when n > 0) { ... }
if (value is > 0 and < 100) { ... }
```
**Workaround:** Use explicit boolean expressions.

#### Target-Typed New (C# 9+)
```csharp
// Not supported:
ResourceManager manager = new("Name", assembly);
```
**Workaround:** Use explicit type: `new ResourceManager("Name", assembly)`

**Implementation Notes:** Need to preserve target type from context during conversion.

#### Null-Conditional Method Calls (C# 6+)
```csharp
// Not supported (method call version):
var result = obj?.GetValue();
var result = resourceSet?.GetString(key);
```
**Workaround:** Use explicit null checks.

**Implementation Notes:** Need proper OPAL S-expression syntax for null-conditional operations.

#### Named Arguments
```csharp
// Not supported:
Method(createIfNotExists: true, tryParents: false);
```
**Workaround:** Use positional arguments (may require reordering).

**Implementation Notes:** Named arguments should be converted to positional in OPAL.

#### Primary Constructors (C# 12+)
```csharp
// Not supported:
internal class DefaultWordsToNumberConverter(CultureInfo culture) : BaseClass
{
    private readonly CultureInfo cultureInfo = culture;
}
```
**Workaround:** Use explicit constructor.

**Implementation Notes:** Primary constructor parameters must be converted to CTOR with parameters.

#### Out/Ref Parameters
```csharp
// Not supported:
public bool TryConvert(string input, out int result) { ... }
// And inline declarations:
TryConvert(input, out var result);
```
**Workaround:** Refactor to use return types (Result<T, E> pattern).

**Implementation Notes:** Need OPAL syntax for reference parameters and inline out declarations.

#### Declaration Patterns (C# 7+)
```csharp
// Not supported:
if (expression is UnaryExpression unary)
{
    // use unary
}
```
**Workaround:** Use explicit type checks and casts.

**Implementation Notes:** Need to generate proper OPAL pattern matching with variable binding.

#### Nested Generic Types
```csharp
// Not supported:
Expression<Func<T, TProp>> expression;
Action<Dictionary<string, List<int>>> handler;
```
**Workaround:** Simplify generic nesting or use intermediate types.

**Implementation Notes:** Type parameter parsing needs to handle recursive generic arguments.

#### Lambda Expressions
```csharp
// Not supported (in complex contexts):
var action = (T instance, object value) => { setter(instance, (V)value); };
Expression<Func<T, bool>> predicate = x => x.IsActive;
```
**Workaround:** Use named methods instead of inline lambdas.

**Implementation Notes:** Lambda syntax needs proper OPAL representation (maybe arrow functions).

#### Throw Expressions (C# 7+)
```csharp
// Not supported:
Name = value ?? throw new ArgumentNullException(nameof(value));
```
**Workaround:** Use explicit if-throw pattern.

**Implementation Notes:** Throw expressions need proper OPAL representation in S-expression context.

#### Generic Type Constraints
```csharp
// Not supported:
public class UnitDefinition<TUnit> where TUnit : struct, Enum { }
```
**Workaround:** Remove constraints or use simpler patterns.

**Implementation Notes:** Constraint syntax needs OPAL §WHERE tag enhancement.

### Medium Priority

#### Range Expressions (C# 8+)
```csharp
// Not supported:
var slice = array[0..5];
var last = array[^1];
```
**Workaround:** Use explicit Substring/Take/Skip calls.

**Implementation Notes:** Requires OPAL range syntax, possibly `[0..5]` or `{0 TO 5}`.

#### Index From End (C# 8+)
```csharp
// Not supported:
var lastItem = items[^1];
var secondLast = items[^2];
```
**Workaround:** Use `items[items.Length - 1]` or `items.Last()`.

### Low Priority (Less Common)

#### List Patterns (C# 11+)
```csharp
// Not supported:
if (list is [var first, .., var last]) { ... }
```

#### Raw String Literals (C# 11+)
```csharp
// Not supported (multi-line):
var json = """
    {
        "name": "test"
    }
    """;
```

#### Collection Expressions in Complex Contexts (C# 12+)
```csharp
// Partially supported - basic [a, b, c] works, but spread operator doesn't:
int[] combined = [..first, ..second];
```

## Implementation Roadmap

1. **Phase 1:** Switch expressions - highest impact for real-world code
2. **Phase 2:** Relational and compound patterns - common in validation logic
3. **Phase 3:** Range/Index expressions - useful but less critical
4. **Phase 4:** C# 11+ features - as adoption increases

## Testing Strategy

When implementing support for these features:
1. Add unit tests in `tests/Opal.Compiler.Tests/`
2. Add real-world examples in E2E tests
3. Update the MigrationAnalyzer to remove the penalty for the newly supported construct

## Related Files

- `src/Opal.Compiler/Analysis/MigrationAnalyzer.cs` - Detects unsupported constructs
- `src/Opal.Compiler/Migration/RoslynSyntaxVisitor.cs` - Main converter (needs updates)
- `src/Opal.Compiler/CodeGen/CSharpEmitter.cs` - Emits back to C# (needs updates)
