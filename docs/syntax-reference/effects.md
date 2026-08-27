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
```

**The line matters.** A `§E{...}` on its **own line**, as above, is the function's own
effect declaration. A `§E{...}` written on the **same line** as a type belongs to that
type instead — see *Effect rows* below.

---

## Effect Rows

*New in v0.15.*

If a function takes another function as a parameter — a callback, a handler, a
comparison — you can now say what that inner function is allowed to do. Write the same
`§E{...}` tag on the **same line** as the type it describes:

```
§F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
```

That says: *`transform` may write to the console.* Before this, there was no way to say
it, so calling `transform` at all was an error.

The rule is just the line. A `§E{...}` on the same line as a type is that type's row; a
`§E{...}` on its own line is the function's effect declaration, exactly as it always was.
Almost everyone already writes the second form, so almost nothing changes.

### The eight places you can write one

```
§F{f001:F:pub} () -> void
  §E{cw}                                    // 1. the function's own declaration

§B{f} §LAM{lam1:x:i32} §E{cw} ... §/LAM{lam1} // 2. a lambda

§DEL{d001:Handler}
  §I{i32:x}
  §O{void}
  §E{cw}                                    // 3. a delegate

§I{Func<i32,i32>:f} §E{cw}                  // 4. a parameter, tag form
(Func<i32,i32>:f §E{cw}, i32:v)             // 5. a parameter, inline form
§O{Func<i32>} §E{cw}                        // 6. a return type
() -> Func<i32> §E{cw}                      //    …or the arrow spelling
§B{f:Func<i32,i32>} §E{cw} <initializer>    // 7. a binding
§FLD{Action<i32>:onChange:pri} §E{cw}       // 8. a field
```

A type carries **one** row. To allow several effects, put them in the one row —
`§E{cw, net}` — not in two tags side by side.

### Placeholder effects (`eff`)

Sometimes a function should be allowed to do *whatever the caller's function does*, no
more and no less. Write a placeholder in the type-parameter list with `eff`, then use its
name in the rows:

```
§F{f001:Twice:pub}<eff e> (Func<i32,i32>:f §E{e}, i32:v) -> i32
  §E{e}
  §R INT:0
```

`Twice` is pure when `f` is pure, and writes to the console when `f` does. One definition
serves both callers.

A placeholder can only be declared on a function or a method — including a method inside
an interface — and can only be used in that declaration's own row and in its parameters'
rows. It may not be named after a real effect code such as `cw`, because then that code
could not be written inside the declaration.

### When it goes wrong

**Calor0405** — the row is somewhere a row cannot go. Most often the row is on the wrong
line:

```
§FLD{i32:x:pri}
§E{cw}            // Calor0405: move it onto the end of the 'x' field line
```

It also fires if you write two rows on one type. The message always names both repairs.

**Calor0404** — a placeholder is declared where it cannot be (on a class or an interface
rather than on a member), used somewhere it cannot be (a return type, a binding, a
lambda), or named after a real effect code.

### What is checked today

**A row is now checked where a function value moves.** When a function value is bound,
re-assigned, passed as an argument, returned, or used to override/implement a member, the
compiler compares the value's row with the row of the place it is going: a row that is too
wide is **Calor0424** (always an error), a row the compiler cannot determine is **Calor0425**
(a warning; `--permissive-effects` waives only this one). A row on something that is provably
not a function (`i32`, `str`, an array) is **Calor0405**; a row on a type the compiler does not
know is accepted, not refused.

**Still to come:** calling a callback through its row. Invoking a function value is refused
today whatever row it carries, and a lambda's body is not yet compared with its declared row:

```
§F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
  §E{cw}
  §R §C{transform} §A value §/C     // Calor0418 until the next slice replaces it
