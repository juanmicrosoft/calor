---
layout: default
title: Design Principles
parent: Philosophy
nav_order: 1
---

# Design Principles

Calor is built on five core principles that guide every language design decision.

---

## The Five Principles

| Principle | Implementation | Agent Benefit |
|:----------|:---------------|:--------------|
| **Explicit over implicit** | Effects declared with `§E{cw, fs:r, net:rw}` | Know side effects without reading implementation |
| **Contracts are code** | First-class `§Q` (requires) and `§S` (ensures) | Generate tests from specs, verify correctness |
| **Everything has an ID** | `§F{f001:Main}`, `§L{l001:i:1:100:1}` | Precise references that survive refactoring |
| **Unambiguous structure** | Matched tags `§F{}...§/F{}` | Parse without semantic analysis |
| **Machine-readable semantics** | Lisp-style operators `(+ a b)` | Symbolic manipulation without text parsing |

---

## 1. Explicit Over Implicit

In traditional languages, side effects are implicit. You have to read the entire function body to know if it:
- Writes to console
- Reads from files
- Makes network calls
- Accesses a database

Calor requires explicit effect declarations:

```
§F{f001:SaveUser:pub}
  §I{User:user}
  §O{bool}
  §E{db:rw, net:rw}        // Explicit: database and network effects
  // ... implementation
§/F{f001}
```

**Agent benefit:** An agent can immediately filter functions by their effects without analyzing implementation details.

### Effect Codes

| Code | Effect |
|:-----|:-------|
| `cw` | Console write |
| `cr` | Console read |
| `fs:r` | File read/exists |
| `fs:w` | File write/delete |
| `fs:rw` | File read + write |
| `net:r` | Network read (GET) |
| `net:w` | Network write (POST/PUT) |
| `net:rw` | Network read + write |
| `http` | HTTP requests |
| `db:r` | Database read (SELECT) |
| `db:w` | Database write (INSERT/UPDATE/DELETE) |
| `db:rw` | Database read + write |
| `time` | System time access |
| `rand` | Random number generation |
| `mut` | Observable heap writes |
| `throw` | Intentional throw statements |

**Subtyping:** `fs:rw` encompasses both `fs:r` and `fs:w`. A function declared `§E{fs:rw}` can call functions with `§E{fs:r}`, but not vice versa. The same applies to `net:*` and `db:*` families.

**Effect manifests:** For .NET interop, the compiler resolves effects through `.calor-effects.json` manifest files. Built-in manifests cover the BCL; project-local manifests handle NuGet packages. See [Effect Manifests](/calor/guides/effect-manifests/) for details.

---

## 2. Contracts Are Code

Preconditions and postconditions aren't comments or assertions buried in code - they're first-class syntax elements:

```
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)              // Requires: b is not zero
  §Q (>= a 0)              // Requires: a is non-negative
  §S (>= result 0)         // Ensures: result is non-negative
  §R (/ a b)
§/F{f001}
```

**Agent benefit:**
- Automatic test generation from contracts
- Static verification of caller sites
- Clear documentation of function behavior

With [`--verify`](/calor/philosophy/static-verification/), the compiler uses Z3 to prove contracts hold at compile time. Proven contracts have their runtime checks elided.

### Contract Syntax

| Tag | Purpose | Example |
|:----|:--------|:--------|
| `§Q` | Precondition (requires) | `§Q (> x 0)` |
| `§S` | Postcondition (ensures) | `§S (!= result null)` |
| `§Q{message="..."}` | With custom error | `§Q{message="x must be positive"} (> x 0)` |

---

## 3. Everything Has an ID

Every structural element has a unique identifier that persists across refactoring:

```
§M{m001:Calculator}           // Module ID: m001
§F{f001:Add:pub}              // Function ID: f001
  §L{for1:i:1:100:1}          // Loop ID: for1
    §IF{if1} (> i 50)         // Conditional ID: if1
    // ...
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

**Agent benefit:**
- "Edit function f_01J5X7..." is unambiguous
- IDs survive code movement and renaming
- No reliance on line numbers that change

> Documentation uses short IDs (`f001`) for readability. Production code uses full ULIDs (`f_01J5X7K9M2NPQRSTABWXYZ12`).

### ID Conventions

| Element | Convention | Example |
|:--------|:-----------|:--------|
| Modules | `m_` + ULID | `§M{m_01J5X7...:Calculator}` |
| Functions | `f_` + ULID | `§F{f_01J5X7...:Add:pub}` |
| Classes | `c_` + ULID | `§CL{c_01J5X7...:MyClass}` |
| Test files | Sequential | `§F{f001:Add:pub}` |

[Learn more: Stable Identifiers](/calor/philosophy/stable-identifiers/) - Why IDs matter and how challenges are overcome

---

## 4. Unambiguous Structure

Every opening tag has a matching closing tag with the same ID:

```
§M{m001:Example}
  §F{f001:Main:pub}
    §L{for1:i:1:10:1}
      §IF{if1} (> i 5)
        // ...
      §/I{if1}
    §/L{for1}
  §/F{f001}
§/M{m001}
```

**Agent benefit:**
- No brace-counting ambiguity
- Parse structure without understanding semantics
- Easy to verify structural correctness

### Closing Tag Rules

| Opening | Closing |
|:--------|:--------|
| `§M{id:name}` | `§/M{id}` |
| `§F{id:name:vis}` | `§/F{id}` |
| `§L{id:var:from:to:step}` | `§/L{id}` |
| `§IF{id} condition` | `§/I{id}` |

---

## 5. Machine-Readable Semantics

Expressions use Lisp-style prefix notation that's directly manipulable:

```
// Calor: Clear AST structure
(+ (* a b) (- c d))

// Equivalent infix: Requires precedence parsing
a * b + c - d     // Wait, is this (a*b)+(c-d) or a*(b+c)-d?
```

**Agent benefit:**
- No operator precedence ambiguity
- Direct AST manipulation
- Symbolic computation without parsing

### Operators

| Category | Operators |
|:---------|:----------|
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Comparison | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Logical | `&&`, `\|\|`, `!` |

---

## Principle Interactions

These principles reinforce each other:

1. **IDs + Closing tags** = Unambiguous scope references
2. **Contracts + Explicit effects** = Complete behavioral specification
3. **Lisp syntax + Contracts** = Symbolic verification possible
4. **IDs + Contracts** = Traceable invariants across refactoring
5. **Z3 + Contracts** = Compile-time proofs of correctness
6. **Effects + Manifests** = Verification across .NET interop boundaries

---

## Why These Principles Are Now Practical

These principles aren't new. Effect systems date to 1986. Design-by-contract to 1986. Unique identifiers have always been possible.

**What's new is who writes the code.**

When humans write code, these principles impose annotation burden that developers resist. When agents write code, annotation cost is zero.

Calor's principles represent 40 years of programming language research that only becomes practical when agents are the primary code authors.

[Learn more: Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/)

---

## Next

- [Effects & Contracts Enforcement](/calor/philosophy/effects-contracts-enforcement/) - Why agent languages unlock practical verification
- [Tradeoffs](/calor/philosophy/tradeoffs/) - What Calor gives up for these principles
