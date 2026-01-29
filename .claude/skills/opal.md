# /opal - Write OPAL Code

OPAL (Optimized Programming for Agent Logic) compiles to C# via .NET.

**IMPORTANT: Always use v2+ syntax when writing OPAL code:**
- Use Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Use arrow syntax for conditionals: `§IF[id] condition → action`
- Use `§P` for print, `§B` for bindings, `§R` for return
- Do NOT use the legacy v1 syntax with `§OP[kind=...]` or `§REF[name=...]`

## Structure Tags

```
§M[id:Name]           Module (namespace)
§F[id:Name:vis]       Function (pub|pri)
§I[type:name]         Input parameter
§O[type]              Output/return type
§E[effects]           Side effects: cw,cr,fw,fr,net,db
§/M[id] §/F[id]       Close tags (ID must match)
```

## Types

```
i32, i64, f32, f64    Numbers
str, bool, void       String, boolean, unit
?T                    Option<T> (nullable)
T!E                   Result<T,E> (fallible)
```

## Lisp-Style Expressions (v2+)

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
§B[name] expr         Bind variable
§R expr               Return value
§P expr               Print (shorthand for Console.WriteLine)
```

## Control Flow

### For Loop
```
§L[id:var:from:to:step]
  ...body...
§/L[id]
```

### While Loop
```
§WH[id] condition
  ...body...
§/WH[id]
```

### Conditionals (v2 arrow syntax)
```
§IF[id] condition → action
§EI condition → action        ElseIf
§EL → action                  Else
§/I[id]
```

Multi-line form:
```
§IF[id] condition
  ...body...
§EI condition
  ...body...
§EL
  ...body...
§/I[id]
```

## Contracts

```
§Q condition                  Requires (precondition)
§Q[message="err"] condition   With custom error
§S condition                  Ensures (postcondition)
```

## Option/Result

```
§SOME value           Some(value)
§NONE[type=T]         None of type T
§OK value             Ok(value)
§ERR "message"        Err(message)
```

## Calls

```
§C[Target]
  §A arg1
  §A arg2
§/C
```

## C# Attributes

Attributes attach inline after structural brackets:

```
[@AttributeName]              No arguments
[@Route("api/test")]          Positional argument
[@JsonProperty(Name="id")]    Named argument
```

Example with class and method:
```
§CLASS[c001:TestController:ControllerBase][@Route("api/[controller]")][@ApiController]
  §METHOD[m001:Get:pub][@HttpGet]
  §/METHOD[m001]
§/CLASS[c001]
```

## Template: FizzBuzz

```opal
§M[m001:FizzBuzz]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]
```

## Template: Function with Contracts

```opal
§M[m001:Math]
§F[f001:SafeDivide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q (!= b 0)
  §S (>= result 0)
  §R (/ a b)
§/F[f001]
§/M[m001]
```

## ID Conventions

- Modules: `m001`, `m002`
- Functions: `f001`, `f002`
- Loops: `for1`, `while1`
- Conditionals: `if1`, `if2`