```

The row is what will *make* that call legal — the compiler has to compare it against the
function you actually pass, and that comparison is the next release. Until then a call
through a function-typed value still needs `§CSHARP` interop or
`--permissive-effects`, exactly as before.

---

## Effect Codes

| Code | Effect | Description | C# Examples |
|:-----|:-------|:------------|:------------|
| `cw` | Console write | Output to console | `Console.WriteLine()` |
| `cr` | Console read | Input from console | `Console.ReadLine()` |
| `fs:r` | Filesystem read | Read from filesystem | `File.ReadAllText()` |
| `fs:w` | Filesystem write | Write to filesystem | `File.WriteAllText()` |
| `fs:rw` | Filesystem read/write | Read and write filesystem | `File.Copy()` |
| `net` | Network | Unspecified network access | `Socket` operations |
| `net:r` | Network read | HTTP GET, etc. | `HttpClient.GetStringAsync()` |
| `net:w` | Network write | HTTP POST, etc. | `HttpClient.PostAsync()` |
| `net:rw` | Network read/write | HTTP operations | `HttpClient.SendAsync()` |
| `http` | HTTP | HTTP client operations | `HttpClient` usage |
| `db` | Database | Unspecified database access | Connection setup |
| `db:r` | Database read | Database queries | `SELECT` queries |
| `db:w` | Database write | Database mutations | `INSERT/UPDATE/DELETE` |
| `db:rw` | Database read/write | Database operations | ORM calls |
| `env` | Environment | Unspecified environment access | `Environment` usage |
| `env:r` | Environment read | Read environment variables | `Environment.GetEnvironmentVariable()` |
| `env:w` | Environment write | Write environment variables | `Environment.SetEnvironmentVariable()` |
| `env:rw` | Environment read/write | Environment operations | Read-modify-write of env vars |
| `proc` | Process | Spawn or control processes | `Process.Start()` |
| `mut` | Mutation | Writes to heap state (fields, collections) | `list.Add()`, field assignment |
| `mut:col` | Collection mutation | Mutates a collection | `dict[key] = value` |
| `alloc` | Allocation | Allocates an object or buffer | `new`, `stackalloc`, unsafe buffers |
| `unsafe` | Unsafe memory | Uses unsafe memory operations | pointer/fixed operations |
| `time` | Time | Reads the clock (nondeterministic) | `DateTime.Now` |
| `rand` | Randomness | Random number generation (nondeterministic) | `Random.Next()` |
| `throw` | Exception | Intentionally throws exceptions | `throw` statements |

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
```

Or explicitly:

```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §E{}                    // Explicitly no effects
  §R (+ a b)
```

### Console Output

```
§F{f001:Greet:pub}
  §I{str:name}
  §O{void}
  §E{cw}                  // Console write
  §P name
```

### File Operations

```
§F{f001:CopyFile:pub}
  §I{str:source}
  §I{str:dest}
  §O{bool}
  §E{fs:rw}               // Filesystem read and write
  // ...
```

### Network Call

```
§F{f001:FetchData:pub}
  §I{str:url}
  §O{str!str}
  §E{net:rw}              // Network operations
  // ...
```

### Database with Logging

```
§F{f001:CreateUser:pub}
  §I{User:user}
  §O{i32}
  §E{db:rw,cw}            // Database and console (for logging)
  // ...
```

### Multiple Effects

```
§F{f001:ProcessOrder:pub}
  §I{Order:order}
  §O{bool}
  §E{db:rw,net:rw,fs:w,cw} // Database, network, filesystem write, console write
  // ...
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

// Read-write file operation
§F{f002:UpdateConfig:pub}
  §I{str:path}
  §I{Config:config}
  §O{void}
  §E{fs:rw}               // Filesystem read and write
  // ...
```

### Interactive Console

```
§F{f001:Prompt:pub}
  §I{str:question}
  §O{str}
  §E{cw,cr}               // Console write and read
  §P question
  §R §C{Console.ReadLine} §/C
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

§F{f002:Process:pri}
  §E{cw}                  // Has console write effect
  // ...
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

§F{f002:Main:pub}
  §O{void}
  // No §E declaration
  §C{f001:Helper}                  // ERROR: cw effect leaks through
```

