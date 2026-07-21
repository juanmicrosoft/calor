# Calor syntax exemplar (reference sheet)

Calor is an indentation-only DSL (2 spaces per level, Python-style) that compiles
<!-- drift:ignore -->
to C#. **Never write structural closer tags** (`§/M`, `§/F`, `§/IF`, `§/WH`,
`§/EACH`, ... are hard errors); blocks end by dedent. The only closers ever
written are `§/C` (call arg lists), `§/LAM` (block lambdas), `§/NEW`, and
`§/LIST`/`§/DICT` (empty collection literals).

Every complete `§M{...}` program in the "Complete programs" section below compiles
(the compiler's own tests prove it); the reference blocks show the shape of each
construct, and every idiom in them is exercised by one of those programs.

## Core tags

```
§M{id:Name}                     Module
§F{id:Name:pub} (str:x) -> str  Function, inline signature (pri = private)
§E{}                            Effects: §E{} pure; fs:r, fs:w, fs:rw file I/O;
                                mut collection/heap mutation; cw console; comma-join
§B{name:str} expr               Immutable binding (type annotation optional: §B{name})
§B{~name:i32} 0                 Mutable binding (~ prefix required to reassign)
§ASSIGN name expr               Reassign a mutable binding
§Q (pre)  §S (post)             Precondition / postcondition (post can use `result`)
§R expr                         Return (bare §R for void)
§P expr                         Print line
§IF{id} (cond) / §EI (cond) / §EL   If / else-if / else (§EI/§EL at §IF's column)
§WH{id} (cond)                  While loop
§L{id:i:0:9:1}                  For loop, i from 0 to 9 step 1 (inclusive)
§EACH{id:x} coll                Foreach over list/array
§EACHKV{id:k:v} dict            Foreach over dictionary
```

Expressions are prefix s-expressions: `(+ a b)`, `(== a b)`, `(&& p q)`, `(! p)`,
`(< i n)`, `(% n 3)`. String `+` concatenates. Typed literals when needed:
`INT:42`, `STR:"hi"`, `BOOL:true`. Escapes in strings: `\"`, `\n`, `\r`.

## Calls

```
§C{Obj.Method} §A arg1 §A arg2 §/C     each arg prefixed §A, closed with §/C
§B{lines:[str]} §C{File.ReadAllLines} §A path §/C
§C{File.AppendAllText} §A path §A (+ key (+ "=" (+ value "\n"))) §/C
§B{ok:bool} §C{File.Exists} §A path §/C
§B{n:i32} §C{LocalFunc} §A x §/C       user functions are called the same way
```

## Strings (prefix ops, not method calls)

```
(len s)                 s.Length            (substr s start count)   s.Substring
(contains s sub)        s.Contains          (indexof s sub :ordinal) s.IndexOf
(starts s pre)          s.StartsWith        (replace s old new)      s.Replace
(trim s)  (upper s)  (lower s)  (equals a b)  (ends s suf)
§B{c:str} (substr line i 1)      // single char at i, as a str
```

## Lists and dictionaries

```
§LIST{items:str}          empty list literal — close the empty form with §/LIST
§/LIST{items}
§PUSH{items} x            items.Add(x)         [needs mut effect]
§B{n:i32} §CNT{items}     items.Count
§B{e:str} §IDX items i    items[i]
§IF{if1} (! §HAS{items} k)   Contains check (dict key: §HAS{d} §KEY k)
§DICT{ages:str:i32}       empty dict literal
§/DICT{ages}
§PUT{ages} k v            ages[k] = v          [needs mut effect]
```

A function whose body mutates a collection (`§PUSH`, `§PUT`, `§SETIDX`) must
declare `mut` in `§E{...}`; file reads/writes need `fs:r` / `fs:w` / `fs:rw`.
Callers must declare (at least) the union of their callees' effects.

**Arrays vs lists (common trap):** `File.ReadAllLines` returns an ARRAY — bind it
as `[str]`. Do NOT copy `[str]` into signatures that require `List<str>`; match
the type the surrounding surface declares.

## Complete programs (the compiler's tests prove every one compiles)

```
§M{m1:Contracts}
  §F{f1:Square:pub} (i32:x) -> i32
    §Q (>= x 0)
    §S (>= result 0)
    §R (* x x)

  §F{f2:Divide:pub} (i32:a, i32:b) -> i32
    §Q{"divisor must not be zero"} (!= b 0)
    §R (/ a b)
```

```
§M{m2:Branching}
  §F{f1:Sign:pub} (i32:x) -> i32
    §IF{if1} (> x 0)
      §R 1
    §EI (< x 0)
      §R (- 0 1)
    §EL
      §R 0
```

```
§M{m3:Files}
  §F{f1:Append:pub} (str:path, str:key, str:value) -> void
    §E{fs:w}
    §C{File.AppendAllText} §A path §A (+ key (+ "=" (+ value "\n"))) §/C

  §F{f2:CountLines:pub} (str:path) -> i32
    §E{fs:r}
    §B{ok:bool} §C{File.Exists} §A path §/C
    §IF{if1} (! ok)
      §R 0
    §B{lines:[str]} §C{File.ReadAllLines} §A path §/C
    §R (len lines)
```

```
§M{m4:Strings}
  §F{f1:FirstField:pub} (str:line) -> str
    §E{}
    §B{i:i32} (indexof line "," :ordinal)
    §IF{if1} (< i 0)
      §R line
    §R (substr line 0 i)
```

```
§M{m5:Collections}
  §F{f1:Tally:pub} (str:key) -> i32
    §E{mut}
    §DICT{counts:str:i32}
    §/DICT{counts}
    §PUT{counts} key 1
    §R §IDX counts key
```

```
§M{m6:Effects}
  §F{f1:Greet:pub} (str:name) -> void
    §E{cw}
    §C{Console.WriteLine} §A "Hello" §/C
    §C{Console.WriteLine} §A name §/C
```

```
§M{m7:Loops}
  §F{f1:PrintOneToFive:pub} () -> void
    §E{cw}
    §L{l1:i:1:5:1}
      §C{Console.WriteLine} §A i §/C
```

```
§M{m8:Bindings}
  §F{f1:Demo:pub} () -> void
    §E{cw}
    §B{greeting:str} "Ada"
    §B{~count:i32} 0
    §C{Console.WriteLine} §A greeting §/C
    §C{Console.WriteLine} §A count §/C
```

```
§M{m9:Classes}
  §CL{c1:Greeter:pub}
    §MT{mt1:Greet:pub} () -> str
      §R "hello"
```

## Common mistakes (these do NOT compile — shown so you avoid them)

```
<!-- drift:ignore -->
§/F §/M §/I §/L       removed; use indentation only (a block ends on dedent)
<!-- drift:ignore -->
§S (>= §RESULT 0)     wrong; the return value is lowercase: §S (>= result 0)
§IF (> x 0)           wrong; §IF needs an id: §IF{if1} (> x 0)
§IF{i1}{x > 0}        wrong; the condition goes in (parens), not braces
<!-- drift:ignore -->
§K   §ELSE            no such keywords; else is §EL, else-if is §EI (no id)
§F{f1:Add:i32:pub}    wrong; a 4-field header drops the return type. Use:
                     §F{f1:Add:pub} (i32:a, i32:b) -> i32
```

Effects: `§E{cw}` declares console write; declare effects on any function that
performs I/O. See the `calor://effects` resource for the full effect catalog.
