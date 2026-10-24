---
layout: default
title: Bindings
parent: Syntax Reference
nav_order: 3
---

# Bindings

The `§B` tag introduces a local binding (Calor's equivalent of a
C# `var` declaration). The binder accepts four forms; in three of them
the **type is inferred** from the initializer expression.

> **Reference for RFC** `v0.6-bind-inference-formalization`. The
> behavior on this page is what the binder does today and is pinned
> by `tests/Calor.Semantics.Tests/BindInferenceDocsTests.cs`.

---

## Syntax

```
§B{name}                    // (1) requires an initializer (see Rules)
§B{name} initializer        // (2) immutable, type inferred from initializer
§B{name:type}               // (3) immutable, declared type, no initializer
§B{name:type} initializer   // (4) immutable, declared type wins

§B{~name} initializer       // (2') mutable variant of (2)
§B{~name:type}              // (3') mutable variant of (3)
§B{~name:type} initializer  // (4') mutable variant of (4)
```

The `~` prefix marks the binding as **mutable**; without `~` the
binding is immutable (cannot be reassigned).

---

## Rules

1. **Either a `:type` annotation or an initializer is required.**
   `§B{x}` with neither is a hard error (`Calor0250`,
   [diagnostics](#diagnostics) below).
2. **An explicit `:type` wins over inference.** Form (4) does not
   re-infer; the declared type is used verbatim. The binder itself
   does not verify the initializer matches the declared type — that
   check happens downstream in the C# compile (or the analyzer if
   `--analyze` is on), so a mismatched pair like `§B{x:str} INT:42`
   compiles to invalid C#.
3. **Without `:type`, the bound type is `initializer.TypeName`.**
   This is "shallow" inference: the binder reads the type the
   initializer's bound node reports and uses it directly. There is no
   bidirectional flow, no widening, no constraint solving.
4. **The reported type is the binder's internal name.** Literal
   initializers produce `INT`, `STRING`, `BOOL`, `FLOAT`. Calling code
   that needs to write the equivalent type annotation uses the
   user-facing names instead — see [Types](/calor/syntax-reference/types/)
   for the mapping (`i32`, `str`, `bool`, `f64`, …).
5. **Inferred bindings emit `var` in C#; explicit bindings emit the
   named type.** Writing `§B{x} INT:0` produces `var x = 0;`; writing
   `§B{x:i32} INT:0` produces `int x = 0;`. Both compile to the same
   IL, but the source-level distinction is preserved end-to-end.
6. **A bare binding takes a next-line expression as its initializer.**
   A binding with nothing after its header on its line, such as
   `§B{~n:i32}`, reads an expression that starts on the next line as
   its initializer, even at the same indent, where it looks like a
   separate statement. Changing this would change the meaning of valid
   programs, so the reading stays and the compiler warns with
   `Calor0121` when the expression starts at the binding's own indent.
   Put the initializer on the `§B` line, or write an explicit one such
   as `` `default` `` when the next line is a separate statement. The
   C# converter always writes an explicit initializer.

---

## Inference table

For each initializer shape on the left, the resulting bound type when
no `:type` annotation is present is on the right.

| Initializer | Bound type | Notes |
|:------------|:-----------|:------|
| `INT:42` (or `42`) | `INT` | `BoundIntLiteral`. Values outside the 32-bit range are bound as `LONG`. |
| `STR:"hello"` | `STRING` | `BoundStringLiteral` |
| `BOOL:true` / `BOOL:false` | `BOOL` | `BoundBoolLiteral` |
| `FLOAT:3.14` | `FLOAT` | `BoundFloatLiteral` |
| `(+ a b)` etc. | result type of the binary op | `BoundBinaryExpression.TypeName` — type of the result, not the operands |
| an identifier `x` | declared type of `x` | through `BoundVariableExpression` |
| `§C{Foo.bar} … §/C` | declared return type of `Foo.bar` | the binder uses the call's bound return type |
| `none` | **inference fails** (today: silent fallback) | `Calor0251` proposed for future strict mode |

The literal-type names on the right (`INT`, `STRING`, …) are the
binder's internal type identifiers. They appear in tool output and
diagnostics. The corresponding [type annotation](/calor/syntax-reference/types/)
you would write in source is the lowercase user-facing name
(`i32`, `str`, `bool`, `f64`).

---

## Examples

### Inferred, immutable

```
§B{score} INT:85               // score: i32 = 85
§B{name} STR:"Calor"           // name: str = "Calor"
§B{active} BOOL:true           // active: bool = true
§B{pi} FLOAT:3.14              // pi: f64 = 3.14
```

### Inferred, mutable

```
§B{~counter} INT:0             // mutable, starts at 0
§B{~total} INT:0
§L{i:1:10:1}
  §SET counter (+ counter INT:1)
  §SET total   (+ total i)
```

### Explicit type, no initializer

```
§B{retries:i32}                // declared but uninitialized — initialise before use
§B{user:str}
```

These forms are typically used where a value is assigned in every
branch of a subsequent `§IF` / `§W`, or where the binding is for a
field initialized in a constructor body.

### Explicit type with initializer

When you want the declared type to **drive** the conversion of the
initializer (rather than be inferred from it), give an annotation:

```
§B{count:i64} INT:42           // i64 binding initialised from an i32 literal
§B{x:f64}     INT:0            // f64 binding initialised from an integer literal
```

### Inferred from a call expression

```
§B{user} §C{repo.FindUser} §A id §/C
// user: User (declared return type of repo.FindUser)
```

### Inferred from a binary op

```
§B{sum} (+ a b)                // sum: result type of (+ a b)
§B{half} (/ x FLOAT:2.0)       // half: f64
```

---

## Diagnostics

### Calor0250 — `BindRequiresTypeOrInitializer`

A `§B{name}` declaration must carry **either** a `:type` annotation
**or** an initializer expression. With neither, the binder cannot
choose a type for the new symbol.

```
§F{f001:Foo:pub}
  §O{i32}
  §B{x}            // Calor0250: §B{x} requires :type or initializer
  §R INT:0
```

Fix by adding one or the other:

```
§B{x:i32}          // explicit type
§B{x} INT:0        // initializer (inferred)
```

**Pre-v0.6 behavior:** the binder silently defaulted to `INT` in this
case, producing wrong-typed code with no diagnostic. The diagnostic
was added in v0.6 and is enforced through the default `calor` compile command
pipeline by `BindValidationPass`.

### Strict-mode diagnostics (default-on since v0.6.3)

The following diagnostics are reserved in the `Calor0250-0259` range
and are enforced **by default** as of v0.6.3 (RFC §6). To opt out
during migration, pass `--no-strict-bind-inference` to `calor`
or set `CompilationOptions.StrictBindInference = false`.

| Code | Title | Fires on |
|:-----|:------|:---------|
| `Calor0251` | `BindCannotInferNullLiteral` | `§B{x} §NN` (untyped None) or `§B{x} null` |
| `Calor0252` | `BindCannotInferGenericReturn` | `§B{x} §C{Vec.empty} §/C` and other well-known generic factory targets (`Vec.empty`, `List.empty`, `Array.empty`, `Set.empty`, `Map.empty`, …) |
| `Calor0253` | `BindAmbiguousNumeric` | `§B{x} (+ INT:0 FLOAT:0.0)` — a binary op mixing integer and floating-point literal operands |

Each fires only when the binding lacks an explicit `:type` annotation;
adding the annotation always silences the diagnostic.

```
§B{x:Option<i32>} §NN              // silences Calor0251
§B{x:Vec<i32>} §C{Vec.empty} §/C   // silences Calor0252
§B{x:f64} (+ INT:0 FLOAT:0.0)      // silences Calor0253
```

### Calor0254 — `BindArrayToConcreteCollection`

Unlike the strict-inference trio above, this is an **always-on hard type
error** (not gated by `--no-strict-bind-inference`) and fires *because of*
an explicit type: a concrete generic collection (`List<T>`, `HashSet<T>`,
`Queue<T>`, `Stack<T>`, …) is given an **array** value. In C# an array
satisfies the collection *interfaces* (`IList<T>`, `IEnumerable<T>`, …) but is
not implicitly convertible to a concrete collection class, so the emitted code
would fail with `CS0029`. This is the E1a array-vs-list trap caught at compile
time.

It fires in three positions — binding, return, and reassignment:

```
§B{lines:List<str>} §C{File.ReadAllLines} §A path §/C   // Calor0254 (binding)

§F{f:Get:pub} (str:path) -> List<str>                   // Calor0254 (return)
  §R §C{File.ReadAllLines} §A path §/C

§B{~items:List<str>}                                     // Calor0254 (reassign)
§ASSIGN items §C{File.ReadAllLines} §A path §/C

§B{lines:[str]} §C{File.ReadAllLines} §A path §/C        // ok — array form
§B{lines:IEnumerable<str>} §C{File.ReadAllLines} §A path §/C  // ok — interface
```

It also fires in a fourth position — **argument**: an array passed where a user
function/method declares a concrete-collection parameter (matched positionally,
by call arity so overloads resolve):

```
§F{f:Take:pub} (List<str>:items) -> i32
  §R (len items)
§C{Take} §A §C{File.ReadAllLines} §A path §/C §/C   // Calor0254 at the argument
```

The reassignment target may be a local, a parameter, or a class field — all
declared types the check can see. The array is recognized when the value calls a
known array-returning BCL method (`File.ReadAllLines`/`ReadAllBytes`,
`Directory.GetFiles`/`GetDirectories`/`GetFileSystemEntries`) or a user function
declared `-> [T]`.

Scope notes: the check runs inside every block body — loop bodies (including
`§EACH`/`§EACHKV`), `§IF` branches, and while, match, try, using, sync, and
unsafe/fixed blocks. Argument checking resolves only free functions and methods
declared in the same module; an unqualified call is resolved context-sensitively
(a call inside class `C` prefers `C`'s member, then a module-level free function),
so a method and a same-named free function do not collide. Callees it cannot see
are conservative false negatives — BCL and cross-module functions (no signature
registry), and constructors (`§NEW`), operator overloads, and indexers (their
parameters are not registered). It does **not** descend into block-lambda
(`§LAM`) bodies, so a lambda declared `-> List<T>` returning an array is not
checked.

### Calor0213 — `WriteToReadOnlyLocal`

A hard error for any write to a local that Calor declares read-only: an
immutable `§B` binding (no `~`) or a `§L` loop variable. Every write form is
refused: `§ASSIGN`, compound assignment, `(inc x)`/`(dec x)`, a `§A{ref}` or
`§A{out}` argument, and a mutable rebind (`§B{~i}`) of a loop variable. Before
0.27 these compiled silently, because the generated C# local is not `readonly`.

```calor
§B{x:i32} 1
§ASSIGN x 2          // Calor0213: 'x' is an immutable binding

§L{l1:i:0:9:1}
  §ASSIGN i 5        // Calor0213: 'i' is a §L loop variable
```

Declare the binding mutable with `~` (`§B{~x:i32} 1`). For a loop whose
counter the body changes, use a `§WH` loop over a mutable binding. Statement
forms that declare a collection (`§LIST`, `§ARR`, `§DICT`, `§HSET`) have no
mutability marker and stay assignable.

### Bindings inside `§PP`

A statement-level `§PP{COND}` block (C# `#if`) is bound like any other code:
both branches are checked, including every branch of an `#elif` chain
(a `§PP` nested in `§PPE`). Each branch is checked on its own, so one branch
never sees another branch's bindings.

`#if` opens no C# scope. The emitter writes a branch's statements straight
into the enclosing block, so a binding declared at the top of a branch is
still visible after the block, as it is in C#. When two branches declare
the same name, a later write is checked against the strictest declaration:
if any branch declares the name immutable, it stays immutable afterwards.

```calor
§PP{DEBUG}
  §B{x:i32} 1
§PPE
  §B{~x:i32} 2
§ASSIGN x 3          // Calor0213: 'x' is immutable in the DEBUG branch
§B{~x} 4             // Calor0260: same reason
```

A branch binding that reuses a name already declared in the enclosing block
is `Calor0258`, because C# rejects it (CS0128) whenever that branch is active.
Branches that declare the same name must give it the same type; otherwise it
is `Calor0201`, because the code after the block sees one variable. A branch
binding that hides a field, parameter or outer local must be declared in every
branch (a `§PP` without `§PPE` has an empty second branch); otherwise it is
`Calor0201`, because after the block the name would mean the outer variable
whenever the other branch is active. A branch kept as verbatim C# (`§RAW`)
counts as declaring it.
Before 0.27 the bodies were only scanned, so a write to a `§PP` binding that
shared a field's name, a write after a nested `§PP`, and a `§B{~x}` after a
`§PP` that declared an immutable `x` all compiled.

### Calor0123 — `TypeFirstBindingHeader`

A two-field header `§B{a:b}` is read **name-first** unless `a` is a
recognizable type spelling (a built-in such as `i32`, or a PascalCase name
such as `Widget`) and `b` is not. A one-letter or all-caps type does not
qualify, so `§B{W:t}` used to declare a variable `W` of type `t`, silently
swapping the two. When the first field looks like a type (an upper-case
name, a keyword type such as `long`, or an array, pointer or nullable form
such as `T[]` or `i32?`) and the second like a variable (a lower-case
identifier that is not a type), the parser now reports `Calor0123`. A
third field other than `const` or `mut` (for example the legacy
`§B{id:name:type}` spelling) is `Calor0123` too, because it used to be
ignored:

```calor
§B{W:t} §NEW{W}      // Calor0123: write §B{t:W}
§B{t:W} §NEW{W}      // OK: name first
```

The C# converter writes the spelling the parser reads back unchanged.

### Calor0255 — `BindShadowsEnclosingScope`

Another always-on hard error: a `§B` that declares a **new** local reusing the
name of a local, parameter, or **loop variable** already in an **enclosing**
scope. C# forbids a nested local from shadowing an enclosing local/parameter
(CS0136), so the emitted code would not compile.

```
§B{~x:i32} 0
§IF{i1} (> x 0)
  §B{x:str} "hi"   // Calor0255: inner local 'x' shadows the outer 'x'

§L{l1:i:0:9:1}
  §B{i:i32} 5      // Calor0255: shadows the loop variable 'i'
```

Two things are deliberately **not** flagged, matching C#:

- **A mutable rebind is reassignment, not shadowing.** `§B{~x}` reusing a name
  already bound in the function emits `x = …` (not a new declaration) — this is
  the accumulator idiom (`§B{~result} (* result i)` inside a loop), and it is
  fine.
- **A local may shadow a field.** Only enclosing locals/parameters count; a local
  named like a class field is legal (the local wins), as in C#.

Sibling (non-nested) blocks may each reuse a name — they are separate scopes.

Not yet covered (each an exit-0-then-broken-`dotnet build` gap tracked by an
issue, pinned by `ShadowingDifferentialTests`): same-scope duplicate `§B`
(CS0128, #731, needs a converter change), a mutable rebind across sibling blocks
(CS0103, #732, an emitter scoping bug), and a type-changing mutable rebind
(CS0029, #733).

LSP quick-fixes that insert the recommended annotation are available
in v0.6.3 and surface in any IDE talking to the Calor language server.
Each diagnostic carries a `SuggestedFix` that inserts the default
annotation right before the closing `}` of the bind's attribute block:

| Code | Inserted annotation | Notes |
|:-----|:--------------------|:------|
| `Calor0251` (`§NN`)    | `:Option<object>` | replace `object` with the concrete element type |
| `Calor0251` (`null`)   | `:object?`        | replace `object` with the concrete type |
| `Calor0252` (`Vec.empty`, `Set.empty`, `List.empty`, `Queue.empty`, `Stack.empty`, `Array.empty`) | `:Vec<object>` etc. | one type parameter; replace `object` |
| `Calor0252` (`Map.empty`, `Dict.empty`, `Dictionary.empty`)  | `:Map<object, object>` etc. | two type parameters |
| `Calor0253`            | `:f64`            | widening default; replace with `:i32` to narrow |

Quick-fixes only fire on canonical bind forms (`§B{name}` or `§B{~name}`)
to keep edit placement provably correct; non-canonical attribute blocks
emit the diagnostic without a fix.

---

## Round-trip

Both forms round-trip stably through `calor convert` and `calor format`:

- A C# `var x = 42;` becomes `§B{x} INT:42` (inferred form).
- A C# `int x = 42;` becomes `§B{x:i32} INT:42` (explicit form).
- Going back, `§B{x} INT:42` becomes `var x = 42;`; `§B{x:i32} INT:42`
  becomes `int x = 42;`.

The emitter never **adds** an annotation that the source did not
already have, and the binder never **drops** an annotation that the
source did have.

---

## See also

- [Types](/calor/syntax-reference/types/) — primitive type names and their C# equivalents
- [Structure Tags](/calor/syntax-reference/structure-tags/) — `§F`, `§I`, `§O` and the surrounding function structure
- [Expressions](/calor/syntax-reference/expressions/) — initializer expressions in Lisp-prefix form
- RFC: [`docs/plans/v0.6-bind-inference-formalization.md`](https://github.com/juanmicrosoft/calor/blob/main/docs/plans/v0.6-bind-inference-formalization.md)