### Soundness Diagnostics (v0.11 strictness batch)

Effect enforcement is fail-closed. The diagnostics on the enforcement surface:

- **Calor0410** — a function uses an effect it does not declare (with call chain).
- **Calor0411** — an unknown external call; add the callee to a `.calor-effects.json` manifest. Unknown calls contribute worst-case effects, so under the default policy they fail loud rather than being assumed pure.
- **Calor0403** — an unknown or misspelled source effect code. Source declarations and manifests use the same authoritative taxonomy; unknown codes are rejected.
- **Calor0418** — invocation of a delegate/function-typed value (a parameter, binding, or field being called). Function-typed values carry no effect contract, so the call is an **error** under enforcement. There is no annotation escape hatch; wrap the call in `§CSHARP` interop (surfaced as an assumption via Calor0419) or compile with `--permissive-effects` (an explicit waiver that demotes the error to a warning).
- **Calor0419** — the effects of a function are **assumed**, not verified: it contains raw C# interop (`§CSHARP`/`§CS`), an unrecognized construct, or calls a function whose effects are assumed. The assumption propagates to callers through the interprocedural pass. Warning by default; error under `--strict-effects`.
- **Calor0420** — an override declares effects not covered by its base method's declared `§E` (override `§E` must be a subset of base `§E`). Broader override effects would launder through dynamic dispatch.
- **Calor0421** — an interface implementation declares effects not covered by the interface method's declared `§E`.
- **Calor0422** — a constructor body performs effects beyond intrinsic initialization mutation/allocation (`mut`, `alloc`). Constructor syntax currently has no `§E` surface, so other effects fail closed; move effectful work to a declared method.
- **Calor0423** — a custom property or event accessor body performs effects beyond intrinsic accessor mutation. Accessors currently have no `§E` surface, so such bodies fail closed.
- **Calor0424** — an effect **row** does not fit where the value is going: a function value with a wider row is assigned to a binding, passed as an argument, or returned into a position whose row is narrower. **Always an error, and no flag waives it** — not even `--permissive-effects`. The message names both rows and the extra effect(s), so the fix is either to widen the destination's `§E{…}` or to pass a function whose row fits.
- **Calor0425** — an effect **row** cannot be decided at one of those same places, because one side (or both) carries no row and so is Unknown, or because it fits only under an assumption. Warning by default; error under `--strict-effects`; **suppressed** by `--permissive-effects`. Add `§E{…}` on the same line as the type to say what may be passed.

The two are the same question asked twice: Calor0424 is *we know it does not fit*, Calor0425 is *we cannot tell*. `--permissive-effects` waives only the second — a waiver for "we do not know" is honest, a waiver for "we know it is wrong" is not. That is also why **`--permissive-effects` no longer softens Calor0420 or Calor0421**: an override or implementation that broadens its base's effect set is an error under every flag as of v0.15.

Calls through a receiver whose static type is an in-module interface or class charge the static type's *declared* `§E` — sound because Calor0420/Calor0421 pin every override and implementation to a subset of that declared set, **including implementations inherited from in-module base classes**. Overrides of **external C# base classes**, and interface implementations satisfied by members inherited from external bases, cannot be variance-checked and are surfaced through the Calor0419 assumption channel instead.

Calls, constructors, object-initializer setters, event accessors, and disposal are
resolved using receiver/constructed type plus inferred argument types. A manifest
signature may therefore distinguish overloads. No production purity decision is
made from a bare method name: unresolved receivers, extensions, constructors, or
`Dispose` calls remain unknown under the strict policy. Every `§NEW` also charges
`alloc`; object initializers and event subscriptions charge `mut` in addition to
resolved accessor effects.

### Disabling Enforcement (Not Recommended)

For migration scenarios, you can disable enforcement:

```bash
calor --input myprogram.calr --no-enforce-effects
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
