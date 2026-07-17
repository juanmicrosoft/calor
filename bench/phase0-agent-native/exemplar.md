# Calor syntax exemplar (reference sheet)

Calor is an indentation-only DSL (2 spaces per level, Python-style) that compiles
to C#. **Never write structural closer tags** (`§/M`, `§/F`, `§/IF`, `§/WH`,
`§/EACH`, ... are hard errors); blocks end by dedent. The only closers ever
written are `§/C` (call arg lists), `§/LAM` (block lambdas), `§/NEW`, and
`§/LIST`/`§/DICT` (empty collection literals).

## Core tags

```
§M{id:Name}                     Module
§F{id:Name:pub} (str:x) -> str  Function, inline signature (pri = private)
§E{}                            Effects: §E{} pure; fs:r, fs:w, fs:rw file I/O;
                                mut collection/heap mutation; cw console; comma-join
§B{name:str} expr               Immutable binding (type annotation optional: §B{name})
§B{~name:i32} 0                 Mutable binding (~ prefix required to reassign)
§ASSIGN name expr               Reassign a mutable binding
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
§B{lines:List<str>  <!-- NOT [str]: the E1a-measured bug; list types emit List<T> -->} §C{File.ReadAllLines} §A path §/C
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
