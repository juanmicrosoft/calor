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

### Contracts
**Preconditions (§Q)**: What must be true BEFORE the function runs
```
§Q (>= x 0)                  // x must be non-negative
§Q (< index length)          // index must be less than length
§Q (!= divisor 0)            // divisor must not be zero
```

**Postconditions (§S)**: What is guaranteed AFTER the function runs
```
§S (>= result 0)             // result is non-negative
§S (== result (* x x))       // result equals x squared
§S (<= result max)           // result is bounded
```

Use `result` to refer to the return value in postconditions.

### Effects Declaration (§E)
Declares what side effects a function may have:
- `cw` = console write (Console.WriteLine, etc.)
- `cr` = console read
- `fs` = file system access
- `net` = network access
- `db` = database access

Example:
```
§E{cw}          // Has console write effect
§E{cw,fs}       // Has console write AND file system effects
```

### Expression Syntax
- Arithmetic: `(+ a b)`, `(- a b)`, `(* a b)`, `(/ a b)`
- Comparison: `(== a b)`, `(!= a b)`, `(< a b)`, `(> a b)`, `(<= a b)`, `(>= a b)`
- Logical: `(and a b)`, `(or a b)`, `(not a)`
- Ternary: `(? condition then else)`

### Adding Contracts
When adding contracts:
1. If the function has an explicit ID, don't change it
2. Add §Q lines for preconditions (after §O, before §S)
3. Add §S lines for postconditions (after §Q, before §E or §R)
4. Add §E for effects (after contracts, before §R)
