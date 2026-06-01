---
layout: default
title: Syntax Reference
nav_order: 4
has_children: true
permalink: /syntax-reference/
---

# Syntax Reference

Complete reference for Calor syntax. Calor uses Lisp-style expressions for all operations.

---

## Quick Reference Table

| Element | Syntax | Example |
|:--------|:-------|:--------|
| Module | `§M{name}` | `§M{Calculator}` |
| Function | `§F{name:visibility}` | `§F{Add:pub}` |
| Async Function | `§AF{name:visibility}` | `§AF{FetchAsync:pub}` |
| Method | `§MT{name:visibility}` | `§MT{Process:pub}` |
| Async Method | `§AMT{name:visibility}` | `§AMT{ProcessAsync:pub}` |
| Await | `§AWAIT expr` | `§AWAIT §C{GetAsync} §/C` |
| Lambda (inline) | `(params) → expr` | `(x) → (* x 2)` |
| Lambda (block) | `§LAM{params}` (indented body) | `§LAM{x:i32}` |
| Delegate | `§DEL{name}` (indented signature) | `§DEL{Handler}` |
| Event | `§EVT{name:vis:type}` | `§EVT{Click:pub:EventHandler}` |
| Subscribe | `§SUB event handler` | `§SUB btn.Click OnClick` |
| Unsubscribe | `§UNSUB event handler` | `§UNSUB btn.Click OnClick` |
| Input | `§I{type:name}` | `§I{i32:x}` |
| Output | `§O{type}` | `§O{i32}` |
| Effects | `§E{codes}` | `§E{cw,fs:r,net:rw}` |
| Requires | `§Q expr` | `§Q (>= x 0)` |
| Ensures | `§S expr` | `§S (>= result 0)` |
| For Loop | `§L{var:from:to:step}` | `§L{i:1:100:1}` |
| While Loop | `§WH condition` | `§WH (> i 0)` |
| Do-While Loop | `§DO` (body indented) | `§DO` then `§WHILE cond` |
| If/ElseIf/Else | `§IF cond` then `§EI` / `§EL` at same column | `§IF (> x 0)` |
| Call | `§C{target}...§/C` | `§C{Math.Max} §A 1 §A 2 §/C` |
| C# Attribute | `[@Name]` or `[@Name(args)]` | `[@HttpPost]`, `[@Route("api")]` |
| Print | `§P expr` | `§P "Hello"` |
| Return | `§R expr` | `§R (+ a b)` |
| Binding | `§B{name} expr` | `§B{x} (+ 1 2)` |
| Operations | `(op args...)` | `(+ a b)`, `(== x 0)` |
| Block end | _dedent_ (Python-style) | _(no `§/X` needed)_ |
| List | `§LIST{id:type}` | `§LIST{nums:i32}` |
| Dictionary | `§DICT{id:kType:vType}` | `§DICT{ages:str:i32}` |
| HashSet | `§HSET{id:type}` | `§HSET{tags:str}` |
| Key-Value | `§KV key value` | `§KV "alice" 30` |
| Push | `§PUSH{coll} value` | `§PUSH{nums} 5` |
| Put | `§PUT{dict} key value` | `§PUT{ages} "bob" 25` |
| Set Index | `§SETIDX{list} idx val` | `§SETIDX{nums} 0 10` |
| Contains | `§HAS{coll} value` | `§HAS{nums} 5` |
| Count | `§CNT{coll}` | `§CNT{nums}` |
| Dict Foreach | `§EACHKV{id:k:v} dict` | `§EACHKV{e1:k:v} ages` |
| Enum | `§EN{id:name}` | `§EN{e001:Color}` |
| Enum Extension | `§EXT{id:enumName}` | `§EXT{ext001:Color}` |
| Switch | `§W{id} expr` | `§W{sw1} score` |
| Case | `§K pattern → result` | `§K 200 → "OK"` |
| Wildcard | `§K _` | `§K _ → "default"` |
| Relational | `§PREL{op} value` | `§PREL{gte} 90` |
| Var Pattern | `§VAR{name}` | `§VAR{n}` |
| Guard | `§WHEN condition` | `§WHEN (> n 0)` |
| Refinement Type | `§RTYPE{id:Name:base} (pred)` | `§RTYPE{r1:NatInt:i32} (>= # INT:0)` |
| Indexed Type | `§ITYPE{id:Name:base:size}` | `§ITYPE{it1:SizedList:List:n}` |
| Proof Obligation | `§PROOF{id:desc} (expr)` | `§PROOF{p1:check} (>= x INT:0)` |

