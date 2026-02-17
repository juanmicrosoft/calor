# calor coverage

Analyze a C# file for Calor conversion coverage and blockers. This command helps AI coding agents understand what percentage of a file can be converted and what blockers exist.

## Usage

```bash
calor coverage <file.cs> [options]
```

## Options

| Option | Description |
|--------|-------------|
| `--verbose`, `-v` | Include detailed dimension scores and examples |

## Examples

### Basic coverage analysis

```bash
calor coverage MyClass.cs
```

Output:
```json
{
  "file": "/path/to/MyClass.cs",
  "success": true,
  "coveragePercent": 100,
  "migrationScore": 45.2,
  "priority": "medium",
  "isConvertible": true,
  "lineCount": 150,
  "methodCount": 8,
  "typeCount": 2,
  "blockers": []
}
```

### Analysis with blockers

```bash
calor coverage FileWithBlockers.cs
```

Output:
```json
{
  "file": "/path/to/FileWithBlockers.cs",
  "success": true,
  "coveragePercent": 37,
  "migrationScore": 12.5,
  "priority": "low",
  "isConvertible": false,
  "lineCount": 200,
  "methodCount": 10,
  "typeCount": 3,
  "blockers": [
    {
      "name": "named-argument",
      "description": "Named arguments (param: value) not yet supported",
      "count": 5,
      "examples": ["name: ...", "description: ..."]
    },
    {
      "name": "primary-constructor",
      "description": "Primary constructors (class Foo(int x)) not yet supported",
      "count": 1,
      "examples": ["class Config(string path)"]
    }
  ]
}
```

### Verbose output with dimensions

```bash
calor coverage MyClass.cs --verbose
```

Output includes dimension scores:
```json
{
  "file": "/path/to/MyClass.cs",
  "success": true,
  "coveragePercent": 100,
  "migrationScore": 65.3,
  "priority": "high",
  "isConvertible": true,
  "blockers": [],
  "dimensions": {
    "ContractPotential": {
      "score": 80.5,
      "weight": 0.2,
      "patternCount": 12,
      "examples": ["throw new ArgumentNullException(...)", "if (...) throw validation"]
    },
    "EffectPotential": {
      "score": 45.0,
      "weight": 0.15,
      "patternCount": 5,
      "examples": ["File.ReadAllText", "Console.WriteLine"]
    },
    "NullSafetyPotential": {
      "score": 70.2,
      "weight": 0.2,
      "patternCount": 15,
      "examples": ["string?", "== null / != null check", "?? operator"]
    }
  }
}
```

## Output Fields

| Field | Description |
|-------|-------------|
| `file` | Absolute path to the analyzed file |
| `success` | Whether analysis completed successfully |
| `coveragePercent` | Percentage of code that can be converted (0-100) |
| `migrationScore` | Migration priority score based on Calor benefit potential (0-100) |
| `priority` | Migration priority: `low`, `medium`, `high`, `critical` |
| `isConvertible` | True if no blockers exist |
| `lineCount` | Number of lines in the file |
| `methodCount` | Number of methods detected |
| `typeCount` | Number of types (classes, interfaces, etc.) detected |
| `blockers` | List of unsupported constructs that block conversion |

## Blocker Types

All blockers that can prevent conversion, organized by category:

### Pattern Matching

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `relational-pattern` | `is > x`, `is < x` patterns | Use explicit comparison expressions |
| `compound-pattern` | `and`/`or` patterns | Use explicit boolean expressions |
| `declaration-pattern` | `is Type varName` | Check type and cast separately |
| `list-pattern` | `[a, b, ..rest]` patterns | Use explicit indexing and Length checks |

### Type System

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `primary-constructor` | `class Foo(int x)` | Use traditional constructor |
| `generic-type-constraint` | `where T : class` | Remove constraints or add runtime checks |
| `nested-generic-type` | `Expression<Func<T, U>>` | Simplify generic nesting |
| `ref-struct` | `ref struct` types | Use regular struct or class |
| `readonly-struct` | `readonly struct` types | Use regular struct |

