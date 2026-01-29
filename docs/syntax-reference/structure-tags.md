---
layout: default
title: Structure Tags
parent: Syntax Reference
nav_order: 1
---

# Structure Tags

Structure tags define the organization of OPAL code: modules, functions, and their boundaries.

---

## Modules

Modules are like C# namespaces. They group related functions.

### Syntax

```
§M[id:name]
  // contents
§/M[id]
```

### Example

```
§M[m001:Calculator]
  // functions go here
§/M[m001]
```

### Rules

- `id` must be unique within the file
- `name` becomes the C# namespace
- Every `§M` must have a matching `§/M` with the same ID

---

## Functions

Functions are the primary code containers.

### Syntax

```
§F[id:name:visibility]
  §I[type:param]       // inputs (0 or more)
  §O[type]             // output (required)
  §E[effects]          // effects (optional)
  §Q condition         // preconditions (0 or more)
  §S condition         // postconditions (0 or more)
  // body
§/F[id]
```

### Visibility

| Value | Meaning | C# Equivalent |
|:------|:--------|:--------------|
| `pub` | Public | `public static` |
| `pri` | Private | `private static` |

### Examples

**Simple function:**
```
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §R (+ a b)
§/F[f001]
```

**Function with effects:**
```
§F[f001:PrintSum:pub]
  §I[i32:a]
  §I[i32:b]
  §O[void]
  §E[cw]
  §P (+ a b)
§/F[f001]
```

**Function with contracts:**
```
§F[f001:Divide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q (!= b 0)
  §R (/ a b)
§/F[f001]
```

---

## Input Parameters

Input parameters define function arguments.

### Syntax

```
§I[type:name]
```

### Examples

```
§I[i32:x]           // int x
§I[str:name]        // string name
§I[bool:flag]       // bool flag
§I[?i32:maybeVal]   // int? maybeVal (nullable)
```

### Multiple Parameters

```
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §I[i32:c]
  §O[i32]
  §R (+ (+ a b) c)
§/F[f001]
```

---

## Output Type

Every function must declare its output type.

### Syntax

```
§O[type]
```

### Examples

```
§O[void]     // returns nothing
§O[i32]      // returns int
§O[str]      // returns string
§O[?i32]     // returns nullable int
§O[i32!str]  // returns Result<int, string>
```

---

## Closing Tags

Every structural element must be closed with a matching tag.

### Rules

1. Opening `§X[id:...]` must have closing `§/X[id]`
2. IDs must match exactly
3. Nesting must be proper (no overlapping scopes)

### Correct Nesting

```
§M[m001:Example]
  §F[f001:Main:pub]
    §L[for1:i:1:10:1]
      §IF[if1] (> i 5)
        §P i
      §/I[if1]
    §/L[for1]
  §/F[f001]
§/M[m001]
```

### Incorrect (Overlapping)

```
// WRONG: if1 closed after for1
§L[for1:i:1:10:1]
  §IF[if1] (> i 5)
§/L[for1]
  §/I[if1]     // Error: if1 overlaps for1
```

---

## Tag Reference

| Opening | Closing | Purpose |
|:--------|:--------|:--------|
| `§M[id:name]` | `§/M[id]` | Module |
| `§F[id:name:vis]` | `§/F[id]` | Function |
| `§L[id:var:from:to:step]` | `§/L[id]` | Loop |
| `§IF[id] cond` | `§/I[id]` | Conditional |
| `§C[target]` | `§/C` | Call (no ID needed) |

---

## C# Attributes

C# attributes are preserved during conversion using inline bracket syntax `[@Attribute]`.

### Syntax

```
§CLASS[id:name][@AttributeName]
§CLASS[id:name][@AttributeName(args)]
§METHOD[id:name:vis][@Attr1][@Attr2]
```

### Examples

**Class with routing attributes (ASP.NET Core):**
```
§CLASS[c001:JoinController:ControllerBase][@Route("api/[controller]")][@ApiController]
  §METHOD[m001:Post:pub][@HttpPost]
  §/METHOD[m001]
§/CLASS[c001]
```

**Property with validation:**
```
§PROP[p001:Email:str:pub][@Required][@EmailAddress]
  §GET
  §SET
§/PROP[p001]
```

### Attribute Arguments

| Style | Syntax | Example |
|:------|:-------|:--------|
| No args | `[@Name]` | `[@ApiController]` |
| Positional | `[@Name(value)]` | `[@Route("api/test")]` |
| Named | `[@Name(Key="value")]` | `[@JsonProperty(PropertyName="id")]` |
| Mixed | `[@Name(pos, Key=val)]` | `[@Range(1, 100, ErrorMessage="Invalid")]` |

### Supported Elements

Attributes can be attached to:
- Classes: `§CLASS[...][@attr]`
- Interfaces: `§IFACE[...][@attr]`
- Methods: `§METHOD[...][@attr]`
- Properties: `§PROP[...][@attr]`
- Fields: `§FLD[...][@attr]`
- Parameters: `§I[type:name][@attr]`

---

## Why Explicit Closing Tags?

1. **Unambiguous parsing** - No brace-counting needed
2. **ID verification** - Compiler catches mismatches
3. **Agent-friendly** - Easy to identify scope boundaries
4. **Refactoring safe** - IDs survive code movement

---

## Next

- [Types](/opal/syntax-reference/types/) - Type system reference
