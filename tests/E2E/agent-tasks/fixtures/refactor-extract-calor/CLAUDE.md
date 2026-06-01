## Calor Syntax Reference

This is a Calor project. Write code in `.calr` files.

### Block Structure (Indentation)

Calor blocks are delimited by **indentation** (like Python), with the
default of **2 spaces per nesting level**. There are **no `§/X` closing
tags** to add; a block ends at the next line that dedents back to (or
past) the parent column. Stable IDs are **optional** on structural
openers — provide one (`§F{f001:Name:pub}`) only if you need a handle
for external tooling. Otherwise `§F{Name:pub}` is fine and the parser
auto-assigns an ID.

### Function Syntax
```
§F{Name:pub}
  §I{type:name}      // Input parameter
  §O{type}           // Output/return type
  §Q (condition)     // Precondition (requires)
  §S (condition)     // Postcondition (ensures)
  §E{effects}        // Effects declaration
  §R expression      // Return
```

### Type Mappings
| C# | Calor |
|----|-------|
| `int` | `i32` |
| `long` | `i64` |
| `string` | `str` |
| `bool` | `bool` |
| `void` | `void` |

### Expression Syntax (Lisp-style, prefix notation)
- Arithmetic: `(+ a b)`, `(- a b)`, `(* a b)`, `(/ a b)`, `(% a b)`
- Comparison: `(== a b)`, `(!= a b)`, `(< a b)`, `(> a b)`, `(<= a b)`, `(>= a b)`
- Logical: `(and a b)`, `(or a b)`, `(not a)`
- Ternary/Conditional: `(? condition then-expr else-expr)`

### Contracts
- Precondition: `§Q (>= x 0)` - use `§Q` for requires
- Postcondition: `§S (>= result 0)` - use `§S` for ensures, `result` refers to return value

### Effects Declaration
- `cw` = console write
- `cr` = console read
- `fs` = file system access
- `net` = network access

### Method Calls
```
§C{MethodName}
§A argument
§/C
```

### Unique IDs
Stable IDs are **optional** on structural openers. When extracting new functions:
- Existing functions keep any explicit ID they already have
- New extracted functions may omit the ID (`§F{Name:pub}`) and the parser will auto-assign one
- Add `§F{f005:Name:pub}` only if you want an external-tooling handle