### Parameters and Arguments

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `ref-parameter` | `out`/`ref` params | Return tuples or Result<T,E> |
| `in-parameter` | `in` params (readonly ref) | Pass by value or use ref |
| `named-argument` | `param: value` | Use positional arguments |
| `out-var` | `out var x` inline declarations | Declare variable before call |
| `scoped-parameter` | `scoped` params/locals | Remove scoped keyword |

### Expressions and Statements

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `target-typed-new` | `new()` without type | Use `new TypeName()` |
| `range-expression` | `0..5`, `..5` | Use explicit bounds |
| `index-from-end` | `^1` | Use `array.Length - 1` |
| `throw-expression` | `?? throw new` | Use if-throw statement |
| `with-expression` | `record with { }` | Create new instance manually |
| `collection-expression` | `[1, 2, 3]` (C# 12) | Use `new[] { 1, 2, 3 }` |
| `null-conditional-method` | `obj?.Method()` | Use explicit null check |

### Control Flow

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `yield-return` | Iterator methods | Return explicit List<T> |
| `goto` | Goto statements | Use structured control flow |
| `labeled-statement` | Label: statements | Use structured control flow |
| `checked-block` | `checked`/`unchecked` blocks | Remove wrapper, handle overflow manually |
| `lock-statement` | `lock (obj)` | Use Monitor.Enter/Exit |
| `await-foreach` | `await foreach` | Enumerate async enumerable manually |
| `await-using` | `await using` | Use try/finally with DisposeAsync |

### Properties and Members

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `init-accessor` | `{ get; init; }` | Use regular set or constructor |
| `required-member` | `required` members | Use constructor parameters |
| `static-abstract-member` | `static abstract` in interfaces | Use instance or static methods |

### Operators and Conversions

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `operator-overload` | Custom operators | Define explicit methods |
| `implicit-conversion` | Implicit operators | Use explicit conversion methods |

### Unsafe Code

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `unsafe` | Unsafe blocks/methods | Use safe alternatives |
| `pointer` | Pointer types | Use safe alternatives |
| `stackalloc` | Stack allocation | Use regular array |
| `fixed` | Fixed buffers | Use regular arrays |
| `volatile` | Volatile fields | Use synchronization primitives |

### C# 11-13 Features

| Blocker | Description | Workaround |
|---------|-------------|------------|
| `default-lambda-parameter` | `(int x = 5) => x` (C# 12) | Use overloads or null checks |
| `file-scoped-type` | `file class` (C# 11) | Use internal or nested types |
| `utf8-string-literal` | `"text"u8` (C# 11) | Use `Encoding.UTF8.GetBytes()` |
| `generic-attribute` | `[Attr<T>]` (C# 11) | Use typeof() in non-generic attribute |
| `using-type-alias` | `using Point = (int, int)` (C# 12) | Define explicit record/class types |

## Use in AI Agents

Recommended workflow:

1. **Check coverage first**:
   ```bash
   calor coverage InputFile.cs
   ```

2. **If `isConvertible` is false**, review blockers and rewrite C# code to avoid unsupported features

3. **Use `calor feature-check`** to understand each blocker's workaround:
   ```bash
   calor feature-check named-argument
   ```

4. **Re-check coverage** after rewriting to confirm all blockers are resolved

5. **Proceed with conversion** using `calor convert`:
   ```bash
   calor convert InputFile.cs
   ```

## Dimension Scoring (--verbose)

The verbose output includes 6 scoring dimensions that indicate how much the file would benefit from Calor features:

| Dimension | Weight | What it measures |
|-----------|--------|------------------|
| `ContractPotential` | 20% | Argument validation, preconditions |
| `EffectPotential` | 15% | I/O, network, database calls |
| `NullSafetyPotential` | 20% | Nullable types, null checks |
| `ErrorHandlingPotential` | 20% | Try/catch, exceptions |
| `PatternMatchPotential` | 10% | Switch statements, pattern matching |
| `ApiComplexityPotential` | 15% | Public APIs lacking documentation |

Higher scores indicate more opportunity to benefit from Calor's features (contracts, effects, Option<T>, Result<T,E>).
