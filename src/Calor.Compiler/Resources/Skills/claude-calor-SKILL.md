---
name: calor
description: Write Calor code with Lisp-style expressions, contracts, and effects. Use for all new code in Calor-first projects.
---

# /calor - Calor Language Reference

Calor compiles to C# via .NET. It combines:
- **§-tags** for structure (functions, classes, control flow)
- **Lisp S-expressions** for operations `(+ a b)`, `(== x 0)`
- **Contracts** for preconditions/postconditions
- **Effects** for tracking side effects

---

## CONTRACT-FIRST METHODOLOGY

**Before writing implementation, extract and express constraints as contracts.** This is your primary tool for ensuring correctness.

### Step 1: Identify Input Constraints (Preconditions)

Read the requirement and find ALL constraints on inputs:
- "must be positive" → `§Q (> n 0)`
- "must not be zero" → `§Q (!= n 0)`
- "X must not exceed Y" → `§Q (<= x y)`
- Any division or modulo operation → `§Q (!= divisor 0)`

### Step 2: Identify Output Guarantees (Postconditions)

Find what the function promises about its result:
- "result is never negative" → `§S (>= result 0)`
- "result is at least 1" → `§S (>= result 1)`
- "result is bounded by input" → `§S (<= result n)`

### Step 3: Write Contracts BEFORE Implementation

The contracts become your specification. **If you can't satisfy a postcondition, your implementation is wrong.**

```calor
// FIRST: Write the contracts
§F{f001:MyFunction:pub}
  §I{i32:n}
  §O{i32}
  §Q (> n 0)           // Input constraint from requirement
  §S (>= result 0)     // Output guarantee from requirement
  // THEN: Implement logic that satisfies the contracts
  §R ...
§/F{f001}
```

### Common Contract Patterns

| Requirement Pattern | Calor Contract |
|---------------------|----------------|
| "must be positive" | `§Q (> n 0)` |
| "must be non-negative" | `§Q (>= n 0)` |
| "must not be zero" | `§Q (!= n 0)` |
| "must be between X and Y (inclusive)" | `§Q (>= n X)` and `§Q (<= n Y)` |
| "must be even" | `§Q (== (% n 2) 0)` |
| "X must not exceed Y" | `§Q (<= x y)` |
| division or modulo by Y | Always `§Q (!= y 0)` |
| "result is never negative" | `§S (>= result 0)` |
| "result is always positive" | `§S (> result 0)` |
| "result is at least 1" | `§S (>= result 1)` |
| "result is bounded by input" | `§S (<= result n)` |

### Self-Verification with Contracts

Use contracts to verify your implementation:
- **If you can't make a postcondition true** → Your implementation is wrong
- **If a precondition seems impossible** → You misunderstood the requirement
- **If contracts conflict** → The requirement has contradictions

---

## 1. MODULES AND FUNCTIONS

### Module
```
§M{id:Name}                   Module (namespace)
  ...contents...
§/M{id}                       Close module
```

### Function
```
§F{id:Name:vis}               Function (vis: pub|pri|prot|int)
  §I{type:name}               Input parameter
  §O{type}                    Output/return type
  §E{effects}                 Side effects
  §Q condition                Precondition (requires)
  §S condition                Postcondition (ensures)
  ...body...
  §R expr                     Return value
§/F{id}                       Close function
```

### Template: Basic Function
```calor
§M{m001:Calculator}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
```

### Template: Function with Contracts
```calor
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (>= result 0)
  §R (/ a b)
§/F{f001}
```

---

## 2. TYPES

### Primitive Types
```
i8, i16, i32, i64             Signed integers
u8, u16, u32, u64             Unsigned integers
f32, f64                      Floating point
str, char, bool, void         String, character, boolean, unit
```

### Collection Types
```
[T]                           Array (e.g., [i32], [str])
[[T]]                         Nested array (e.g., [[i32]])
List<T>                       List<T>
Dict<K,V>                     Dictionary<K,V>
HashSet<T>                    HashSet<T>
```

