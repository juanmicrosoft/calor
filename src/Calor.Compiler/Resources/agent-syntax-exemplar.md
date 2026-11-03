<!-- Generated from src/Calor.Compiler/Resources/Teaching/reference.json by calor self-check docs --fix. Do not edit. -->

# Calor syntax exemplar (reference sheet)

Calor compiles to C# on .NET 10. Indent bodies two spaces; structural blocks end at dedent.
<!-- drift:ignore -->
Never write structural closers such as `§/M`, `§/F`, `§/IF`, `§/WH` or `§/EACH`.
The current staged headers below keep their existing field order, without source IDs.
Constructors, block lambdas and collection literals retain their documented value delimiters;
`§/DO` carries the do-while condition. Ordinary calls need only parentheses, not call tags.

Every complete `§M{...}` program in the "Complete programs" section below compiles
(the compiler's own tests prove it). The fragments summarize these supported forms.

## Core tags

```
§M{Name}                             Module
§F{Name:pub} (string:x) -> string     Inline function signature (priv = private)
§E{}                                 Pure; cw console write; fs:r/fs:w file I/O;
                                     mut collection/heap mutation; alloc allocation
§B{name:string} expr                 Immutable (type optional: §B{name})
§B{~name:int} 0                      Mutable (~ prefix required to reassign)
§ASSIGN name expr                    All updates, including increments
§Q condition / §S condition          Precondition / postcondition (result = return value)
§R expr                              Return (bare §R for void)
§P expr                              Print line
§IF (cond) / §EI (cond) / §EL         Indented if / else-if / else branches
§WH (cond)                           While
§L{i:0:9:1}                          Inclusive snapshot range, 0 to 9 step 1
§EACH{x} collection                  Foreach (optional item type)
§EACHKV{k:v} dictionary              Key/value foreach
```

Expressions use infix operators with C# precedence: `a + b`, `a == b`, `p && q`,
`!p`, `i < n`, `n % 3`, `value ?? fallback`, `condition ? yes : no`.
Binary operators associate left; `??` and `?:` associate right. Boolean operators
short circuit. Use `!condition || consequence` for implication, not a special operator.
Newlines end expressions. Group infix values in whitespace-separated staged hosts.
Use C# type names, `T[]` arrays, `T?` nullability and literals such as `42`, `true`,
`"hi"`, `18.00m` and `1.5f`. Escapes include `\"`, `\n`, `\r`.
Plain strings interpolate `"Hello ${name}"`; `"\${0}"` keeps literal placeholder text.
Use nullability and exceptions for absence/failure, not dedicated Option/Result forms.

## Calls

```
Obj.Method(arg1, arg2)                     Postfix call; comma-separated arguments
§B{lines:string[]} File.ReadAllLines(path)
File.AppendAllText(path, key + "=" + value + "\n")
§B{ok:bool} File.Exists(path)
§B{n:int} LocalFunc(x)                     User functions use the same call syntax
```

Callees may be expressions; calls, member access and indexing can chain.
Only methods use parentheses; property/field reads use `obj.Property`.
Bind local helpers to typed `§LAM{...} body §/LAM` values, not nested `§F` declarations.

## Strings (staged built-ins)

```
(len s)                 s.Length            (substr s start count)   s.Substring
(contains s sub)        s.Contains          (indexof s sub :ordinal) s.IndexOf
(starts s pre)          s.StartsWith        (replace s old new)      s.Replace
(trim s)  (upper s)  (lower s)  (equals a b)  (ends s suffix)
§B{c:string} (substr line i 1)               One-character string, not a char
```

These named built-ins retain prefix operands during integration; they are not
arithmetic operators or ordinary method names. Do not invent extra arguments.

## Lists and dictionaries

```
§LIST{items:string}                   Empty list; close with §/LIST
§/LIST
§PUSH{items} x                        Append [needs mut]
§B{n:int} §CNT{items}                Count
§B{element:string} items[i]          Index
§DICT{ages:string:int}               Empty dictionary
§/DICT
§PUT{ages} k v                        Upsert ages[k] = v [needs mut]
```

Functions that mutate collections (`§PUSH`, `§PUT`, `§SETIDX`) declare `mut`;
allocation needs `alloc`; file I/O needs `fs:r` / `fs:w` / `fs:rw`.
Callers declare at least the union of their callees' effects. Unknown effects are not purity proofs.
Arrays and lists differ: `File.ReadAllLines` returns `string[]`, not `List<string>`.

## Complete programs (the compiler's tests prove every one compiles)

```calor
§M{Contracts}
  §F{Square:pub} (int:x) -> int
    §Q (x >= 0)
    §S (result >= 0)
    §R (x * x)

  §F{Divide:pub} (int:a, int:b) -> int
    §Q{"divisor must not be zero"} (b != 0)
    §R (a / b)
```

```calor
§M{Branching}
  §F{Sign:pub} (int:x) -> int
    §IF (x > 0)
      §R 1
    §EI (x < 0)
      §R (-1)
    §EL
      §R 0
```

```calor
§M{Files}
  §F{Append:pub} (string:path, string:key, string:value) -> void
    §E{fs:w}
    File.AppendAllText(path, (key + ("=" + (value + "\n"))))

  §F{CountLines:pub} (string:path) -> int
    §E{fs:r}
    §B{ok:bool} File.Exists(path)
    §IF (!ok)
      §R 0
    §B{lines:string[]} File.ReadAllLines(path)
    §R (len lines)
```

```calor
§M{Strings}
  §F{FirstField:pub} (string:line) -> string
    §E{}
    §B{i:int} (indexof line "," :ordinal)
    §IF (i < 0)
      §R line
    §R (substr line 0 i)
```

```calor
§M{Collections}
  §F{Tally:pub} (string:key) -> int
    §E{mut,alloc}
    §DICT{counts:string:int}
    §/DICT
    §PUT{counts} key 1
    §R counts[key]
```

```calor
§M{Effects}
  §F{Greet:pub} (string:name) -> void
    §E{cw}
    Console.WriteLine("Hello")
    Console.WriteLine(name)
```

```calor
§M{Loops}
  §F{PrintOneToFive:pub} () -> void
    §E{cw}
    §L{i:1:5:1}
      Console.WriteLine(i)
```

```calor
§M{Bindings}
  §F{Demo:pub} () -> void
    §E{cw}
    §B{greeting:string} "Ada"
    §B{~count:int} 0
    Console.WriteLine(greeting)
    Console.WriteLine(count)
```

```calor
§M{Classes}
  §CL{Greeter:pub}
    §MT{Greet:pub} () -> string
      §R "hello"
```

## Common mistakes (these do NOT compile — shown so you avoid them)

```
<!-- drift:ignore -->
§/F §/M §/I §/L       removed; use indentation only (a block ends on dedent)
<!-- drift:ignore -->
§S (§RESULT >= 0)     wrong; the return value is lowercase: §S (result >= 0)
§IF                  wrong; §IF needs a condition, such as §IF (x > 0)
§IF{i1}{x > 0}        wrong; the condition goes in (parens), not braces
<!-- drift:ignore -->
§K   §ELSE            no such keywords; else is §EL, else-if is §EI (no id)
§F{f1:Add:int:pub}    wrong; the obsolete ID-bearing header is rejected. Use:
                     §F{Add:pub} (int:a, int:b) -> int
```

Effects: `§E{cw}` declares console write; declare effects on any function that
performs I/O. See the `calor://effects` resource for the full effect catalog.
