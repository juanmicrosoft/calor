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

### Moving Functions Between Modules
When moving a function to another module:
1. PRESERVE the function's unique ID - this is critical
2. Update any callers to use qualified name if needed
3. Contracts (§Q, §S) should move with the function
4. Effects (§E) should move with the function
5. The ID enables stable references even after the move

### Cross-Module Calls
After moving, callers may need to reference the new module:
```
§C{ModuleName.FunctionName}
§A arg
§/C
```

### Type Mappings
| C# | Calor |
|----|-------|
| `int` | `i32` |
| `string` | `str` |
| `void` | `void` |
