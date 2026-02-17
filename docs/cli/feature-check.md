# calor feature-check

Check if a C# feature is supported in Calor conversion. This command is designed for AI coding agents to query feature compatibility before attempting conversion.

## Usage

```bash
# Check a specific feature
calor feature-check <feature-name>

# List all features
calor feature-check --list

# List features filtered by support level
calor feature-check --list --level notsupported
```

## Options

| Option | Description |
|--------|-------------|
| `--list`, `-l` | List all known features and their support levels |
| `--level` | Filter by support level: `full`, `partial`, `notsupported`, `manualrequired` |

## Examples

### Check if yield-return is supported

```bash
calor feature-check yield-return
```

Output:
```json
{
  "feature": "yield-return",
  "found": true,
  "supported": false,
  "supportLevel": "notsupported",
  "description": "Yield return (iterator methods) is not supported",
  "alternative": "Use explicit List<T> construction and return the complete list"
}
```

### Check if async-await is supported

```bash
calor feature-check async-await
```

Output:
```json
{
  "feature": "async-await",
  "found": true,
  "supported": true,
  "supportLevel": "full",
  "description": "Async/await is converted to Calor async functions"
}
```

### List all unsupported features

```bash
calor feature-check --list --level notsupported
```

### Check an unknown feature

```bash
calor feature-check some-unknown-feature
```

Output:
```json
{
  "feature": "some-unknown-feature",
  "found": false,
  "alternative": "Feature not in registry. It may be supported (basic C# features work), or check documentation."
}
```

## Support Levels

| Level | Description |
|-------|-------------|
| `full` | Feature is fully supported with direct mapping to Calor |
| `partial` | Feature is supported but may need manual review |
| `notsupported` | Feature cannot be converted automatically |
| `manualrequired` | Feature requires manual intervention to convert |

## Feature Reference

### Fully Supported Features

| Feature | Description |
|---------|-------------|
| `class`, `interface`, `struct`, `record`, `enum` | Type definitions |
| `method`, `property`, `field`, `constructor` | Members |
| `if`, `for`, `foreach`, `while`, `switch` | Control flow |
| `try-catch` | Exception handling |
| `async-await` | Async functions |
| `lambda` | Lambda expressions |
| `generics` | Generic types and methods |
| `pattern-matching-basic` | Type, constant, var patterns |
| `string-interpolation` | String formatting |
| `null-coalescing`, `null-conditional` | Null operators |

### Partially Supported Features

| Feature | Description |
|---------|-------------|
| `linq-method`, `linq-query` | LINQ (may need review) |
| `ref-parameter`, `out-parameter` | Reference parameters (kept with warning) |
| `pattern-matching-advanced` | Advanced patterns (may be simplified) |
| `attributes` | Converted to comments |
| `dynamic` | Converted to 'any' with warning |
| `is-type-pattern` | Type patterns (declaration patterns unsupported) |
| `nested-generic-type` | May have issues |

### Not Supported Features

| Feature | Workaround |
|---------|------------|
| `yield-return` | Use explicit List<T> construction |
| `primary-constructor` | Use traditional constructor |
| `relational-pattern`, `compound-pattern` | Use explicit comparisons |
| `generic-type-constraint` | Remove or add runtime checks |
| `range-expression`, `index-from-end` | Use explicit bounds |
| `target-typed-new` | Use `new TypeName()` |
| `null-conditional-method` | Use explicit null check |
| `named-argument` | Use positional arguments |
| `declaration-pattern` | Check type and cast separately |
| `throw-expression` | Use if-throw statement |
| `out-var` | Declare variable before call |
| `in-parameter` | Pass by value or use ref |
| `checked-block` | Remove wrapper |
| `with-expression` | Create new instance manually |
| `init-accessor` | Use regular set or constructor |
| `required-member` | Use constructor parameters |
| `list-pattern` | Use explicit indexing |
| `static-abstract-member` | Use instance/static methods |
| `ref-struct`, `readonly-struct` | Use regular struct |
| `lock-statement` | Use Monitor.Enter/Exit |
| `await-foreach` | Enumerate manually |
| `await-using` | Use try/finally with DisposeAsync |
| `scoped-parameter` | Remove scoped keyword |
| `collection-expression` | Use array/list constructor |
| `goto`, `labeled-statement` | Use structured control flow |
| `unsafe`, `pointer`, `stackalloc`, `fixed`, `volatile` | Use safe alternatives |
| `default-lambda-parameter` | Use overloads or null checks |
| `file-scoped-type` | Use internal or nested types |
| `utf8-string-literal` | Use `Encoding.UTF8.GetBytes()` |
| `generic-attribute` | Use typeof() in non-generic attribute |
| `using-type-alias` | Define explicit record/class types |

### Manual Required Features

| Feature | Workaround |
|---------|------------|
| `extension-method` | Convert to instance methods or traits |
| `operator-overload` | Define explicit methods |
| `implicit-conversion`, `explicit-conversion` | Use conversion methods |
| `equals-operator` | Define Equals method |

## Use in AI Agents

Before attempting to convert C# code to Calor, agents should:

1. Check if specific features are supported:
   ```bash
   calor feature-check yield-return
   ```

2. If unsupported, use the `alternative` field to rewrite the code first:
   ```json
   {"alternative": "Use explicit List<T> construction and return the complete list"}
   ```

3. For bulk checking, use `--list` to get all unsupported features upfront.
