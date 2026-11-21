<!-- Generated from src/Calor.Compiler/Resources/Teaching/reference.json by calor self-check docs --fix. Do not edit. -->

# Calor syntax exemplar (reference sheet)

Calor compiles to C# on .NET 10. Indent bodies two spaces; structural blocks end at dedent.
Headers are lowercase and space-separated, without source IDs; tooling maintains identity.
Calls use parentheses; lambdas use => with an expression or an indented statement body.

## Core syntax

```
module Name                          Module
func Name (string:x) → string        Public by default; private is explicit
!                                    Pure row; cw console write; fs:r/fs:w file I/O;
                                     mut collection/heap mutation; alloc allocation
let string:name = expression         Immutable (type optional)
var int:name = 0                     Mutable (type optional)
set name expression                  Update; increment with set i i + 1
int:x | (# > 0)                      Precondition: refine the parameter (# is its value)
=> condition                         Postcondition (result = return value)
return expression                    Return (bare return for void)
Console.WriteLine(expression)        Print line [needs cw]
if c / else if c / else              Indented branches
while condition                      Checked before each iteration
for i from 0 to 9 step 1             Inclusive range; bounds evaluated once
for i = 0; i < Bound(); i = i + 1    Condition re-evaluated on every test
each item in collection              Iteration (optional type:item)
each key, value in dictionary        Two-name deconstruction
```

Infix operators use C# precedence: a + b, a == b, p && q, !p, n % 3, value ?? fallback and
condition ? yes : no. && and || short circuit; write implication as !p || q. Newlines end expressions.
Use C# type names, T[] arrays, T? nullability and literals such as 42, true, "hi", 18.00m and 1.5f.
Escapes include \", \n and \r. Strings interpolate "Hello ${name}"; "\${0}" keeps placeholder text.
Use nullability and exceptions for absence and failure, not Option/Result forms.

## Calls

```
Obj.Method(arg1, arg2)                     Comma-separated arguments
let string[]:lines = File.ReadAllLines(path)
File.AppendAllText(path, key + "=" + value + "\n")
let bool:ok = File.Exists(path)
let int:n = LocalFunc(x)                   User functions use the same syntax
let Func<int,int>:twice = (int:x) => x * 2
```

Calls, member access and indexing chain. Properties take no parentheses: obj.Property.
Never write structural closers or brace bodies, including for indented lambdas.

## Strings

```
s.Length                              UTF-16 code-unit count
s.Contains('x')   s.IndexOf('x')        char overloads, not substring search
s.ToUpperInvariant()   s.ToLowerInvariant()
System.String.Equals(a, b)             Ordinal equality
let char:character = line[i]           A UTF-16 code unit, not a string
let string:copy = new string(chars)    char[] buffer [needs alloc]
```

Contains(string), Substring, trimming and comparison-mode searches are unknown under strict
checking; scan ordinally into a buffer as FirstField does. Do not invent overloads.

## Lists and dictionaries

```
let List<string>:items = new List<string>()
items.Add(value)                            Append [needs mut]
let int:n = items.Count
let string:element = items[i]
let Dictionary<string,int>:ages = new Dictionary<string,int>()
set ages[key] value                         Upsert, not Add [needs mut]
```

Mutation needs mut, allocation alloc, file I/O fs:r, fs:w or fs:rw. A caller declares at least
its callees' effects; unknown effects prove nothing, and casts or fake types never earn a charge.
File.ReadAllLines returns string[], not List<string>.

## Complete programs (the compiler's tests prove every one compiles)

```calor
module Contracts
  func Square (int:x | (# >= 0)) → int
    !
    => result >= 0
    return x * x

  func Divide (int:a, int:b | (# != 0)) → int
    !
    return a / b
```

```calor
module Branching
  func Sign (int:x) → int
    !
    if (x > 0)
      return 1
    else if (x < 0)
      return (-1)
    else
      return 0
```

```calor
module Files
  func Append (string:path, string:key, string:value) → void
    ! fs:w
    File.AppendAllText(path, (key + ("=" + (value + "\n"))))

  func CountLines (string:path) → int
    ! fs:r
    let bool:ok = File.Exists(path)
    if (!ok)
      return 0
    let string[]:lines = File.ReadAllLines(path)
    return lines.Length
```

```calor
module Strings
  func FirstField (string:line) → string
    ! alloc, mut
    var int:end = 0
    while end < line.Length && line[end] != ','
      set end end + 1
    if end == line.Length
      return line
    let char[]:prefix = new char[end]
    var int:i = 0
    while i < end
      set prefix[i] line[i]
      set i i + 1
    return new string(prefix)
```

```calor
module Collections
  func Tally (string:key) → int
    ! mut,alloc
    let Dictionary<string,int>:counts = new Dictionary<string,int>()
    set counts[key] 1
    return counts[key]
```

```calor
module Effects
  func Greet (string:name) → void
    ! cw
    Console.WriteLine("Hello")
    Console.WriteLine(name)
```

```calor
module Loops
  func PrintOneToFive () → void
    ! cw
    for i from 1 to 5 step 1
      Console.WriteLine(i)
```

```calor
module Bindings
  func Demo () → void
    ! cw
    let string:greeting = "Ada"
    var int:count = 0
    Console.WriteLine(greeting)
    Console.WriteLine(count)
```

```calor
module Classes
  class Greeter
    func Greet () → string
      !
      return "hello"
```

## Common mistakes (these do NOT compile — shown so you avoid them)

```
=> RESULT >= 0       wrong; the return value is lowercase: => result >= 0
if                   wrong; if needs a condition, such as if x > 0
if {x > 0}           wrong; braces do not surround conditions
```

Closing tags and section headers are retired; write `else` or `else if` for a branch.
Declaration IDs live in `.calor/ids.json`, never in a header: `func Add (int:a, int:b) → int`.
Functions that perform I/O declare effects (`! cw` is console write); see `calor://effects`.
