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
was added in v0.6 and is enforced through the main `calor compile`
pipeline by `BindValidationPass`.

### Strict-mode diagnostics (default-on since v0.6.3)

The following diagnostics are reserved in the `Calor0250-0259` range
and are enforced **by default** as of v0.6.3 (RFC §6). To opt out
during migration, pass `--no-strict-bind-inference` to `calor compile`
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

The reassignment target may be a local, a parameter, or a class field — all
declared types the check can see. The array is recognized when the value calls a
known array-returning BCL method (`File.ReadAllLines`/`ReadAllBytes`,
`Directory.GetFiles`/`GetDirectories`/`GetFileSystemEntries`) or a user function
declared `-> [T]`.

Scope notes: the check runs inside every block body — loop bodies (including
`§EACH`/`§EACHKV`), `§IF` branches, and while, match, try, using, sync, and
unsafe/fixed blocks. It does **not** descend into block-lambda (`§LAM`) bodies,
so a lambda declared `-> List<T>` returning an array is not checked. Argument
position (an array passed to a `List<T>` parameter) is tracked in issue #725.

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
