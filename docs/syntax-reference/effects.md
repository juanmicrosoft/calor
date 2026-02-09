---
layout: default
title: Effects
parent: Syntax Reference
nav_order: 6
---

# Effects

Effects declare the side effects a function may have. This is unique to Calor - traditional languages leave side effects implicit.

---

## Why Declare Effects?

In traditional code, you must read the entire implementation to know if a function:
- Writes to console
- Reads files
- Makes network calls
- Modifies a database

Calor requires explicit declaration:

```
§F{f001:SaveUser:pub}
  §I{User:user}
  §O{bool}
  §E{db:rw,net:rw}        // Declares: database and network operations
  // ...
§/F{f001}
```

Now an agent knows immediately what side effects to expect.

---

## Effect Syntax

```
§E{code1,code2,...}
§E{}                      // No effects (pure function)
```

Place the effect declaration after the output type:

```
§F{id:name:vis}
  §I{...}
  §O{...}
  §E{effects}             // Here
  §Q ...
  §S ...
  // body
§/F{id}
```

---

## Effect Codes

| Code | Effect | Description | C# Examples |
|:-----|:-------|:------------|:------------|
| `cw` | Console write | Output to console | `Console.WriteLine()` |
| `cr` | Console read | Input from console | `Console.ReadLine()` |
| `fs:r` | Filesystem read | Read from filesystem | `File.ReadAllText()` |
| `fs:w` | Filesystem write | Write to filesystem | `File.WriteAllText()` |
| `fs:rw` | Filesystem read/write | Read and write filesystem | `File.Copy()` |
| `net:r` | Network read | HTTP GET, etc. | `HttpClient.GetStringAsync()` |
| `net:w` | Network write | HTTP POST, etc. | `HttpClient.PostAsync()` |
| `net:rw` | Network read/write | HTTP operations | `HttpClient.SendAsync()` |
| `db:r` | Database read | Database queries | `SELECT` queries |
| `db:w` | Database write | Database mutations | `INSERT/UPDATE/DELETE` |
| `db:rw` | Database read/write | Database operations | ORM calls |

---

## Examples

### Pure Function (No Effects)

```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  // No §E means pure - no side effects
  §R (+ a b)
§/F{f001}
```

Or explicitly:

```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §E{}                    // Explicitly no effects
  §R (+ a b)
§/F{f001}
```

### Console Output

```
§F{f001:Greet:pub}
  §I{str:name}
  §O{void}
  §E{cw}                  // Console write
  §P name
§/F{f001}
```

### File Operations

```
§F{f001:CopyFile:pub}
  §I{str:source}
  §I{str:dest}
  §O{bool}
  §E{fs:rw}               // Filesystem read and write
  // ...
§/F{f001}
```

### Network Call

```
§F{f001:FetchData:pub}
  §I{str:url}
  §O{str!str}
  §E{net:rw}              // Network operations
  // ...
§/F{f001}
```

### Database with Logging

```
§F{f001:CreateUser:pub}
  §I{User:user}
  §O{i32}
  §E{db:rw,cw}            // Database and console (for logging)
  // ...
§/F{f001}
```

### Multiple Effects

```
§F{f001:ProcessOrder:pub}
  §I{Order:order}
  §O{bool}
  §E{db:rw,net:rw,fs:w,cw} // Database, network, filesystem write, console write
  // ...
§/F{f001}
```

---

## Effect Patterns

### Read-Only vs Read-Write

```
// Read-only file operation
§F{f001:LoadConfig:pub}
  §I{str:path}
  §O{Config}
  §E{fs:r}                // Only filesystem read
  // ...
§/F{f001}

// Read-write file operation
§F{f002:UpdateConfig:pub}
  §I{str:path}
  §I{Config:config}
  §O{void}
  §E{fs:rw}               // Filesystem read and write
  // ...
§/F{f002}
```

### Interactive Console

```
§F{f001:Prompt:pub}
  §I{str:question}
  §O{str}
  §E{cw,cr}               // Console write and read
  §P question
  §R §C{Console.ReadLine} §/C
§/F{f001}
```

---

## Benefits for Agents

### 1. Filtering by Effect

"Find all functions that access the database":
```
// Agent searches for §E{..db..}
```

### 2. Refactoring Safety

"This function should be pure, but it has effects":
```
§F{f001:Calculate:pub}
  §O{i32}
  §E{cw}                  // Wait, why is Calculate logging?
```

### 3. Testing Strategy

- Functions with no effects: Unit test directly
- Functions with `cw/cr`: Mock console
- Functions with `fs:r/fs:w/fs:rw`: Mock filesystem
- Functions with `net:r/net:w/net:rw`: Mock HTTP
- Functions with `db:r/db:w/db:rw`: Mock database

### 4. Composition Analysis

```
// If f1 calls f2, f1's effects must include f2's effects
§F{f001:ProcessAndSave:pub}
  §E{db:rw,cw}            // Must include f002's effects
  §C{f002:Process} ... §/C
§/F{f001}

§F{f002:Process:pri}
  §E{cw}                  // Has console write effect
  // ...
§/F{f002}
```

---

## Effect Enforcement

**Effect enforcement is enabled by default.** The compiler doesn't just warn - it **rejects** code with undeclared effects.

### Why Strict Enforcement?

In traditional languages, effect annotations are optional hints. Developers forget them, skip them under time pressure, or let them rot as code evolves.

Calor takes a different approach: **effects are enforced, not suggested.**

This is practical because Calor is designed for coding agents, not humans. Agents:
- Generate effect annotations for free (no annotation burden)
- Maintain perfect consistency (never forget to update)
- Don't cut corners under deadline pressure

[Learn more: Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/)

### Compile-Time Errors

```
error Calor0410: Function 'f001' uses effect 'console_write' but does not declare it
  Call chain: f001 → f002 → Console.WriteLine
```

The compiler provides:
1. **Exact violation** - Which effect is missing
2. **Call chain** - How the effect propagates through your code
3. **Function ID** - Precise reference for agents to fix

### Interprocedural Analysis

The compiler doesn't just check individual functions. It performs **interprocedural analysis** using Strongly Connected Components (SCC) to trace effects through any depth of calls.

You cannot hide an effect by burying it in helper functions.

```
§F{f001:Helper:pri}
  §C{Console.WriteLine} "hidden"   // Has cw effect
§/F{f001}

§F{f002:Main:pub}
  §O{void}
  // No §E declaration
  §C{f001:Helper}                  // ERROR: cw effect leaks through
§/F{f002}
```

### Disabling Enforcement (Not Recommended)

For migration scenarios, you can disable enforcement:

```bash
calor compile myprogram.calr --enforce-effects=false
```

Or in MSBuild:

```xml
<PropertyGroup>
  <CalorEnforceEffects>false</CalorEnforceEffects>
</PropertyGroup>
```

---

## Next

- [Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/) - Why this matters
- [Contracts](/calor/syntax-reference/contracts/) - Preconditions and postconditions
- [Benchmarking](/calor/benchmarking/) - See how effects help comprehension
