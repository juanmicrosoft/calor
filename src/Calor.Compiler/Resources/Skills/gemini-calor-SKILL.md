---
name: calor
description: Write Calor code with Lisp-style expressions, contracts, and effects. Use for all new code in Calor-first projects.
---

# @calor - Write Calor Code

Calor (Optimized Programming for Agents Language) compiles to C# via .NET.

**Calor syntax:**
- Use Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Use arrow syntax for conditionals: `§IF{id} condition → action`
- Use `§P` for print, `§B` for bindings, `§R` for return

## Structure Tags

```
§M{id:Name}           Module (namespace)
§F{id:Name:vis}       Function (pub|pri)
§I{type:name}         Input parameter
§O{type}              Output/return type
§E{effects}           Side effects: cw,cr,fw,fr,net,db
§/M{id} §/F{id}       Close tags (ID must match)
```

## Types

```
i32, i64, f32, f64    Numbers
u8, u16, u32, u64     Unsigned integers
str, bool, void       String, boolean, unit
?T                    Option<T> (nullable)
T!E                   Result<T,E> (fallible)
[T]                   Array of T (e.g., [u8], [i32], [str])
[[T]]                 Nested array (e.g., [[i32]] for int[][])

datetime              DateTime
datetimeoffset        DateTimeOffset
timespan              TimeSpan
date                  DateOnly
time                  TimeOnly
guid                  Guid

List<T>               List<T>
Dict<K,V>             Dictionary<K,V>
ReadList<T>           IReadOnlyList<T>
ReadDict<K,V>         IReadOnlyDictionary<K,V>
```

### Array Type Examples

```
[u8]                  byte[]
[i32]                 int[]
[str]                 string[]
[[i32]]               int[][] (jagged array)
```

## Lisp-Style Expressions

```
(+ a b)               Add
(- a b)               Subtract
(* a b)               Multiply
(/ a b)               Divide
(% a b)               Modulo
(== a b)              Equal
(!= a b)              Not equal
(< a b) (> a b)       Less/greater
(<= a b) (>= a b)     Less-equal/greater-equal
(&& a b) (|| a b)     Logical and/or
(! a)                 Logical not
```

## Statements

```
§B{name} expr         Bind variable
§R expr               Return value
§P expr               Print (shorthand for Console.WriteLine)
```

## Control Flow

### For Loop
```
§L{id:var:from:to:step}
  ...body...
§/L{id}
```

### While Loop
```
§WH{id} condition
  ...body...
§/WH{id}
```

### Do-While Loop
```
§DO{id}
  ...body (executes at least once)...
§/DO{id} condition
```

### Conditionals (arrow syntax)
```
§IF{id} condition → action
§EI condition → action        ElseIf
§EL → action                  Else
§/I{id}
```

Multi-line form:
```
§IF{id} condition
  ...body...
§EI condition
  ...body...
§EL
  ...body...
§/I{id}
```

## Contracts

```
§Q condition                  Requires (precondition)
§Q{message="err"} condition   With custom error
§S condition                  Ensures (postcondition)
```

## Option/Result

```
§SM value             Some(value)
§NN{type=T}           None of type T
§OK value             Ok(value)
§ERR "message"        Err(message)
```

## Calls

```
§C{Target}
  §A arg1
  §A arg2
§/C
```

## C# Attributes

Attributes attach inline after structural braces using `[@...]` syntax:

```
[@AttributeName]              No arguments
[@Route("api/test")]          Positional argument
[@JsonProperty(Name="id")]    Named argument
```

Example with class and method:
```
§CLASS{c001:TestController:ControllerBase}[@Route("api/[controller]")][@ApiController]
  §METHOD{m001:Get:pub}[@HttpGet]
  §/METHOD{m001}
§/CLASS{c001}
```

## Fields and Properties

```
§FLD{[u8]:_buffer:priv}       Private byte[] field
§FLD{i32:_count:priv}         Private int field

§PROP{p001:Buffer:[u8]:pub}   Public byte[] property
  §GET{pub}
    §R _buffer
  §/GET
§/PROP{p001}
```

## Template: FizzBuzz

```calor
§M{m001:FizzBuzz}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §L{for1:i:1:100:1}
    §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

## Template: Function with Contracts

```calor
§M{m001:Math}
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (>= result 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
```

## Template: Class with Array Fields

```calor
§M{m001:DataProcessor}
§CLASS{c001:DataProcessor}
  §FLD{[u8]:_buffer:priv}
  §FLD{[i32]:_indices:priv}

  §PROP{p001:Buffer:[u8]:pub}
    §GET{pub}
      §R _buffer
    §/GET
  §/PROP{p001}

  §METHOD{m001:ProcessData:pub}
    §I{[str]:args}
    §O{i32}
    §R args.Length
  §/METHOD{m001}
§/CLASS{c001}
§/M{m001}
```

## ID Conventions

- Modules: `m001`, `m002`
- Functions: `f001`, `f002`
- Classes: `c001`, `c002`
- Properties: `p001`, `p002`
- Methods: `m001`, `m002`
- Loops: `for1`, `while1`, `do1`
- Conditionals: `if1`, `if2`
