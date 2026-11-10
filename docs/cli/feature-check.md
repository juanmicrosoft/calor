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
the feature payload lives under `data`:

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
    "description": "A property or indexer whose accessor uses yield return/yield break is preserved verbatim as converter-owned C# interop; Calor has no iterator accessors (Calor0209)",
    "alternative": "Move the iterator body into a method and have the accessor return its result, or let the converter preserve the original C# member"
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

Selected current registry entries and their boundaries:

| Feature | Level | Notes |
|---------|-------|-------|
| `ref-parameter`, `out-parameter`, `out-var` | Partial | Supported by-reference arguments use `ref value`, `out value`, and `out _`. Ref locals, ref returns, method-generic `out T`, and `out var` outside supported statement contexts stay C#. `[NotNullWhen]` is copied but inert |
| `in-parameter` | Full | Call sites that omit `in`, or pass an operand that runs code, keep their statement as C# |
| `string-interpolation` | Partial | Not `Full`: `[InterpolatedStringHandler]` targets and hole-free `FormattableString` targets have no Calor form and stay C# |
| `local-function` | Partial | A bounded non-capturing, non-generic slice becomes a typed lambda binding at its original declaration point. Forward/mutual calls, captures, unsupported recursive bodies and unsupported signatures preserve the containing C# member |
| `iterator-accessor`, `partial-property` | NotSupported | The original C# member is preserved; iterator *methods* (`yield-return`) have native syntax |
| `string-interpolation-handler` | NotSupported | Keep the call as C#, or pass an already formatted string to a `string` overload |
| `array-element-block` | NotSupported | A collection-initializer element, or a multi-line element in a rectangular array row, keeps its statement as C# |

A registry level describes conversion support, not whole-program acceptance or
an effect/proof guarantee. Validate the output with its original references,
parse options and runtime oracles. Preserved C# is not native coverage.

## Use in AI Agents

Before attempting to convert C# code to Calor, agents should:

1. Check if specific features are supported:
   ```bash
   calor feature-check iterator-accessor
   ```

2. If unsupported, use the `alternative` field to rewrite the code first:
   ```json
   {"alternative": "Move the iterator body into a method and have the accessor return its result, or let the converter preserve the original C# member"}
   ```

3. For bulk checking, use `--list` to get all unsupported features upfront.