### Optional/Result Types
```
?T                            Option<T> (shorthand)
Option<T>                     Option<T> (explicit)
T!E                           Result<T,E> (e.g., i32!str)
```

### DateTime Types
```
datetime                      DateTime
datetimeoffset                DateTimeOffset
timespan                      TimeSpan
date                          DateOnly
time                          TimeOnly
guid                          Guid
```

### Async Return Types
```
Task<T>                       Async return type
```

---

## 3. LISP-STYLE EXPRESSIONS

### Arithmetic
```
(+ a b)                       Add / String concatenation
(- a b)                       Subtract
(* a b)                       Multiply
(/ a b)                       Divide
(% a b)                       Modulo
(** a b)                      Power
```

### Comparison
```
(== a b)                      Equal
(!= a b)                      Not equal
(< a b)                       Less than
(<= a b)                      Less or equal
(> a b)                       Greater than
(>= a b)                      Greater or equal
```

### Logical
```
(&& a b)                      Logical AND
(|| a b)                      Logical OR
(! a)                         Logical NOT
```

### Bitwise
```
(& a b)                       Bitwise AND
(| a b)                       Bitwise OR
(^ a b)                       Bitwise XOR
(~ a)                         Bitwise NOT
(<< a n)                      Left shift
(>> a n)                      Right shift
```

### Ternary
```
(? condition then else)       Conditional expression
```

---

## 4. STATEMENTS

### Variable Binding
```
§B{name} expr                 Bind immutable variable
§B{type:name} expr            Bind with explicit type
§B{type:name:mut} expr        Bind mutable variable
```

### Assignment
```
§ASSIGN target value          Assign to mutable variable
§ASSIGN §THIS.field value     Assign to instance field
```

### Return
```
§R expr                       Return value
```

### Print/Debug
```
§P expr                       Console.WriteLine
§Pf expr                      Console.Write (no newline)
§G                            Console.ReadLine
§D expr                       Debug.WriteLine
```

---

## 5. CONTROL FLOW

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
  ...body...
§/DO{id} condition
```

### Break/Continue
```
§BK                           Break
§CN                           Continue
```

### If/ElseIf/Else (Arrow Syntax)
```
§IF{id} condition → action
§EI condition → action
§EL → action
§/I{id}
```

### If/ElseIf/Else (Block Syntax)
```
§IF{id} condition
  ...body...
§EI condition
  ...body...
§EL
  ...body...
§/I{id}
```

### Template: FizzBuzz
```calor
§F{f001:FizzBuzz:pub}
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
```

---

## 6. CONTRACTS

### Preconditions and Postconditions
```
§Q condition                  Requires (precondition)
§Q{message="err"} condition   With custom error message
§S condition                  Ensures (postcondition)
§S{message="err"} condition   With custom error message
```

### Quantifiers
```
(forall ((var type)) body)    Universal: true for ALL values
(exists ((var type)) body)    Existential: true for SOME value
(-> antecedent consequent)    Implication: if A then B
(implies a b)                 Implication (alias)
```

Use `arr{i}` to access array element at index `i` in quantifier body.

### Template: Forall Quantifier
```calor
§F{f001:AllPositive:pub}
  §I{[i32]:arr}
  §O{bool}
  §S (-> result (forall ((i i32)) (> arr{i} 0)))
  §B{bool:allPos} true
  §L{for1:i:0:(- (len arr) 1):1}
    §IF{if1} (<= arr{i} 0) → §ASSIGN allPos false
    §/I{if1}
  §/L{for1}
  §R allPos
§/F{f001}
```

### Template: Exists Quantifier
```calor
§F{f001:HasNegative:pub}
  §I{[i32]:arr}
  §O{bool}
  §S (== result (exists ((i i32)) (< arr{i} 0)))
  §L{for1:i:0:(- (len arr) 1):1}
    §IF{if1} (< arr{i} 0) → §R true
    §/I{if1}
  §/L{for1}
  §R false
