# OPAL — Optimized Programming for Agent Logic

A programming language designed specifically for AI coding agents,
compiling to .NET via C# emission.

## The Problem: Why C# Fails AI Agents

Traditional languages like C# are poorly suited for AI coding agents:

- **Context-sensitive parsing**: C# requires full semantic analysis to understand code structure
- **Implicit side effects**: No way to know if `DoSomething()` writes to disk without reading implementation
- **Contracts are comments**: Preconditions/postconditions aren't machine-readable
- **No unique identifiers**: Can't precisely reference "line 42" across refactors
- **Ambiguous scoping**: Brace-matching requires understanding context
- **Token inefficiency**: Boilerplate consumes context window

**Example problem** (C#):
```csharp
// Agent sees this - what does it do? Side effects? Throws? Modifies state?
public int Process(string input) {
    var result = _service.Transform(input);  // Hidden network call? File I/O?
    return result.Value;                      // Nullable? Exception?
}
```

## The Solution: OPAL's Agent-First Design

| C# Problem | OPAL Solution | Agent Benefit |
|------------|---------------|---------------|
| Context-sensitive parsing | XML-like matched tags `§F[]...§/F[]` | Parse without semantic analysis |
| Implicit side effects | Explicit effects `§E[cw,fr]` | Know I/O without reading code |
| Comments as contracts | First-class `§Q`/`§S` | Generate tests from specs |
| No unique IDs | Every construct has ID `§F[f001:Main]` | Precise change tracking |
| Ambiguous scoping | Explicit close tags with ID matching | Unambiguous structure |
| Token inefficiency | Compact syntax, semantic density | More logic per token |

## Side-by-Side Comparison: HelloWorld

**OPAL** (`samples/HelloWorld/hello.opal`):
```
§M[m001:Hello]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A "Hello from OPAL!"
  §/C
§/F[f001]
§/M[m001]
```

**Equivalent C#** (requires boilerplate + implicit knowledge):
```csharp
namespace Hello
{
    public static class HelloModule
    {
        public static void Main()
        {
            Console.WriteLine("Hello from OPAL!");
        }
    }
}
```

**What the agent knows from OPAL without analysis**:
- Module ID: `m001`, Name: `Hello`
- Function ID: `f001`, Name: `Main`, Visibility: `public`
- Return type: `void`
- Side effects: `cw` (console write) — **no other I/O**
- Exact call target: `Console.WriteLine`

**What C# hides**:
- No indication this is the only side effect
- No ID for tracking across changes
- Requires understanding C# semantics to parse

## Side-by-Side Comparison: Contracts

**OPAL with contracts** (machine-readable):
```
§F[f002:Square:pub]
  §I[i32:x]
  §O[i32]
  §Q §OP[kind=gte] §REF[name=x] 0
  §S §OP[kind=gte] §REF[name=result] 0
  §R §OP[kind=mul] §REF[name=x] §REF[name=x]
§/F[f002]
```

**C# equivalent** (contracts hidden in runtime code):
```csharp
public static int Square(int x)
{
    if (!(x >= 0))
        throw new ArgumentException("Precondition failed");

    var result = x * x;

    if (!(result >= 0))
        throw new InvalidOperationException("Postcondition failed");

    return result;
}
```

**Agent advantage with OPAL**:
- `§Q` explicitly marks precondition: `x >= 0`
- `§S` explicitly marks postcondition: `result >= 0`
- Agent can extract contracts without parsing exception logic
- Can generate test cases: `x = -1` (should fail), `x = 0, 5, 100` (should pass)

## Side-by-Side Comparison: Control Flow

**OPAL FizzBuzz** (`samples/FizzBuzz/fizzbuzz.opal`):
```
§L[for1:i:1:100:1]
  §IF[if1] §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] 15 0
    §C[Console.WriteLine] §A "FizzBuzz" §/C
  §ELSEIF §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] 3 0
    §C[Console.WriteLine] §A "Fizz" §/C
  §ELSEIF §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] 5 0
    §C[Console.WriteLine] §A "Buzz" §/C
  §ELSE
    §C[Console.WriteLine] §A §REF[name=i] §/C
  §/I[if1]
§/L[for1]
```

**C# equivalent**:
```csharp
for (var i = 1; i <= 100; i++)
{
    if (i % 15 == 0) Console.WriteLine("FizzBuzz");
    else if (i % 3 == 0) Console.WriteLine("Fizz");
    else if (i % 5 == 0) Console.WriteLine("Buzz");
    else Console.WriteLine(i);
}
```

**Agent advantage with OPAL**:
- Loop bounds explicit: `§L[for1:i:1:100:1]` → var=i, from=1, to=100, step=1
- Agent knows iteration count (100) without symbolic execution
- Nested IDs (`for1`, `if1`) enable precise references
- Operators are symbolic (`§OP[kind=MOD]`), not textual

## Key Features Summary

| Feature | Syntax | Why Agents Need This |
|---------|--------|---------------------|
| **Unique IDs** | `§F[f001:Main]` | Track code across refactors |
| **Effects Declaration** | `§E[cw,fr,net]` | Know side effects without analysis |
| **Preconditions** | `§Q expression` | Extract testable constraints |
| **Postconditions** | `§S expression` | Verify implementation correctness |
| **Explicit Loops** | `§L[id:var:from:to:step]` | Calculate complexity, iterations |
| **Option Types** | `§SOME`, `§NONE` | Enforce null safety |
| **Result Types** | `§OK`, `§ERR` | Enforce error handling |
| **Matched Tags** | `§F[]...§/F[]` | Unambiguous scope boundaries |

## Quick Start

```bash
# Clone and build
git clone https://github.com/juanmicrosoft/opal.git
cd opal && dotnet build

# Compile OPAL to C#
dotnet run --project src/Opal.Compiler -- \
  --input samples/HelloWorld/hello.opal \
  --output samples/HelloWorld/hello.g.cs

# Run the program
dotnet run --project samples/HelloWorld
```

## Syntax Quick Reference

| Element | Syntax | Example |
|---------|--------|---------|
| Module | `§M[id:name]` | `§M[m001:MyModule]` |
| Function | `§F[id:name:visibility]` | `§F[f001:Main:pub]` |
| Input param | `§I[type:name]` | `§I[i32:count]` |
| Output type | `§O[type]` | `§O[void]` |
| Effects | `§E[codes]` | `§E[cw,fr]` |
| Call | `§C[target]` | `§C[Console.WriteLine]` |
| Argument | `§A value` | `§A "hello"` |
| Loop | `§L[id:var:from:to:step]` | `§L[l1:i:1:100:1]` |
| Requires | `§Q expr` | `§Q §OP[kind=gte] §REF[name=x] 0` |
| Ensures | `§S expr` | `§S §OP[kind=gte] §REF[name=result] 0` |
| Close tags | `§/X[id]` | `§/F[f001]` |

**Effect codes**: `cw` (console write), `cr` (console read), `fw` (file write), `fr` (file read), `net` (network), `db` (database)

## Status

- [x] Core compiler (lexer, parser, code gen)
- [x] Control flow (for, if, while)
- [x] Type system (Option, Result)
- [x] Contracts (requires, ensures)
- [x] MSBuild SDK integration
- [ ] Direct IL emission