---

## Types

| Type | Description | C# Equivalent |
|:-----|:------------|:--------------|
| `i32` | 32-bit integer | `int` |
| `i64` | 64-bit integer | `long` |
| `f32` | 32-bit float | `float` |
| `f64` | 64-bit float | `double` |
| `str` | String | `string` |
| `bool` | Boolean | `bool` |
| `void` | No return value | `void` |
| `?T` | Optional T | `T?` (nullable) |
| `T!E` | Result (T or error E) | `Result<T, E>` |

---

## Operators

| Category | Operators |
|:---------|:----------|
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Comparison | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Logical | `&&`, `\|\|`, `!` |
| String | `len`, `upper`, `lower`, `trim`, `contains`, `starts`, `ends`, `indexof`, `substr`, `replace`, `concat`, `equals` |
| Char | `char-at`, `char-code`, `char-from-code`, `is-letter`, `is-digit`, `is-whitespace`, `is-upper`, `is-lower`, `char-upper`, `char-lower` |
| Regex | `regex-test`, `regex-match`, `regex-replace`, `regex-split` |
| StringBuilder | `sb-new`, `sb-append`, `sb-appendline`, `sb-insert`, `sb-remove`, `sb-clear`, `sb-tostring`, `sb-length` |

All operators use Lisp-style prefix notation: `(+ a b)`, `(&& x y)`, `(upper s)`

---

## Effect Codes

| Code | Effect | Description |
|:-----|:-------|:------------|
| `cw` | Console write | `Console.WriteLine` |
| `cr` | Console read | `Console.ReadLine` |
| `fs:w` | File write | File system writes |
| `fs:r` | File read | File system reads |
| `net:rw` | Network | HTTP, sockets, etc. |
| `db:rw` | Database | Database operations |

---

## ID Conventions

Stable IDs are **optional** on structural openers and are auto-assigned
by the parser when omitted. Provide an explicit ID only when you want
to reference the element from external tooling (e.g. `calor navigate`).

| Element | Auto-assigned form | Explicit form |
|:--------|:-------------------|:--------------|
| Modules | `§M{Calculator}` | `§M{m001:Calculator}` |
| Functions | `§F{Add:pub}` | `§F{f001:Add:pub}` |
| Loops | `§L{i:1:10:1}` | `§L{for1:i:1:10:1}` |
| Conditionals | `§IF cond` | `§IF{if1} cond` |

---

## Complete Example

```
§M{FizzBuzz}
  §F{Main:pub}
    §O{void}
    §E{cw}
    §L{i:1:100:1}
      §IF (== (% i 15) 0) → §P "FizzBuzz"
      §EI (== (% i 3) 0) → §P "Fizz"
      §EI (== (% i 5) 0) → §P "Buzz"
      §EL → §P i
```

Blocks are delimited by **indentation** (default 2 spaces per nesting
level). Legacy `§/X` closers are still accepted for transition
compatibility but should not be used in new code — see
[Structure Tags](/calor/syntax-reference/structure-tags/) for details.

---

## Detailed Reference

- [Structure Tags](/calor/syntax-reference/structure-tags/) - Modules, functions, block structure
- [Types](/calor/syntax-reference/types/) - Type system, Option, Result
- [Expressions](/calor/syntax-reference/expressions/) - Lisp-style operators
- [Control Flow](/calor/syntax-reference/control-flow/) - Loops, conditionals
- [Contracts](/calor/syntax-reference/contracts/) - Requires, ensures
- [Effects](/calor/syntax-reference/effects/) - Effect declarations
- [String Operations](/calor/syntax-reference/string-operations/) - String, char, regex, StringBuilder operations
- [Refinement Types](/calor/syntax-reference/refinement-types/) - Refinement types, indexed types, proof obligations