§/F{f001}
```

---

## 7. EFFECTS SYSTEM

Declare side effects using `§E{codes}`:

```
cw                            Console write
cr                            Console read
fs:r                          File system read
fs:w                          File system write
net:r                         Network read
net:w                         Network write
db:r                          Database read
db:w                          Database write
```

Combine with commas: `§E{cw,fs:r,fs:w}`

### Template: File Read
```calor
§F{f001:ReadFile:pub}
  §I{str:path}
  §O{str}
  §E{fs:r}
  §R §C{File.ReadAllText} §A path §/C
§/F{f001}
```

### Template: File Write
```calor
§F{f001:WriteFile:pub}
  §I{str:path}
  §I{str:content}
  §O{void}
  §E{fs:w}
  §C{File.WriteAllText} §A path §A content §/C
§/F{f001}
```

### Template: Multiple Effects
```calor
§F{f001:ProcessFile:pub}
  §I{str:inputPath}
  §I{str:outputPath}
  §O{void}
  §E{fs:r,fs:w,cw}
  §B{str:content} §C{File.ReadAllText} §A inputPath §/C
  §B{str:processed} §C{content.ToUpper} §/C
  §C{File.WriteAllText} §A outputPath §A processed §/C
  §P "File processed"
§/F{f001}
```

---

## 8. OPTION AND RESULT TYPES

### Option Constructors
```
§SM value                     Some(value)
§NN                           None (type inferred)
§NN{type=T}                   None of explicit type
```

### Result Constructors
```
§OK value                     Ok(value)
§ERR "message"                Err(message)
§ERR expr                     Err with expression
```

### Template: Option Some/None
```calor
§F{f001:TryDouble:pub}
  §I{i32:x}
  §O{Option<i32>}
  §IF{if1} (> x 0) → §R §SM (* x 2)
  §EL → §R §NN
  §/I{if1}
§/F{f001}

§F{f002:GetNothing:pub}
  §O{Option<i32>}
  §R §NN
§/F{f002}
```

### Template: Result Ok/Err
```calor
§F{f003:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32!str}
  §IF{if1} (!= b 0) → §R §OK (/ a b)
  §EL → §R §ERR "Division by zero"
  §/I{if1}
§/F{f003}

§F{f004:TryParse:pub}
  §I{str:input}
  §O{i32!str}
  §R §OK 42
§/F{f004}

§F{f005:AlwaysFail:pub}
  §O{i32!str}
  §R §ERR "This always fails"
§/F{f005}
```

---

## 9. FUNCTION CALLS

```
§C{Target}                    Call function/method
  §A arg1                     Argument
  §A arg2
§/C                           Close call
```

Inline form: `§C{Target} §A arg §/C`

### Template: Method Call Chain
```calor
§B{str:upper} §C{input.ToUpper} §/C
§B{str:trimmed} §C{upper.Trim} §/C
```

---

## 10. STRING OPERATIONS

### Query Operations (return non-string)
```
(len s)                       s.Length → int
(contains s text)             s.Contains(text) → bool
(starts s prefix)             s.StartsWith(prefix) → bool
(ends s suffix)               s.EndsWith(suffix) → bool
(indexof s substr)            s.IndexOf(substr) → int
(isempty s)                   string.IsNullOrEmpty(s) → bool
(isblank s)                   string.IsNullOrWhiteSpace(s) → bool
(equals s t)                  s.Equals(t) → bool
```

### Transform Operations (return string)
```
(upper s)                     s.ToUpper()
(lower s)                     s.ToLower()
(trim s)                      s.Trim()
(ltrim s)                     s.TrimStart()
(rtrim s)                     s.TrimEnd()
(substr s start len)          s.Substring(start, len)
(substr s start)              s.Substring(start)
(replace s old new)           s.Replace(old, new)
(lpad s width)                s.PadLeft(width)
(rpad s width)                s.PadRight(width)
```

### Static Operations
```
(concat a b c)                string.Concat(a, b, c)
(join sep items)              string.Join(sep, items)
(split s delim)               s.Split(delim)
(fmt template args...)        string.Format(template, args)
(str x)                       x.ToString()
```

### Regex Operations
```
(regex-test s pattern)        Regex.IsMatch(s, pattern) → bool
(regex-match s pattern)       Regex.Match(s, pattern) → Match
(regex-replace s pat rep)     Regex.Replace(s, pat, rep) → string
(regex-split s pattern)       Regex.Split(s, pattern) → string[]
```

---

## 11. CHARACTER OPERATIONS

### Extraction
```
(char-at s index)             s[index] → char
(char-code c)                 (int)c → int
(char-from-code n)            (char)n → char
```

### Classification (return bool)
```
(is-letter c)                 char.IsLetter(c)
(is-digit c)                  char.IsDigit(c)
(is-whitespace c)             char.IsWhiteSpace(c)
(is-upper c)                  char.IsUpper(c)
(is-lower c)                  char.IsLower(c)
```

### Transformation (return char)
```
(char-upper c)                char.ToUpper(c)
(char-lower c)                char.ToLower(c)
```

### Template: String Length
```calor
§F{f001:StringLength:pub}
  §I{str:s}
  §O{i32}
  §R (len s)
