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

### Check if iterator accessors are supported

```bash
calor feature-check iterator-accessor
```

Output is wrapped in the [envelope schema 2.0](/calor/cli/envelope-schema/);
the feature payload lives under `data` (0.25 output, `§` shown unescaped):

```json
{
  "version": "2.0",
  "command": "feature-check",
  "diagnostics": [],
  "summary": { "total": 0, "errors": 0, "warnings": 0, "info": 0 },
  "data": {
    "feature": "iterator-accessor",
    "found": true,
    "supported": false,
    "supportLevel": "notsupported",
    "description": "A property or indexer whose accessor uses yield return/yield break is preserved verbatim as a §CSHARP interop block; Calor has no iterator accessors (Calor0209)",
    "alternative": "Move the iterator body into a method (§MT with §YIELD) and have the accessor return its result, or keep the member as a §CSHARP interop block"
  }
}
```

### Check if async-await is supported

```bash
calor feature-check async-await
```

`data` payload (envelope wrapper elided):

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

`data` payload (envelope wrapper elided):

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

The registry in `src/Calor.Compiler/Migration/FeatureSupport.cs` is the source of truth;
run `calor feature-check --list` (optionally with `--level`) for the current level and
description of every feature. This page does not copy the full list, because a copy drifts.

The interop features the 0.25 converter changes, as the registry reports them:

| Feature | Level | Notes |
|---------|-------|-------|
| `ref-parameter`, `out-parameter`, `out-var` | Partial | By-reference arguments convert natively (`§A{ref}`, `§A{out}`, `out _`). Ref locals, ref returns, method-generic `out T`, and `out var` outside an expression statement, local declaration, `return` or `if` condition stay C#. `[NotNullWhen]` is copied but inert |
| `in-parameter` | Full | Call sites that omit `in`, or pass an operand that runs code, keep their statement as C# |
| `string-interpolation` | Partial | Not `Full`: `[InterpolatedStringHandler]` targets and hole-free `FormattableString` targets have no Calor form and stay C# |
| `local-function` | Partial | A bounded slice of non-capturing local functions becomes a nested `§F`; any other keeps its member as `§CSHARP` |
| `iterator-accessor`, `partial-property` | NotSupported | The member stays `§CSHARP`; iterator *methods* (`yield-return`) are Full |
| `string-interpolation-handler` | NotSupported | Keep the call as C#, or pass an already formatted string to a `string` overload |
| `array-element-block` | NotSupported | A collection-initializer element, or a multi-line element in a rectangular array row, keeps its statement as C# |

One registry entry does not match 0.25 behavior: `yield-return` (methods only). The
website's `calor feature-check` page lists it as a known gap.

## Use in AI Agents

Before attempting to convert C# code to Calor, agents should:

1. Check if specific features are supported:
   ```bash
   calor feature-check iterator-accessor
   ```

2. If unsupported, use the `alternative` field to rewrite the code first:
   ```json
   {"alternative": "Move the iterator body into a method (§MT with §YIELD) and have the accessor return its result, or keep the member as a §CSHARP interop block"}
   ```

3. For bulk checking, use `--list` to get all unsupported features upfront.
