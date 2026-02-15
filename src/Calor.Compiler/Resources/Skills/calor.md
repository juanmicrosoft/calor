# /calor - Write Calor Code

Calor (Optimized Programming for Agents Language) compiles to C# via .NET.

**Calor syntax:**
- Use Lisp-style expressions: `(+ a b)`, `(== x 0)`, `(% i 15)`
- Use arrow syntax for conditionals: `§IF{id} condition → action`
- Use `§P` for print, `§B` for bindings, `§R` for return

## Semantic Guarantees

Calor has formal semantics (v1.0.0) that differ from C#. **Do not assume C# behavior.**

| Rule | Calor Behavior | Test |
|------|----------------|------|
| Evaluation Order | Strictly left-to-right for all expressions | S1, S2 |
| Short-Circuit | `&&`/`||` always short-circuit | S3, S4 |
| Scoping | Lexical with shadowing; inner scope does NOT mutate outer | S5 |
| Integer Overflow | TRAP by default (throws `OverflowException`) | S7 |
| Type Coercion | Explicit for narrowing; implicit only for widening | S8 |
| Contracts | `§Q` before body, `§S` after body | S10 |

See `docs/semantics/core.md` for full specification.

## Structure Tags

```
§M{id:Name}           Module (namespace)
§F{id:Name:vis}       Function (pub|pri)
§I{type:name}         Input parameter
§O{type}              Output/return type
§E{effects}           Side effects: cw,cr,fs:r,fs:w,net:rw,db:rw
§U{namespace}         Using directive (using namespace)
§U{alias:namespace}   Using with alias (using alias = namespace)
§U{static:namespace}  Using static (using static namespace)
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

### Array Operations

```
§ARR elem1 elem2 §/ARR    Array literal
§IDX array index          Array access (array[index])
§IDX array §^ n           Index from end (array[^n])
§^ n                      Index from end operator (^n)
§LEN array                Array length (array.Length)
§SETIDX{array} idx val    Set element at index (array[idx] = val)
```

**CRITICAL: Don't mix tag-style (§IDX) inside Lisp expressions:**
```
// WRONG: §IDX inside Lisp expression
§ASSIGN sum (+ sum §IDX data i)    ← ERROR

// CORRECT: Use a binding first
§B{val} §IDX data i
§ASSIGN sum (+ sum val)             ← Works
```

### Type Casting

```
§AS{targetType} expr      Safe cast (as operator)
§CAST{targetType} expr    Explicit cast
```

### Character Operations

**CRITICAL: Calor does NOT support single-quoted character literals.**

```
// WRONG - single quotes cause "Unexpected character '''" error
(== c '0')        ← ERROR
(== c '-')        ← ERROR
```

**Use character classification predicates or numeric code comparisons:**

```
// Character classification
(is-digit c)        true if c is '0'-'9'
(is-letter c)       true if c is a letter
(is-whitespace c)   true if c is space, tab, newline
(is-upper c)        true if c is uppercase
(is-lower c)        true if c is lowercase

// Character extraction and conversion
(char-at s i)       Get character at index i from string s
(char-code c)       Get Unicode code point: 'A' → 65
(char-from-code n)  Create char from code: 65 → 'A'
(char-upper c)      Convert to uppercase
(char-lower c)      Convert to lowercase

// Compare characters using numeric codes
(== (char-code c) 48)   c == '0' (code 48)
(== (char-code c) 45)   c == '-' (code 45)
```

**Common Character Codes:**
| Char | Code | Char | Code |
|------|------|------|------|
| '0'  | 48   | '9'  | 57   |
| 'A'  | 65   | 'Z'  | 90   |
| 'a'  | 97   | 'z'  | 122  |
| '-'  | 45   | '_'  | 95   |
| ' '  | 32   | '.'  | 46   |

### String Operations

```
(len s)           String length
(contains s t)    Contains substring
(starts s t)      Starts with
(ends s t)        Ends with
(indexof s t)     Index of (first occurrence, 2 args only!)
(isempty s)       Is null or empty
(equals s t)      String equality