§/F{f001}
```

### Template: Get First Character
```calor
§F{f002:GetFirstChar:pub}
  §I{str:s}
  §O{char}
  §Q (> (len s) 0)
  §R (char-at s 0)
§/F{f002}
```

---

## 12. ARRAYS

### Array Creation
```
§ARR elem1 elem2 §/ARR        Array literal
§ARR{name:type:size}          Sized array declaration
```

### Array Operations
```
§IDX{array} index             Array access: array[index]
§IDX{array} §^ n              Index from end: array[^n]
§LEN array                    Array length: array.Length
```

### Template: Array Sum
```calor
§F{f001:Sum:pub}
  §I{[i32]:arr}
  §O{i32}
  §B{i32:total} 0
  §L{for1:i:0:(- (len arr) 1):1}
    §ASSIGN total (+ total §IDX{arr} i)
  §/L{for1}
  §R total
§/F{f001}
```

---

## 13. COLLECTIONS

### List
```
§LIST{name:type}              Create list
  value1
  value2
§/LIST{name}

§PUSH{list} value             Add to end
§INS{list} index value        Insert at index
§SETIDX{list} index value     Set at index
§REM{list} value              Remove first occurrence
§CLR{list}                    Clear all
§CNT{list}                    Count
§HAS{list} value              Contains check
```

### Dictionary
```
§DICT{name:keyType:valType}   Create dictionary
  §KV key1 value1
  §KV key2 value2
§/DICT{name}

§PUT{dict} key value          Add or update
§REM{dict} key                Remove by key
§HAS{dict} key                Contains key
§CLR{dict}                    Clear all
```

### HashSet
```
§HSET{name:type}              Create hash set
  value1
  value2
§/HSET{name}

§ADD{set} value               Add to set (idiomatic)
§PUSH{set} value              Add to set (alias)
§REM{set} value               Remove from set
§HAS{set} value               Contains check
§CLR{set}                     Clear all
```

### Iteration
```
§EACH{id:var} collection      Foreach (type inferred)
§EACH{id:var:type} collection  Foreach (explicit type)
  ...body...
§/EACH{id}

§EACHKV{id:key:val} dict      Foreach over dictionary
  ...body...
§/EACHKV{id}
```

### Template: List Operations
```calor
§F{f001:SumList:pub}
  §O{i32}
  §LIST{numbers:i32}
    1
    2
    3
  §/LIST{numbers}
  §PUSH{numbers} 4
  §PUSH{numbers} 5
  §B{i32:total} 0
  §EACH{e1:n} numbers
    §ASSIGN total (+ total n)
  §/EACH{e1}
  §R total
§/F{f001}
```

### Template: HashSet Operations
```calor
§F{f001:UniqueCount:pub}
  §I{i32:a}
  §I{i32:b}
  §I{i32:c}
  §O{i32}
  §HSET{unique:i32}
  §/HSET{unique}
  §ADD{unique} a
  §ADD{unique} b
  §ADD{unique} c
  §R §CNT{unique}
