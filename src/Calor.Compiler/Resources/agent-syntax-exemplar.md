<!-- Generated from src/Calor.Compiler/Resources/Teaching/reference.json by calor self-check docs --fix. Do not edit. -->

# Calor syntax exemplar (reference sheet)

Calor compiles to C# on .NET 10. Indent bodies two spaces; structural blocks end at dedent.
Declarations use lowercase, space-separated headers without source IDs. Tooling maintains identity.
Ordinary calls use parentheses; lambdas use => with an expression or indented statement body.

Every complete module in the "Complete programs" section below must compile under the same
strict policy used by calor_compile. The fragments summarize supported forms; they are not programs.

## Core syntax

```
module Name                          Module
func Name (string:x) → string        Public by default; private is explicit
!                                    Pure row; cw console write; fs:r/fs:w file I/O;
                                     mut collection/heap mutation; alloc allocation
let string:name = expression         Immutable (type optional)
var int:name = 0                     Mutable (type optional)
set name expression                 Updates, including increments expressed as addition
int:x | (# > 0)                      Precondition: refine the parameter (# is its value)
=> condition                         Postcondition (result = body return value)
return expression                   Return (bare return for void)
Console.WriteLine(expression)        Print line [needs cw]
if condition / else if condition / else
                                     Indented branches
while condition                     Condition checked before each iteration
for i from 0 to 9 step 1             Inclusive snapshot range; bounds evaluated once
for i = 0; i < Bound(); i = i + 1    Condition reevaluated on every test
each item in collection             Collection iteration (optional type:item)
each key, value in dictionary        Synchronous two-name deconstruction
```

Expressions use infix operators with C# precedence: a + b, a == b, p && q,
!p, i < n, n % 3, value ?? fallback and condition ? yes : no.
Binary operators associate left; ?? and ?: associate right. Boolean operators short circuit.
Use !condition || consequence for implication, not a special operator. Newlines end expressions.
Use C# type names, T[] arrays, T? nullability and literals such as 42, true, "hi", 18.00m and 1.5f.
Escapes include \", \n and \r. Plain strings interpolate "Hello ${name}";
"\${0}" keeps literal placeholder text. Use nullability and exceptions for absence/failure,
not dedicated Option/Result forms.

## Calls

```
Obj.Method(arg1, arg2)                     Postfix call; comma-separated arguments
let string[]:lines = File.ReadAllLines(path)
File.AppendAllText(path, key + "=" + value + "\n")
let bool:ok = File.Exists(path)
let int:n = LocalFunc(x)                   User functions use the same call syntax
let Func<int,int>:twice = (int:x) => x * 2
```

Callees may be expressions; calls, member access and indexing can chain.
Only methods use parentheses; property/field reads use obj.Property.
Do not write structural closers or brace bodies for indented lambdas.

## Strings

```
s.Length                              UTF-16 code-unit count
s.Contains('x')   s.IndexOf('x')        Contracted char overloads, not substring search
s.ToUpperInvariant()   s.ToLowerInvariant()
System.String.Equals(a, b)             Contracted ordinal equality
let char:character = line[i]           A UTF-16 code unit, not a string
let string:copy = new string(chars)    char[] buffer [needs alloc]
```

Contracts apply to the selected overload. Contains(string), Substring, trimming and
comparison-mode searches currently remain unknown under strict checking. The complete
FirstField program uses an ordinal scan and a buffer, declaring alloc and mut honestly.
Do not invent overloads or replace ordinal comparison with culture-dependent behavior.

## Lists and dictionaries

```
let List<string>:items = new List<string>()
items.Add(value)                            Append [needs mut]
let int:n = items.Count
let string:element = items[i]
let Dictionary<string,int>:ages = new Dictionary<string,int>()
set ages[key] value                         Upsert, not Add [needs mut]
```

Collection mutation needs mut; allocation needs alloc; file I/O needs fs:r, fs:w or fs:rw.
Callers declare at least the union of their callees' effects. Unknown effects are not purity proofs.
Source-generic receiver metadata must resolve honestly; do not add casts or fake types to obtain
an effect charge. Arrays and lists differ: File.ReadAllLines returns string[], not List<string>.

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

Blocks end at dedent; structural closing tags and section headers are retired.
Write `else` or `else if` for a branch. Declaration IDs belong in the project
`.calor/ids.json` sidecar, never in the source signature. Use an ordinary header:
`func Add (int:a, int:b) → int`.

Effects: `! cw` declares console write. Declare effects on functions that perform I/O.
See `calor://effects` for the full effect catalog.