(upper s)         To uppercase
(lower s)         To lowercase
(trim s)          Trim whitespace
(substr s i n)    Substring (start, length)
(substr s i)      Substring to end
(replace s a b)   Replace all occurrences

(str x)           Convert any value to string
(concat a b c)    Concatenate strings
(fmt "{0}" x)     Format string (C# String.Format style)
```

**NOTE:** No `<` or `>` comparison on strings. Use `(equals)` for equality.
`(indexof)` takes exactly 2 arguments - no startIndex parameter.

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
§B{type:name} expr    Bind with explicit type
§R expr               Return value
§P expr               Print line (Console.WriteLine)
§Pf expr              Print without newline (Console.Write)
§ASSIGN target val    Assignment statement
```

### Explicit Body Markers

```
§BODY                 Start function body (optional)
...statements...
§END_BODY             End function body (optional)
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

### Break and Continue
```
§BK                       Break out of loop
§CN                       Continue to next iteration
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

**CRITICAL: §IF is a STATEMENT, not an expression.**
```
// WRONG - cannot use §IF to assign a value
§B{x} §IF{if1} (< n 0) → (- 0 n) §EL → n §/I{if1}  ← ERROR

// CORRECT - use separate branches
§IF{if1} (< n 0)
  §B{x} (- 0 n)
§EL
  §B{x} n
§/I{if1}
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
§CL{c001:TestController:ControllerBase}[@Route("api/[controller]")][@ApiController]
  §MT{m001:Get:pub}[@HttpGet]
  §/MT{m001}
§/CL{c001}
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
  §CL{c001:DataProcessor}
  §FLD{[u8]:_buffer:priv}
  §FLD{[i32]:_indices:priv}

  §PROP{p001:Buffer:[u8]:pub}
    §GET{pub}
      §R _buffer
    §/GET
  §/PROP{p001}

  §MT{m001:ProcessData:pub}
    §I{[str]:args}
    §O{i32}
    §R args.Length
  §/MT{m001}
§/CL{c001}
§/M{m001}
```

## Generics

### Generic Functions and Classes

Type parameters use `<T>` suffix syntax after tag attributes:

```
§F{id:Name:pub}<T>            Generic function with one type param
§F{id:Name:pub}<T, U>         Generic function with two type params
§CL{id:Name}<T>               Generic class
§IFACE{id:Name}<T>            Generic interface
§MT{id:Name:vis}<T>           Generic method
```

### Generic Type References

Use angle brackets inline for generic types:

```
§I{List<T>:items}             List<T> parameter
§I{Dictionary<str, T>:lookup} Dictionary parameter
§O{IEnumerable<T>}            Generic return type
§FLD{List<T>:_items:pri}      Generic field
```

### Type Constraints (§WHERE)

```
§WHERE T : class              Reference type constraint
§WHERE T : struct             Value type constraint
§WHERE T : new()              Parameterless constructor
§WHERE T : IComparable<T>     Interface constraint
§WHERE T : class, IDisposable Multiple constraints
```

### Template: Generic Repository

```calor
§M{m001:GenericDemo}
§CL{c001:Repository:pub}<T>
  §WHERE T : class
  §FLD{List<T>:_items:pri}

  §CTOR{ct001:pub}
    §ASSIGN _items §NEW{List<T>}
  §/CTOR{ct001}

  §MT{m001:Add:pub}
    §I{T:item}
    §O{void}
    §C{_items.Add} §A item §/C
  §/MT{m001}

  §MT{m002:GetAll:pub}
    §O{IReadOnlyList<T>}
    §R _items
  §/MT{m002}
§/CL{c001}
§/M{m001}
```

## Interfaces

```
§IFACE{id:Name}           Interface definition
  §MT{id:MethodName}      Method signature
    §I{type:param}        Parameters
    §O{returnType}        Return type
  §/MT{id}
§/IFACE{id}

§IFACE{id:Name}<T>        Generic interface
  §WHERE T : constraint   Type constraint
```

### Template: Interface Definition

```calor
§M{m001:Shapes}
§IFACE{i001:IShape}
  §MT{m001:Area}
    §O{f64}
  §/MT{m001}
  §MT{m002:Perimeter}
    §O{f64}
  §/MT{m002}
§/IFACE{i001}
§/M{m001}
```

### Template: Generic Interface

```calor
§IFACE{i001:IRepository:pub}<T>
  §WHERE T : class
  §MT{m001:GetById}
    §I{i32:id}
    §O{T}
  §/MT{m001}
§/IFACE{i001}
```

## Class Inheritance

```
§CL{id:Name:modifiers}    Class definition
  modifiers: abs, seal, pub, pri
§EXT{BaseClass}           Extends (class inheritance)
§IMPL{InterfaceName}      Implements (interface)
```

### Method Modifiers

```
§VR                       Virtual method modifier
§OV                       Override method modifier
§AB                       Abstract method modifier
§SD                       Sealed method modifier
```

Example: `§MT{mt001:GetValue:pub:over}` uses `over` as inline modifier,
or use `§VR`/`§OV`/`§AB`/`§SD` as standalone tags.

## Records and Union Types

```
§D{Name} (type:field, ...)   Record (data class)
§/D{Name}                    End record (if block form)

§T{Name}                     Type/Union definition
  §V{VariantName}            Variant case
  §V{VariantName} (fields)   Variant with data
§/T{Name}                    End type
```

### Template: Result Type

```calor
§T{Result}
  §V{Ok} (i32:value)
  §V{Err} (str:message)
§/T{Result}
```

### Template: Class Inheritance

```calor
§M{m001:Shapes}
§CL{c001:Shape:pub abs}
  §MT{mt001:Area:pub:abs}
    §O{f64}
  §/MT{mt001}
§/CL{c001}

§CL{c002:Circle:pub}
  §EXT{Shape}
  §FLD{f64:radius:pri}
  §MT{mt001:Area:pub:over}
    §O{f64}
    §R (* 3.14159 (* radius radius))
  §/MT{mt001}
§/CL{c002}
§/M{m001}
```

### Template: Interface Implementation

```calor
§CL{c001:GameObject:pub}
  §IMPL{IMovable}
  §IMPL{IDrawable}
  §MT{mt001:Move:pub}
    §O{void}
  §/MT{mt001}
  §MT{mt002:Draw:pub}
    §O{void}
  §/MT{mt002}
§/CL{c001}
```

## Constructors

```
§CTOR{id:visibility}      Constructor
  §I{type:param}          Parameters
  §BASE §A arg §/BASE     Call base constructor
  §THIS §A arg §/THIS     Call this constructor
  §ASSIGN target value    Field assignment
§/CTOR{id}

§SET                      Property setter body
§/SET                     End property setter
§INIT                     Init-only setter (C# 9+)
§DEFAULT                  Default value expression
```

### Template: Constructor with Base Call

```calor
§M{m001:Animals}
§CL{c001:Animal:pub}
  §FLD{str:_name:pro}
  §CTOR{ctor001:pub}
    §I{str:name}
    §ASSIGN §THIS._name name
  §/CTOR{ctor001}
§/CL{c001}

§CL{c002:Dog:pub}
  §EXT{Animal}
  §FLD{str:_breed:pri}
  §CTOR{ctor001:pub}
    §I{str:name}
    §I{str:breed}
    §BASE §A name §/BASE
    §ASSIGN §THIS._breed breed
  §/CTOR{ctor001}
§/CL{c002}
§/M{m001}
```

## Object Creation

```
§NEW{TypeName}            Create new instance
  §A arg1                 Constructor arguments
  §A arg2
§/NEW                     (optional closing tag)
```

### Template: Object Creation

```calor
§B{Item:item} §NEW{Item} §A 42
§B{Person:person} §NEW{Person} §A "Alice" §A 30
```

## THIS and BASE Expressions

```
§THIS                     Reference to current instance
§THIS.property            Access member via this
§BASE                     Reference to base class
§BASE.method              Access base class member
§C{base.Method} §/C       Call base class method
```

### Template: Override with Base Call

```calor
§CL{c001:Derived:pub}
  §EXT{Base}
  §MT{mt001:GetValue:pub:over}
    §O{i32}
    §R (+ §C{base.GetValue} §/C 5)
  §/MT{mt001}
§/CL{c001}
```

## Field Assignment

```
§ASSIGN target value      Assign value to target
§ASSIGN §THIS.field val   Assign to instance field
```

## Enums

```
§EN{id:Name}              Simple enum (preferred)
  Red
  Green
  Blue
§/EN{id}

§ENUM{id:Name}            Enum (legacy alias for §EN)
§/ENUM{id}                End enum (legacy)

§EN{id:Name:underlyingType}  Enum with underlying type
  Ok = 200
  NotFound = 404
  Error = 500
§/EN{id}
```

Underlying types: `i8`, `u8`, `i16`, `u16`, `i32`, `u32`, `i64`, `u64`

### Template: Status Enum

```calor
§M{m001:Api}
§EN{e001:StatusCode}
  Ok = 200
  NotFound = 404
  ServerError = 500
§/EN{e001}
§/M{m001}
```

## Enum Extension Methods

```
§EEXT{id:EnumName}          Extension methods for enum
  §F{f001:MethodName:pub}
    §I{EnumType:self}       First param becomes 'this'
    §O{returnType}
    // body
  §/F{f001}
§/EEXT{id}
```

### Template: Color with ToHex Extension

```calor
§M{m001:Colors}
§EN{e001:Color}
  Red
  Green
  Blue
§/EN{e001}

§EEXT{ext001:Color}
  §F{f001:ToHex:pub}
    §I{Color:self}
    §O{str}
    §R §W{sw1} self
      §K Color.Red → "#FF0000"
      §K Color.Green → "#00FF00"
      §K Color.Blue → "#0000FF"
    §/W{sw1}
  §/F{f001}
§/EEXT{ext001}
§/M{m001}
```

## Async/Await

Async functions and methods use `§AF` and `§AMT` tags:

```
§AF{id:Name:vis}          Async function (returns Task<T>)
§/AF{id}                  End async function
§AMT{id:Name:vis}         Async method (returns Task<T>)
§/AMT{id}                 End async method
§AWAIT expr               Await an async operation
§AWAIT{false} expr        Await with ConfigureAwait(false)
§ASYNC                    Async modifier (standalone)
```

### Template: Async Function

```calor
§M{m001:AsyncDemo}
§AF{f001:FetchDataAsync:pub}
  §I{str:url}
  §O{str}
  §B{str:result} §AWAIT §C{client.GetStringAsync} §A url §/C
  §R result
§/AF{f001}
§/M{m001}
```

### Template: Async Method in Class

```calor
§CL{c001:DataService:pub}
  §AMT{mt001:ProcessAsync:pub}
    §I{i32:id}
    §O{str}
    §B{str:data} §AWAIT{false} §C{LoadDataAsync} §A id §/C
    §R data
  §/AMT{mt001}
§/CL{c001}
```

## Collections

### List

```
§LIST{name:elementType}   Create and initialize a list
  value1
  value2
§/LIST{name}

§PUSH{listName} value     Add to end of list
§INS{listName} index val  Insert at index
§SETIDX{listName} idx val Set element at index
§REM{listName} value      Remove first occurrence
§CLR{listName}            Clear all elements
§CNT{listName}            Get count
§HAS{listName} value      Check if contains
```

### Dictionary

```
§DICT{name:keyType:valType}  Create dictionary
  §KV key1 value1
  §KV key2 value2
§/DICT{name}

§PUT{dictName} key value     Add or update entry
§REM{dictName} key           Remove by key
§HAS{dictName} key           Check if key exists
§CLR{dictName}               Clear all entries
```

### HashSet

```
§HSET{name:elementType}   Create hash set
  value1
  value2
§/HSET{name}

§PUSH{setName} value      Add to set
§REM{setName} value       Remove from set
§HAS{setName} value       Check membership
§CLR{setName}             Clear all elements
```

### Iterating Collections

```
§EACH{id:var} collection  Foreach over collection
  ...body...
§/EACH{id}

§EACHKV{id:k:v} dict      Foreach over dictionary key-values
  ...body...
§/EACHKV{id}
```

### Template: Collection Operations

```calor
§M{m001:Collections}
§F{f001:Demo:pub}
  §O{void}
  §E{cw}

  §LIST{numbers:i32}
    1
    2
    3
  §/LIST{numbers}
  §PUSH{numbers} 4
  §INS{numbers} 0 0

  §DICT{ages:str:i32}
    §KV "alice" 30
    §KV "bob" 25
  §/DICT{ages}
  §PUT{ages} "charlie" 35

  §HSET{tags:str}
    "urgent"
    "review"
  §/HSET{tags}

  §EACH{e1:n} numbers
    §P n
  §/EACH{e1}
§/F{f001}
§/M{m001}
```

## Exception Handling

```
§TR{id}                   Try block
  ...try body...
§CA{ExceptionType:varName} Catch clause
  ...catch body...
§CA                       Catch-all (no type)
  ...catch body...
§FI                       Finally block
  ...finally body...
§/TR{id}                  End try block

§TH "message"             Throw new Exception
§TH expr                  Throw expression
§RT                       Rethrow (inside catch)

§CA{Type:var} §WHEN cond  Exception filter
```

### Template: Try/Catch/Finally

```calor
§M{m001:ErrorHandling}
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §TR{t1}
    §R (/ a b)
  §CA{DivideByZeroException:ex}
    §P "Division by zero!"
    §R 0
  §FI
    §P "Cleanup complete"
  §/TR{t1}
§/F{f001}
§/M{m001}
```

### Template: Exception Filter with WHEN

```calor
§TR{t1}
  §TH "Error"
§CA{Exception:ex} §WHEN (== errorCode 42)
  §R "Special handling"
§CA{Exception:ex}
  §RT
§/TR{t1}
```

## Lambdas and Delegates

### Delegate Definition

```
§DEL{id:Name:vis}         Delegate type declaration
  §I{type:param}
  §O{returnType}
§/DEL{id}
```

### Lambda Expressions

```
§LAM{id:param:type}       Single-param lambda
§LAM{id:p1:t1:p2:t2}      Multi-param lambda
  ...body...
§/LAM{id}                 Closing tag required
```

### Template: Delegate and Lambda

```calor
§M{m001:Delegates}
§DEL{d001:Calculator:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
§/DEL{d001}

§F{f001:Demo:pub}
  §O{void}
  §B{Action<i32>:printer} §LAM{lam1:x:i32}
    §P x
  §/LAM{lam1}
  §C{printer} §A 42 §/C
§/F{f001}
§/M{m001}
```

## Events

```
§EVT{id:Name:vis:DelegateType}  Event declaration
§SUB{target.Event} handler      Subscribe (+=)
§UNSUB{target.Event} handler    Unsubscribe (-=)
```

### Template: Events

```calor
§CL{c001:Button:pub}
  §EVT{evt001:Click:pub:EventHandler}
§/CL{c001}

§CL{c002:Handler:pub}
  §MT{mt001:Setup:pub}
    §I{Button:button}
    §O{void}
    §SUB{button.Click} OnClick
  §/MT{mt001}

  §MT{mt002:Cleanup:pub}
    §I{Button:button}
    §O{void}
    §UNSUB{button.Click} OnClick
  §/MT{mt002}

  §MT{mt003:OnClick:pri}
    §I{object:sender}
    §I{EventArgs:e}
    §O{void}
    §P "Clicked!"
  §/MT{mt003}
§/CL{c002}
```

## String Interpolation

```
§INTERP                   Start interpolated string
  "text {expr} more text"
§/INTERP                  End interpolation
```

### Template: Interpolated Strings

```calor
§B{str:name} "World"
§B{str:greeting} §INTERP "Hello, {name}!" §/INTERP
§P greeting
```

## Modern Operators

```
§?? left right            Null coalescing: left ?? right
§?. target member         Null conditional: target?.member
§RANGE start end          Range: start..end
§^ n                      Index from end: ^n
§EXP expr                 Expression (for complex expressions)
§DEFAULT                  Default value (default(T))
```

### With Expression (Records)

```
§WITH{source}             Create copy with modifications
  PropertyName = newValue
§/WITH
```

### Template: Modern Operators

```
§B{?str:name} §NN
§B{str:displayName} §?? name "Anonymous"
§B{[i32]:arr} §ARR 1 2 3 4 5 §/ARR
§B{i32:last} §IDX arr §^ 1
§B{[i32]:slice} §IDX arr §RANGE 1 3
```

Note: `§^` and `§RANGE` syntax requires full program context.

## Switch/Match Expressions

Calor uses `§W` (Match) and `§K` (Case) for pattern matching. Alternative alias `§SW` is also available.

```
§W{id} target             Match expression start
§K pattern → expr         Case with arrow syntax
§K pattern                Case with block body
  ...body...
§/K                       End case (optional for arrow syntax)
§/W{id}                   End match (or §/SW{id})
```

### Template: Switch Expression

```calor
§M{m001:Grades}
§F{f001:GetGrade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → "A"
    §K §PREL{gte} 80 → "B"
    §K §PREL{gte} 70 → "C"
    §K §PREL{gte} 60 → "D"
    §K _ → "F"
  §/W{sw1}
§/F{f001}
§/M{m001}
```

### Template: HTTP Status Switch

```calor
§W{sw1} code
  §K 200 → "OK"
  §K 404 → "Not Found"
  §K 500 → "Server Error"
  §K _ → "Unknown"
§/W{sw1}
```

### Guard Clauses with WHEN

```calor
§W{sw1} x
  §K §VAR{n} §WHEN (> n 100) → "large"
  §K §VAR{n} §WHEN (< n 0) → "negative"
  §K 0 → "zero"
  §K _ → "normal"
§/W{sw1}
```

## Pattern Matching Enhancements

### Relational Patterns

```
§PREL{op} value           Relational pattern
  op: gte, lte, gt, lt    Maps to >=, <=, >, <
```

### Variable Pattern

```
§VAR x                    Captures value into variable x
```

### Property Pattern

```
§PPROP{Type}              Match type with properties
  PropertyName = pattern
§/PPROP
```

### Positional Pattern

```
§PPOS{Type}               Match type with positional values
  pattern1
  pattern2
§/PPOS
```

### List Pattern

```
§PLIST                    Match list structure
  pattern1
  pattern2
  §REST                   Rest of list (..)
§/PLIST
```

### Template: Advanced Pattern Matching

```calor
§R §W{sw1} value
  §K §PREL{lt} 0 → "negative"
  §K §PREL{gte} 100 → "large"
  §K §VAR{n} §WHEN (== (% n 2) 0) → "even"
  §K _ → "odd"
§/W{sw1}
```

### Property Match Pattern

```
§PMATCH{property} pattern    Match property value inline
```

## Extended Features: Metadata

### Issue Tracking

```
§TD "description"            Todo marker
§TD{id:category:priority}    Todo with metadata
§FX "description"            Fixme marker (bug to fix)
§FX{id:category:priority}    Fixme with metadata
§HK "description"            Hack marker (workaround)
§HK{id} "description"        Hack with id
```

### Inline Examples

```
§EX expr → expected          Inline example/test
§EX{id:msg:"desc"} expr → expected   Example with metadata
```

### Dependencies

```
§US{target1, target2}        Uses (dependencies)
§/US                         End uses block
§UB{caller1, caller2}        Used by (reverse dependencies)
§/UB                         End used by block
```

### Versioning and Stability

```
§SN{1.0.0}                   Since (version introduced)
§DP{since:1.0.0}             Deprecated marker
§DP{since:1.0.0:use:NewMethod:reason:"Obsolete"}
§BR{1.5.0} "description"     Breaking change marker
§XP                          Experimental (unstable API)
§SB                          Stable (API guaranteed stable)
```

### Complexity Contracts

```
§CX{time:O(n)}               Time complexity
§CX{time:O(n):space:O(1)}    Time and space complexity
§CX{worst:time:O(n^2)}       Worst-case complexity
```

### Context and Decisions

```
§CT                          Context section start
  ...context documentation...
§/CT                         End context

§DC{id}                      Decision record
  §CHOSEN option             Chosen option
  §REJECTED option           Rejected alternative
  §REASON "explanation"      Rationale
§/DC{id}                     End decision
```

### Visibility Sections

```
§VS                          Visible section (for agents/users)
  ...visible content...
§/VS                         End visible

§HD                          Hidden section (implementation details)
  ...hidden content...
§/HD                         End hidden

§FC target                   Focus marker (highlight importance)
```

### Agent Authorship

```
§AU{agent:agent-id}          Author marker
§AU{agent:id:date:2024-01-15:task:PROJ-123}
§TASK{PROJ-123} "description"  Task reference
§DATE{2024-01-15}            Date marker
§LK{agent:id:expires:time}   Lock (multi-agent editing)
```

### Property Testing

```
§PT predicate                Property test
§PT ∀arr: (== (Reverse (Reverse arr)) arr)
```

### File References

```
§FILE{path/to/file.cs}       Reference external file
§FILE{path} "description"    With description
```

### Field and Invariant

```
§FL{type:name}               Field definition (in types)
§IV condition                Invariant (must always hold)
§IV{msg:"error"} condition   Invariant with message
```

### Collection Access

```
§KEY expr                    Get dictionary key
§VAL expr                    Get dictionary value
```

### Where Clause (Legacy)

```
§WR T : constraint           Where clause (legacy, prefer §WHERE)
```

## ID Integrity Rules

### Canonical IDs (Production Code)
```
f_01J5X7K9M2NPQRSTABWXYZ12    Function
m_01J5X7K9M2NPQRSTABWXYZ12    Module
c_01J5X7K9M2NPQRSTABWXYZ12    Class
mt_01J5X7K9M2NPQRSTABWXYZ12   Method
ctor_01J5X7K9M2NPQRSTABWXYZ12 Constructor
p_01J5X7K9M2NPQRSTABWXYZ12    Property
i_01J5X7K9M2NPQRSTABWXYZ12    Interface
e_01J5X7K9M2NPQRSTABWXYZ12    Enum
```

### Test IDs (ONLY in tests/, docs/, examples/)
```
f001, m001, c001              Sequential test IDs
```

### Agent Rules - CRITICAL
1. **NEVER** modify an existing ID
2. **NEVER** copy IDs when extracting code
3. **OMIT** IDs for new declarations - run `calor ids assign`
4. **VERIFY** before commit: `calor ids check`

### Preservation Rules
| Operation | ID Behavior |
|-----------|-------------|
| Rename | PRESERVE |
| Move file | PRESERVE |
| Reformat | PRESERVE |
| Extract helper | NEW ID |

### Verification Steps
```bash
calor ids check .
calor ids assign . --dry-run
```