§/F{f001}
```

---

## 14. CLASSES

### Class Definition
```
§CL{id:Name:modifiers}        Class (modifiers: pub, pri, abs, seal)
  §FLD{type:name:vis}         Field
  §PROP{id:Name:type:vis}     Property
  §CTOR{id:vis}               Constructor
  §MT{id:Name:vis:mods}       Method (mods: abs, virt, over, seal, stat)
§/CL{id}
```

### Inheritance and Interfaces
```
§EXT{BaseClass}               Extends base class
§IMPL{IInterface}             Implements interface
```

### Template: Simple Class with Method
```calor
§CL{c001:Calculator:pub}
  §MT{m001:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (+ a b)
  §/MT{m001}
§/CL{c001}
```

### Template: Class with Auto-Property
```calor
§CL{c002:Product:pub}
  §PROP{p001:Price:i32:pub}
    §GET
    §SET
  §/PROP{p001}
§/CL{c002}
```

### Template: Class with Constructor
```calor
§CL{c003:Person:pub}
  §FLD{str:_name:pri}
  §FLD{i32:_age:pri}

  §CTOR{ctor001:pub}
    §I{str:name}
    §I{i32:age}
    §ASSIGN §THIS._name name
    §ASSIGN §THIS._age age
  §/CTOR{ctor001}

  §MT{m001:GetName:pub}
    §O{str}
    §R _name
  §/MT{m001}
§/CL{c003}
```

### Template: Inheritance
```calor
§CL{c001:Animal:pub}
  §FLD{str:_name:pro}
  §MT{m001:Speak:pub:virt}
    §O{str}
    §R "..."
  §/MT{m001}
§/CL{c001}

§CL{c002:Dog:pub}
  §EXT{Animal}
  §MT{m001:Speak:pub:over}
    §O{str}
    §R "Woof!"
  §/MT{m001}
§/CL{c002}
```

---

## 15. INTERFACES

```
§IFACE{id:Name:vis}           Interface definition
  §MT{id:MethodName}          Method signature
    §I{type:param}
    §O{returnType}
  §/MT{id}
§/IFACE{id}
```

### Template: Interface
```calor
§IFACE{i001:IShape:pub}
  §MT{m001:Area}
    §O{f64}
  §/MT{m001}
  §MT{m002:Perimeter}
    §O{f64}
  §/MT{m002}
§/IFACE{i001}
```

---

## 16. PROPERTIES

```
§PROP{id:Name:type:vis}       Property definition
  §GET                        Getter (expression or block)
    expr or ...body...
  §/GET
  §SET                        Setter
    ...body...
  §/SET
  §INIT                       Init-only setter
§/PROP{id}
```

### Template: Property with Getter/Setter
```calor
§CL{c001:Product:pub}
  §FLD{f64:_price:pri}

  §PROP{p001:Price:f64:pub}
    §GET
      §R _price
    §/GET
    §SET
      §Q (>= value 0)
      §ASSIGN _price value
    §/SET
  §/PROP{p001}
§/CL{c001}
```

---

## 17. CONSTRUCTORS

```
§CTOR{id:vis}                 Constructor
  §I{type:param}              Parameters
  §BASE §A arg §/BASE         Call base constructor
  §THIS §A arg §/THIS         Call this constructor
  §ASSIGN target value        Field assignment
§/CTOR{id}
```

### Template: Constructor with Base Call
```calor
§CL{c001:Manager:pub}
  §EXT{Employee}
  §FLD{i32:_teamSize:pri}

  §CTOR{ctor001:pub}
    §I{str:name}
    §I{i32:teamSize}
    §BASE §A name §/BASE
    §ASSIGN §THIS._teamSize teamSize
  §/CTOR{ctor001}
§/CL{c001}
```

---

## 18. OBJECT CREATION

```
§NEW{TypeName}                Create instance
  §A arg1                     Constructor arguments
  §A arg2
```

### Template: Object Creation
```calor
§B{Person:person} §NEW{Person} §A "Alice" §A 30
§B{List<i32>:numbers} §NEW{List<i32>}
```

---

## 19. ENUMS

```
§EN{id:Name}                  Simple enum
  Value1
  Value2 = 5
  Value3
§/EN{id}

§EN{id:Name:type}             Enum with underlying type
  Value1 = 0
  Value2 = 1
§/EN{id}
```

Underlying types: `i8`, `u8`, `i16`, `u16`, `i32`, `u32`, `i64`, `u64`

### Template: Enum with Explicit Values
```calor
§EN{e001:Color}
  Red = 1
  Green = 2
  Blue = 4
§/EN{e001}
```

### Enum Extension Methods
```
§EEXT{id:EnumName}            Extension methods for enum
  §F{...}...§/F{...}
§/EEXT{id}
```

### Template: Enum with Extension
```calor
§EN{e001:Status}
  Pending
  Active
  Completed
§/EN{e001}

§EEXT{ext001:Status}
  §F{f001:IsFinished:pub}
    §I{Status:self}
    §O{bool}
    §R (== self Status.Completed)
  §/F{f001}
§/EEXT{ext001}
```

---

## 20. GENERICS

### Generic Functions and Classes
```
§F{id:Name:vis}<T>            Generic function
§F{id:Name:vis}<T, U>         Multiple type parameters
§CL{id:Name}<T>               Generic class
§IFACE{id:Name}<T>            Generic interface
§MT{id:Name:vis}<T>           Generic method
```

### Type Constraints
```
§WHERE T : class              Reference type
§WHERE T : struct             Value type
§WHERE T : new()              Has parameterless constructor
§WHERE T : IComparable<T>     Implements interface
§WHERE T : BaseClass          Inherits from class
```

### Template: Generic Repository
```calor
§CL{c001:Repository:pub}<T>
  §WHERE T : class
  §FLD{List<T>:_items:pri}

  §CTOR{ctor001:pub}
    §ASSIGN _items §NEW{List<T>}
  §/CTOR{ctor001}

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
```

---

## 21. ASYNC/AWAIT

```
§AF{id:Name:vis}              Async function (returns Task<T>)
  §O{T}                       Return type (wrapped in Task automatically)
  §E{effects}                 Effects (typically net:r)
§/AF{id}

§AMT{id:Name:vis}             Async method in class
§/AMT{id}

§AWAIT expr                   Await expression
§AWAIT{false} expr            Await with ConfigureAwait(false)
```

### Template: Async Function
```calor
§AF{f001:FetchDataAsync:pub}
  §I{str:url}
  §O{str}
  §E{net:r}
  §B{str:result} §AWAIT §C{client.GetStringAsync} §A url §/C
  §R result
§/AF{f001}
```

### Template: Async Method in Class
```calor
§CL{c001:DataService:pub}
  §AMT{m001:LoadAsync:pub}
    §I{i32:id}
    §O{str}
    §E{net:r}
    §B{str:data} §AWAIT{false} §C{FetchAsync} §A id §/C
    §R data
  §/AMT{m001}
§/CL{c001}
```

---

## 22. SWITCH/MATCH EXPRESSIONS

```
§W{id} target                 Match expression
  §K pattern → expr           Case with arrow (expression)
  §K pattern                  Case with block
    ...body...
  §/K
§/W{id}
```

Alternative: `§SW{id}` and `§/SW{id}`

### Patterns
```
§K value → expr               Literal pattern
§K _ → expr                   Wildcard (default)
§K §VAR{name} → expr          Variable pattern
§K §VAR{name} §WHEN cond      Variable with guard
§K §PREL{op} value            Relational (op: gt, gte, lt, lte)
§K §SM §VAR{x} → expr         Option Some pattern
§K §NN → expr                 Option None pattern
§K §OK §VAR{x} → expr         Result Ok pattern
§K §ERR §VAR{e} → expr        Result Err pattern
```

### Template: Switch with Literal Patterns
```calor
§F{f001:DayCategory:pub}
  §I{i32:day}
  §O{i32}
  §R §W{sw1} day
    §K 1 → 100
    §K 2 → 100
    §K 3 → 100
    §K 4 → 100
    §K 5 → 100
    §K 6 → 200
    §K 7 → 200
    §K _ → 0
  §/W{sw1}
§/F{f001}
```

### Template: Grade Calculator (Relational Patterns)
```calor
§F{f002:GetGrade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → "A"
    §K §PREL{gte} 80 → "B"
    §K §PREL{gte} 70 → "C"
    §K §PREL{gte} 60 → "D"
    §K _ → "F"
  §/W{sw1}
§/F{f002}
```

### Template: Option Matching
```calor
§F{f001:UnwrapOrDefault:pub}
  §I{Option<i32>:opt}
  §I{i32:defaultVal}
  §O{i32}
  §R §W{sw1} opt
    §K §SM §VAR{x} → x
    §K §NN → defaultVal
  §/W{sw1}
§/F{f001}
```

### Template: Result Matching
```calor
§F{f001:HandleResult:pub}
  §I{i32!str:result}
  §O{i32}
  §R §W{sw1} result
    §K §OK §VAR{val} → val
    §K §ERR §VAR{msg} → -1
  §/W{sw1}
§/F{f001}
```

---

## 23. EXCEPTION HANDLING

```
§TR{id}                       Try block
  ...protected code...
§CA{ExceptionType:var}        Catch with type
  ...handler...
§CA{Type:var} §WHEN cond      Catch with filter
  ...handler...
§CA                           Catch-all
  ...handler...
§FI                           Finally block
  ...cleanup...
§/TR{id}

§TH expr                      Throw exception
§TH "message"                 Throw new Exception(message)
§RT                           Rethrow (inside catch)
```

### Template: Try/Catch/Finally
```calor
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
```

---

## 24. LAMBDAS AND DELEGATES

### Inline Lambda (Arrow Syntax)
```
(param) → expr                Single parameter
(type:param) → expr           Typed parameter
() → expr                     No parameters
(p1, p2) → expr               Multiple parameters
```

### Block Lambda
```
§LAM{id:param:type}           Lambda with block body
  ...body...
§/LAM{id}

§LAM{id:p1:t1:p2:t2}          Multiple parameters
  ...body...
§/LAM{id}
```

### Delegate Definition
```
§DEL{id:Name:vis}             Delegate type
  §I{type:param}
  §O{returnType}
§/DEL{id}
```

### Template: Inline Lambda
```calor
§F{f001:ApplyDouble:pub}
  §I{i32:x}
  §O{i32}
  §B{Func<i32,i32>:doubler} (n) → (* n 2)
  §R §C{doubler} §A x §/C
§/F{f001}
```

### Template: Block Lambda
```calor
§F{f001:ApplyComplex:pub}
  §I{i32:x}
  §O{i32}
  §B{Func<i32,i32>:processor} §LAM{lam1:n:i32}
    §IF{if1} (> n 0) → §R (* n 2)
    §EL → §R 0
    §/I{if1}
  §/LAM{lam1}
  §R §C{processor} §A x §/C
§/F{f001}
```

---

## 25. EVENTS

```
§EVT{id:Name:vis:DelegateType}  Event declaration
§SUB{target.Event} handler      Subscribe (+=)
§UNSUB{target.Event} handler    Unsubscribe (-=)
```

### Template: Event Subscription
```calor
§CL{c001:Button:pub}
  §EVT{evt001:Click:pub:EventHandler}
§/CL{c001}

§CL{c002:Handler:pub}
  §MT{m001:Setup:pub}
    §I{Button:btn}
    §O{void}
    §SUB{btn.Click} OnClick
  §/MT{m001}

  §MT{m002:OnClick:pri}
    §I{object:sender}
    §I{EventArgs:e}
    §O{void}
    §P "Clicked!"
  §/MT{m002}
§/CL{c002}
```

---

## 26. RECORDS AND VARIANTS

### Record Definition
```
§REC{id:Name}                 Record type
  §FLD{type:name}             Record field
§/REC{id}
```

### Discriminated Union (Variant)
```
§TYPE{id:Name}                Union type
  §VARIANT{Name}              Variant
    §FLD{type:name}           Variant field
  §/VARIANT
§/TYPE{id}
```

### Template: Record
```calor
§REC{r001:Person}
  §FLD{str:FirstName}
  §FLD{str:LastName}
  §FLD{i32:Age}
§/REC{r001}
```

### Template: Discriminated Union
```calor
§TYPE{t001:Shape}
  §VARIANT{Circle}
    §FLD{f64:Radius}
  §/VARIANT
  §VARIANT{Rectangle}
    §FLD{f64:Width}
    §FLD{f64:Height}
  §/VARIANT
§/TYPE{t001}
```

---

## 27. MODERN OPERATORS

```
§?? left right                Null coalescing: left ?? right
§?. target member             Null conditional: target?.member
§RANGE start end              Range: start..end
§^ n                          Index from end: ^n
```

### With Expression (Records)
```
§WITH{source}                 Non-destructive update
  PropertyName = newValue
§/WITH
```

### String Interpolation
```
§INTERP "text {expr} more"    Interpolated string
§/INTERP
```

---

## 28. C# ATTRIBUTES

```
[@AttributeName]              Simple attribute
[@Route("api/test")]          With positional arg
[@JsonProperty(Name="id")]    With named arg
```

### Template: API Controller
```calor
§CL{c001:UsersController:ControllerBase}[@Route("api/[controller]")][@ApiController]
  §MT{m001:Get:pub}[@HttpGet]
    §O{IEnumerable<User>}
    §R users
  §/MT{m001}

  §MT{m002:GetById:pub}[@HttpGet("{id}")]
    §I{i32:id}
    §O{User}
    §R §C{FindUser} §A id §/C
  §/MT{m002}
§/CL{c001}
```

---

## 29. USING DIRECTIVES

```
§U{namespace}                 Using directive
```

### Template: Using Statements
```calor
§U{System}
§U{System.Collections.Generic}
§U{System.Linq}

§M{m001:MyModule}
  ...
§/M{m001}
```

---

## 30. ID CONVENTIONS

### Production IDs (auto-generated)
```
f_01J5X7K9M2NPQRSTABWXYZ12    Function
m_01J5X7K9M2NPQRSTABWXYZ12    Module
c_01J5X7K9M2NPQRSTABWXYZ12    Class
mt_01J5X7K9M2NPQRSTABWXYZ12   Method
```

### Test IDs (tests/, docs/, examples/ only)
```
f001, m001, c001, mt001       Sequential IDs
```

### Rules
1. **NEVER** modify existing IDs
2. **NEVER** copy IDs when extracting code
3. **OMIT** IDs for new declarations, run `calor ids assign`
4. **VERIFY** before commit: `calor ids check`

---

## QUICK REFERENCE

| Task | Syntax |
|------|--------|
| Function | `§F{id:Name:pub} §I{type:name} §O{type} §R expr §/F{id}` |
| Class | `§CL{id:Name:pub} §FLD{...} §MT{...} §/CL{id}` |
| Loop | `§L{id:i:0:10:1} ... §/L{id}` |
| If | `§IF{id} cond → action §EI cond → action §EL → action §/I{id}` |
| Match | `§W{id} target §K pattern → expr §/W{id}` |
| Contract | `§Q condition` (pre), `§S condition` (post) |
| Effect | `§E{cw,fs:r,net:r}` |
| Async | `§AF{id:Name:pub} §O{T} §AWAIT expr §/AF{id}` |
| Lambda | `(x) → expr` or `§LAM{id:x:i32} ... §/LAM{id}` |
| List | `§LIST{name:type} ... §/LIST{name}` then `§PUSH{name} val` |
| HashSet | `§HSET{name:type} §/HSET{name}` then `§ADD{name} val` |
| Option | `§SM value` (Some), `§NN` (None) |
| Result | `§OK value`, `§ERR "msg"` |
