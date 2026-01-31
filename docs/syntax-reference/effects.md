---
layout: default
title: Effects
parent: Syntax Reference
nav_order: 6
---

# Effects

Effects declare the side effects a function may have. This is unique to OPAL - traditional languages leave side effects implicit.

---

## Why Declare Effects?

In traditional code, you must read the entire implementation to know if a function:
- Writes to console
- Reads files
- Makes network calls
- Modifies a database

OPAL requires explicit declaration:

```
§F{f001:SaveUser:pub}
  §I{User:user}
  §O{bool}
  §E{db,net}              // Declares: database and network operations
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
| `fw` | File write | Write to filesystem | `File.WriteAllText()` |
| `fr` | File read | Read from filesystem | `File.ReadAllText()` |
| `net` | Network | HTTP, sockets, etc. | `HttpClient.GetAsync()` |
| `db` | Database | Database operations | SQL queries, ORM calls |

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
  §E{fr,fw}               // File read and write
  // ...
§/F{f001}
```

### Network Call

```
§F{f001:FetchData:pub}
  §I{str:url}
  §O{str!str}
  §E{net}                 // Network operations
  // ...
§/F{f001}
```

### Database with Logging

```
§F{f001:CreateUser:pub}
  §I{User:user}
  §O{i32}
  §E{db,cw}               // Database and console (for logging)
  // ...
§/F{f001}
```

### Multiple Effects

```
§F{f001:ProcessOrder:pub}
  §I{Order:order}
  §O{bool}
  §E{db,net,fw,cw}        // Database, network, file write, console write
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
  §E{fr}                  // Only file read
  // ...
§/F{f001}

// Read-write file operation
§F{f002:UpdateConfig:pub}
  §I{str:path}
  §I{Config:config}
  §O{void}
  §E{fr,fw}               // File read and write
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
- Functions with `fr/fw`: Mock filesystem
- Functions with `net`: Mock HTTP
- Functions with `db`: Mock database

### 4. Composition Analysis

```
// If f1 calls f2, f1's effects must include f2's effects
§F{f001:ProcessAndSave:pub}
  §E{db,cw}               // Must include f002's effects
  §C{f002:Process} ... §/C
§/F{f001}

§F{f002:Process:pri}
  §E{cw}                  // Has console write effect
  // ...
§/F{f002}
```

---

## Effect Checking

The compiler warns if:

1. **Missing effect**: Function has side effect but doesn't declare it
2. **Unused effect**: Function declares effect but doesn't use it
3. **Propagation**: Caller doesn't include callee's effects

---

## Next

- [Syntax Reference](/opal/syntax-reference/) - Back to overview
- [Benchmarking](/opal/benchmarking/) - See how effects help comprehension
