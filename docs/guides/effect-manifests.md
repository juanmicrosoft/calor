# Effect Manifests

Effect manifests allow you to declare effects for .NET types that Calor calls. This enables the Calor compiler to verify effect declarations even when your code calls external .NET libraries.

## Overview

When Calor code calls a .NET method, the compiler needs to know what effects that method may have. Effect manifests provide this information through JSON files that map types and methods to their effects.

## Effect Taxonomy

### Console I/O
- `cw` - Console write
- `cr` - Console read

### Filesystem Effects
- `fs:r` - Filesystem read
- `fs:w` - Filesystem write
- `fs:rw` - Filesystem read and write (encompasses `fs:r` and `fs:w`)

### Network Effects
- `net:r` - Network read
- `net:w` - Network write
- `net:rw` - Network read and write (encompasses `net:r` and `net:w`)

### Database Effects
- `db:r` - Database read
- `db:w` - Database write
- `db:rw` - Database read and write (encompasses `db:r` and `db:w`)

### Environment Effects
- `env:r` - Environment variable read
- `env:w` - Environment variable write

### System Effects
- `proc` - Process operations
- `alloc` - Memory allocation
- `unsafe` - Unsafe memory operations

### Nondeterminism
- `time` - Time-dependent operations
- `rand` - Random number generation

### Mutation/Exception
- `mut` - Heap mutation
- `throw` - Exception throwing

## Effect Subtyping

Broader effects encompass narrower ones:

- `fs:rw` encompasses `fs:r` and `fs:w`
- `net:rw` encompasses `net:r` and `net:w`
- `db:rw` encompasses `db:r` and `db:w`
- `env:rw` encompasses `env:r` and `env:w` (if declared manually)

This means if you declare `fs:rw` on a function, it can call methods that require only `fs:r` or `fs:w`.

## Manifest JSON Schema

```json
{
  "version": "1.0",
  "description": "Optional description",
  "mappings": [
    {
      "type": "Namespace.TypeName",
      "defaultEffects": ["effect1", "effect2"],
      "methods": {
        "MethodName": ["effect1"],
        "*": ["fallback_effect"]
      },
      "getters": {
        "PropertyName": ["effect"]
      },
      "setters": {
        "PropertyName": ["effect"]
      },
      "constructors": {
        "(ParamType1,ParamType2)": ["effect"],
        "()": []
      }
    }
  ],
  "namespaceDefaults": {
    "Namespace.Pattern": ["default_effect"]
  }
}
```

### Fields

| Field | Description |
|-------|-------------|
| `version` | Schema version (currently `"1.0"`) |
| `description` | Optional description of the manifest's purpose |
| `mappings` | Array of type mappings |
| `namespaceDefaults` | Default effects for entire namespaces |

### Type Mapping Fields

| Field | Description |
|-------|-------------|
| `type` | Fully-qualified type name |
| `defaultEffects` | Effects for any method not explicitly listed |
| `methods` | Method-specific effects (`"*"` for wildcard) |
| `getters` | Property getter effects |
| `setters` | Property setter effects |
| `constructors` | Constructor effects (keyed by parameter signature) |

## Resolution Order

When resolving effects for a method call, the resolver checks these sources in order:

1. **Built-in catalog** - Hardcoded BCL method effects
2. **Specific method in type mapping** - Exact method name match
3. **Wildcard `"*"` in type mapping** - Fallback for any method
4. **`defaultEffects` on type** - Type-level default
5. **`namespaceDefaults`** - Namespace pattern match
6. **Unknown** - Method effects are unknown

## Manifest Search Locations

Manifests are loaded from multiple locations (later sources override earlier):

1. **Built-in** (lowest priority) - Embedded BCL manifests
2. **User-level** - `~/.calor/manifests/*.calor-effects.json`
3. **Solution-level** - `{solution}/.calor-effects/*.calor-effects.json`
4. **Project-local** (highest priority) - `.calor-effects.json` in project root

## Example Manifest

```json
{
  "version": "1.0",
  "description": "Effects for MyApp custom types",
  "mappings": [
    {
      "type": "MyApp.Services.UserRepository",
      "defaultEffects": ["db:rw"],
      "methods": {
        "GetById": ["db:r"],
        "GetAll": ["db:r"],
        "Save": ["db:w"],
        "Delete": ["db:w"]
      }
    },
    {
      "type": "MyApp.Services.EmailService",
      "methods": {
        "SendAsync": ["net:w"],
        "ValidateAddress": []
      }
    },
    {
      "type": "MyApp.Utils.Calculator",
      "defaultEffects": [],
      "methods": {
        "*": []
      }
    }
  ],
  "namespaceDefaults": {
    "MyApp.Data": ["db:rw"],
    "MyApp.Http": ["net:rw"]
  }
}
```

## CLI Commands

### Resolve Effects

Check what effects are resolved for a method:

```bash
calor effects resolve Console.WriteLine
calor effects resolve System.IO.File.ReadAllText
calor effects resolve MyApp.Service.DoWork --project ./src
```

### Validate Manifests

Validate all manifests in the search path:

```bash
calor effects validate
calor effects validate --project ./src --solution ./
```

### List Types

List all types with effect declarations:

```bash
calor effects list
calor effects list --type Console
calor effects list --json
```

## Strict Effects Mode

By default, unknown external calls produce warnings. Use `--strict-effects` to promote these to errors:

```bash
calor --input app.calr --strict-effects
```

This is useful for ensuring all external calls are properly documented in manifests.

## Creating Custom Manifests

1. Create a `.calor-effects.json` file in your project root
2. Add type mappings for your custom types or third-party libraries
3. Use `calor effects validate` to check for errors
4. Use `calor effects resolve` to test resolution

### Tips

- Use `defaultEffects: []` for types that are mostly pure
- Use `"*": []` wildcard for types where all methods are pure
- Use namespace defaults for libraries with consistent effect patterns
- Keep manifests focused (one per domain/library)
