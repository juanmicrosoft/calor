---
layout: default
title: Structure Tags
parent: Syntax Reference
nav_order: 1
---

# Structure Tags

Calor declarations use lowercase keywords, space-separated headers and
two-space indentation. The route retains its historical name, but active
source uses `module`, `func`, `class` and the other native forms below.
Expressions use infix operators and ordinary calls.

The [generated current-language catalog](/calor/syntax-reference/current-language/)
is the shared vocabulary reference. Positive complete programs compile with
normal effect enforcement; negative programs and fragments are explicitly marked.

## Raw C# blocks

The converter can preserve unsupported C# at its owning compilation-unit,
member or statement boundary. Its counted raw frames retain the payload's
length, bytes and scope; they are preservation output, not a way to claim
native syntax support or verified effects. Do not handwrite payload wrappers
or move namespace-local imports into a different scope.
Retain the original references and parse context when converting; missing
metadata is not evidence that a replacement is equivalent.

## Modules

The first `module` labels the compilation unit. It is **not** a namespace or
a named static class. An optional namespace follows the label.

| Header | Namespace |
|:-------|:----------|
| `module FileLabel` | Global |
| `module FileLabel Contoso.Tools` | File-scoped `Contoso.Tools` |
| `module FileLabel Contoso.Tools block` | Block `Contoso.Tools` |

Without a namespace, declarations enter the global namespace and root functions
emit in `GlobalModule`. With namespace `Demo`, root functions emit in
`Demo.DemoModule`. The label itself does not become a CLR type.
`block` requests a block namespace instead of a file-scoped namespace; it does
not change the namespace's CLR identity.

### Named static containers

A nested `module` declares a named static container. Its functions are
implicitly static. Use this form instead of a static class declaration.

```calor
module StructureModules Demo
  using System

  module Arithmetic
    func Twice (int:value) → int
      !
      return value * 2

  func Main () → int
    !
    return Arithmetic.Twice(21)
```

`Demo.DemoModule.Main()` returns `42`; the nested container is
`Demo.Arithmetic`, not `Demo.StructureModules.Arithmetic`.

<a id="namespace-scopes"></a>
<a id="using-directives"></a>

### Namespace and import scope

Imports appear inside the file module. Ordinary `using` directives are
compilation-unit imports, even under a header that names a namespace.
`namespace using` places an import inside that named namespace; it is not a
namespace declaration or block. It requires a named namespace in the module header.

```calor
module BlockNamespace Demo.Tools block
  using Text = System.Text
  using static System.Math

  class Box
    field Text.StringBuilder?:Builder
    func Read () → int
      !
      return 42
```

The class is `Demo.Tools.Box`, and `new Demo.Tools.Box().Read()` returns `42`.
The alias names `System.Text`; `Builder` is a nullable `StringBuilder` field.
Other import forms include `using System.Text` and `global using System`.
For namespace-local lookup, write `namespace using System.Text`,
`namespace using static System.Math`, or `namespace using Text = System.Text`.
Local aliases can shadow compilation-unit aliases without changing either target.

**Namespace preservation boundary:** the native header represents one
namespace scope. `ConvertProject` and `migrate --split-namespaces` can retain
resolved imports in their original compilation-unit or namespace-local scope.
Single-result `Convert` and `convert` retain the whole-file preservation boundary
for namespace-local imports. Multiple namespace declarations,
nested namespaces, and conditional or unsupported scoped-import topology still
require preservation. Missing original names or references also prevent native
scoped-import conversion; implicit generated imports are not a repair.
A projection that changes declared type or nullability metadata also stays
preserved, rather than receiving native credit.
Do not flatten these boundaries and claim equivalent lookup behavior.
Converter-preserved C# is not native language coverage, and raw payloads are not
handwritten examples.

## Functions

Use the same `func` header for root functions, named-module functions and class
methods. Parameters put the type before the name; `→` introduces the return type.

```calor-fragment
func Name (int:value, string:text) → int
func Hidden private () → void
func Compute protected virtual (int:value) → int
```

Root and named-module functions emit static methods. Ordinary class methods
are instance methods unless declared `static`.

### Visibility

Public is the default. Write full accessibility keywords, not abbreviated
header fields. The available member accessibilities correspond to C#:
`public`, `private`, `internal`, `protected`, `protected internal`, and
`private protected`. A visibility must also be legal in its containing scope;
for example, a top-level class cannot be private.

### Examples

Effect and contract clauses precede the first body statement. Empty `!`
declares a pure row; `! cw` admits console output.

```calor
module FunctionHeaders
  func Add (int:a, int:b) → int
    !
    return a + b

  func Divide (int:a, int:b) → int
    !
    requires b != 0
    => result == a / b
    return a / b

  func PrintSum (int:a, int:b) → void
    ! cw
    Console.WriteLine(Add(a, b))

  func Main () → int
    !
    return Add(2, 3) + Divide(10, 2)
```

`Main()` returns `10`. `PrintSum(2, 3)` writes `5` followed by a newline.
`requires` has no native custom-message clause; use an explicit condition
and exception when an API needs a specific failure message.
See [contracts](/calor/syntax-reference/contracts/) and
[effects](/calor/syntax-reference/effects/) for their enforcement boundaries.

## Async Functions

Put `async` after the function name and before the parameters.
The declared output describes the body result or an explicit .NET carrier.

```calor
module AsyncFunctions
  using System.Threading.Tasks

  func Read async () → int
    !
    => result == 21
    return await Task.FromResult(21)

  func ExplicitTask async () → Task<int>
    !
    return await Task.FromResult(7)

  func ExplicitValueTask async () → ValueTask<int>
    ! alloc
    return await new ValueTask<int>(9)

  func Wait async () → void
    !
    await Task.CompletedTask
```

Awaiting `Read()` produces `21`. Its CLR return type is `Task<int>`, and its
postcondition describes `21`, not the task. `ExplicitTask()` also returns
`Task<int>`, without double wrapping. `ExplicitValueTask()` retains
`ValueTask<int>` and produces `9`. `Wait()` returns a non-generic `Task`.

### Automatic Task Wrapping

| Async output | CLR carrier | Body result |
|:-------------|:------------|:------------|
| `int` | `Task<int>` | `int` |
| `string` | `Task<string>` | `string` |
| `void` | `Task` | No value |
| `Task<int>` | `Task<int>` | `int` |
| `ValueTask<int>` | `ValueTask<int>` | `int` |
| Non-generic `Task` or `ValueTask` | Same carrier | No value |

A synchronous function returning `Task<int>` is different: its body returns
the task itself. A source type named `Task` or a custom `MyTask<T>` is not
automatically treated as the .NET carrier.

## Async Methods

Class methods use the same header and carrier rules. Ordinary method modifiers
remain in the header, for example `func Read async virtual () → int` or
`func Read async static () → int`.

```calor
module AsyncMethods
  using System.Threading.Tasks

  class DataService
    func GetUserAsync async (int:id) → int
      !
      return await Task.FromResult(id + 1)

  func Main async () → int
    ! alloc
    let DataService:service = new DataService()
    return await service.GetUserAsync(41)
```

Awaiting `Main()` returns `42`. See
[inheritance](https://calor.dev/docs/syntax-reference/inheritance/) for virtual, override,
abstract, sealed and static method declarations.

## Await Expression

Use `await expression` for an ordinary await.

```calor
module AwaitExpressions
  using System.Threading.Tasks

  func Main async () → int
    !
    let int:value = await Task.FromResult(42)
    if await Task.FromResult(true)
      return value
    return 0
```

Awaiting `Main()` returns `42`. Await can initialize a binding, supply a
condition or appear in a return expression. Awaiting a non-generic carrier
has no value; use it as a statement, as in `Wait` above, not as a binding
initializer. Custom awaitables are not admitted merely by their names.

### ConfigureAwait limitation

The earlier constructed-type limitation is fixed. This .NET call now resolves
under normal effect enforcement; awaiting `Read()` returns `42` with the pure
row shown. The compiler retains the constructed `Task<int>` and awaiter types
rather than rebuilding their identity from display strings.

```calor
module ConfigureAwaitLimit
  using System.Threading.Tasks
  func Read async () → int
    !
    return await Task.FromResult(42).ConfigureAwait(false)
```

The bool overload accepts both `false` and `true`. Configured non-generic
`Task`, `ValueTask` and `ValueTask<T>` carriers are also supported. Receiver and
argument effects still count, and a wrong argument cannot borrow this pure
framework contract. This is not arbitrary custom-awaitable support or inference
for an uninstantiated generic metadata definition.

## Lambda Expressions

Use `(type:name, ...) => expression`, or place an indented statement body after
`=>`. Lambda effect rows follow the closing parameter parenthesis and precede
`=>`. Keep those tokens on one line. There are no brace bodies or closing tags.

```calor
module LambdaExamples
  func Main () → int
    ! alloc
    let Func<int,int>:twice ! = (int:x) ! => x * 2
    let Func<int,int,int>:add ! = (int:a, int:b) ! => a + b
    let Func<int>:answer ! = () ! => 7
    let Func<int,int>:block ! = (int:x) ! =>
      let int:next = x + 1
      return next * 2
    return twice(3) + add(2, 3) + answer() + block(2)
```

`Main()` returns `24`. The explicit pure rows describe these callables; an
unannotated lambda is not automatically proof of purity. Delegate allocation
is separate from invocation effects.

Lambdas may capture enclosing variables and may be passed as call arguments,
including LINQ arguments when the selected API and higher-order effect
requirements are supported. Preserve the target delegate type and required
row instead of assuming every callback is pure.

### Async Lambdas

An async lambda uses `async` before its parameter list:

```calor
module AsyncLambdas
  using System.Threading.Tasks

  func Main async () → int
    ! alloc
    let Func<int,Task<int>>:next ! = async (int:x) ! =>
      return await Task.FromResult(x + 1)
    return await next(41)
```

Awaiting `Main()` returns `42`. Its delegate returns `Task<int>`, while the
lambda body returns an `int`.

## Local Functions

Bind a local helper to a lambda and declare it before use. Nested `func`
declarations are not the local-callable form.

```calor
module LocalCallables
  func Use (int:value) → int
    ! alloc
    let Func<int,int>:addOne ! = (int:x) ! => x + 1
    return addOne(value)
```

`Use(2)` returns `3`. This is a delegate binding, not a forward-declared
static local function. It does not authorize recursive or mutually recursive
recipes. The converter lowers only supported C# local-function shapes;
unsupported shapes remain explicitly preserved C#, not rewritten by guesswork.
See the catalog's [lambda entry](/calor/syntax-reference/current-language/#lambda-expression).

## Delegate Definitions

A delegate names a callable signature. Use inline parameters and an optional
indented effect row. Delegates can appear at file or class scope.

```calor
module DelegateDefinitions
  delegate Calculator (int:a, int:b) → int
    !

  delegate Logger (string:message) → void
    ! cw

  func Apply (Calculator:operation !, int:a, int:b) → int
    !
    return operation(a, b)

  func Main () → int
    ! alloc
    let Calculator:add ! = (int:a, int:b) ! => a + b
    return Apply(add, 2, 3)
```

`Main()` returns `5`. The CLR `Calculator.Invoke` signature returns `int`
and takes two `int` parameters; `Logger.Invoke` returns `void` and takes a
`string`. Its `cw` row describes allowed callback effects, not automatic
console output from declaring the delegate.

## Enum Definitions

An enum has indented members, optional explicit values and an optional CLR
underlying type. The default underlying type is `int`.

```calor
module EnumDefinitions
  enum Color
    Red
    Green
    Blue

  enum StatusCode
    Ok = 200
    NotFound = 404
    Error = 500

  enum Access byte
    None = 0
    Read = 1
    Write = 2

  func Main () → bool
    !
    return Color.Red != Color.Blue && StatusCode.Ok != StatusCode.Error
```

`Main()` returns `true`. `Color.Red` has value `0`, `StatusCode.NotFound`
has value `404`, and `Access` has underlying CLR type `byte`.
Names with bit-like values are not automatically a `[Flags]` declaration;
add the ordinary attribute when that metadata is required.

## Enum Extension Methods

Use a named static module, with `this` before the first parameter's type.
There is no separate enum-extension declaration keyword.

```calor
module EnumExtensions
  enum Color
    Red
    Green
    Blue

  module ColorExtensions
    func ToHex (this Color:self) → string
      !
      return match self
        case Color.Red → "#FF0000"
        case Color.Green → "#00FF00"
        case Color.Blue → "#0000FF"
        case _ → "unknown"

    func IsPrimary (this Color:self) → bool
      !
      return self == Color.Red || self == Color.Green || self == Color.Blue

  func Main () → string
    !
    return ColorExtensions.ToHex(Color.Red) + ":" + ColorExtensions.IsPrimary(Color.Blue)
```

`Main()` returns `"#FF0000:True"` through normally enforced **native explicit
static calls**. The binder retains the actual source enum owner and member
identity when selecting the overload; it does not substitute an integer or a
same-named metadata enum.

The methods also carry CLR extension-method metadata. A C# caller evaluating
`Color.Red.ToHex() + ":" + Color.Blue.IsPrimary()` gets the same string.

Native receiver-style calls are supported when the receiver and the first
`this` parameter resolve to actual source enum types. The following program
returns `"#FF0000"` under normal effect enforcement. The compiler selects the
source method and emits its fully qualified static call:

```calor
module EnumReceiverLimit
  enum Color
    Red
  module ColorExtensions
    func ToHex (this Color:self) → string
      !
      return "#FF0000"
  func Receiver () → string
    !
    return Color.Red.ToHex()
```

This admission does not cover arbitrary object, generic, or metadata extension
receivers. Ambiguous, inaccessible, or preserved competing declarations cannot
borrow the source method's effect contract. Inherited enum instance-member
names remain conservatively unresolved.

Escaped enum member names also retain their source owner and member identity.
The `@` escape is part of the source spelling, not a different member.
This normally enforced program returns `7`:

```calor
module EscapedEnumLimit
  enum Color
    @event = 7
  module Extensions
    func Pick (Color:value) → int
      !
      return 7
  func Main () → int
    !
    return Extensions.Pick(Color.@event)
```

An explicit `global::` qualifier in this argument position is not current
native grammar. This complete negative reports `Calor0100`, not a successful
escape from the shadowing parameter:

```calor expect=Calor0100
module GlobalEnumLimit
  enum Color
    Red
  module Extensions
    func Pick (Color:value) → int
      !
      return 7
  func Main (int:Color) → int
    !
    return Extensions.Pick(global::Color.Red)
```

## Event Definitions

Use `event DelegateType:Name`, followed by visibility or supported modifiers
when needed. A simple event has no body. A custom event pairs indented `add`
and `remove` bodies.

```calor
module EventDefinitions
  delegate Handler (int:value) → void

  class Publisher
    event Handler:Changed

  class SubscriptionCounter
    field int:Count = 0
    event Handler:Changed
      add
        set Count Count + 1
      remove
        set Count Count + 10
```

`Publisher.Changed` has CLR delegate type `Handler`. For
`SubscriptionCounter.Changed`, adding and removing one handler through the CLR
event API leaves `Count` equal to `11`. This accessor demonstrates bookkeeping;
it does not store handlers or promise notification delivery.
Named-module events are static; ordinary classes can explicitly declare
`event Handler:Changed static`.

## Event Subscribe/Unsubscribe

**Native event subscription and unsubscription statements are not admitted.**
Do not invent `+=` or `-=` statements or use retired subscription tags.
The declarations and accessors above are native, but the CLR event API
observation is not a native subscription example.

When migrating a C# program that subscribes to an event, use the converter's
preservation output and retain its original behavior. Preservation is not
native authoring support. See the catalog's
[event entry](/calor/syntax-reference/current-language/#event-definition).

## Input Parameters

Function, method and delegate parameters are inline and comma-separated:
`(int:x, string:name, bool:flag, int?:maybeValue)`. Types precede names.
Parameter modifiers precede types, for example `ref int:value`,
`out int:result`, `in int:value`, or `params int[]:items`.
Defaults follow `=`, as in `int:increment = 1`.

```calor
module ParameterArrays
  func Total (params int[]:items) → int
    !
    var int:sum = 0
    each int:item in items
      set sum sum + item
    return sum

  func Add (int:value, int:increment = 1) → int
    !
    return value + increment

  func Main () → int
    ! alloc
    return Total(1, 2, 3) + Add(0)
```

`Main()` returns `7`. See [calls](/calor/syntax-reference/calls/) for named
arguments, caller storage for `ref`/`out`, and their executable examples.
Attribute metadata on a parameter goes after its name and before its effect
row or default value.

## Output Type

Write `→ Type` after the parameter list. Use `void` for no body result and
bare `return` for an early void return. Examples include `→ int`,
`→ string?`, `→ byte[]`, and `→ List<string>`.

The output type is a receiving boundary, not a conversion escape hatch.
Arrays and collections retain their actual CLR types. Async outputs follow
the carrier rules above; postconditions use the body `result`.

## Array Types

Use `T[]`, not a prefix bracket form. Jagged arrays use `T[][]`, rectangular
arrays use `T[,]`, and nullable elements use `T?[]`.

| Type | Meaning |
|:-----|:--------|
| `byte[]` | One-dimensional byte array |
| `int[]` | One-dimensional integer array |
| `string[]` | One-dimensional string array |
| `int[][]` | Array of integer arrays |
| `int[,]` | Rectangular two-dimensional integer array |
| `int?[]` | Array of nullable integers |

```calor
module ArrayDeclarations
  class DataProcessor
    field byte[]?:Buffer
    field int[]?:Indices

    func ProcessData (string[]:args) → int
      !
      return args.Length

  func First (int[][]:rows) → int
    !
    return rows[0][0]
```

`new DataProcessor().ProcessData(new string[] { "a", "b" })` returns `2`;
`First(new int[][] { new int[] { 7 } })` returns `7`.
The fields are nullable because this example does not initialize them.
See [types](/calor/syntax-reference/types/) for construction and nullability.

## Block Structure (Indentation)

Bodies end at dedent. Indent each nested body two spaces; do not mix tabs and
spaces. Branch continuations such as `else if` and `else` align with their
`if`; `catch` and `finally` align with their `try`. A `case` is indented under
its `match`, and statements in a case body are indented again.

```calor
module IndentedBlocks
  func Main () → int
    !
    var int:total = 0
    for i from 1 to 3 step 1
      if i > 1
        set total total + i
      else
        set total total + 10
    return total
```

`Main()` returns `15`; the final `return` is outside the loop.
Lambdas with statement bodies also end at dedent. There are no structural
closing tags.

### Tag Reference

The historical heading is retained for incoming links. Use the
[generated catalog](/calor/syntax-reference/current-language/) for the full
native declaration vocabulary, and
[control flow](/calor/syntax-reference/control-flow/) for `if`, `while`,
`for`, `each`, `match`, `try` and their continuations.

## Generics

Add `<T>` or `<T, U>` after a function or type name. Place `where` constraints
inside the declaration, before body statements. Generic type references use
ordinary angle brackets, for example `Dictionary<string,List<int>>`.

```calor
module GenericDeclarations
  interface IRepository<T>
    where T : class
    func Get (int:id) → T
      !

  class Box<T>
    where T : class
    func Echo (T:value) → T
      !
      return value

  func Identity<T> (T:value) → T
    !
    return value

  func Main () → int
    !
    return Identity<int>(42)
```

`Main()` returns `42`. `IRepository<T>` and `Box<T>` retain their reference-type
constraints. Methods use the same generic function header within a class.
Other constraint forms include `where T : struct`, `where T : new()`, and
`where T : class, IComparable<T>`. A constructor constraint is an obligation on
the type argument, not permission to omit allocation effects.

A marker interface can have no members. Sibling declarations still belong at
the same indentation; do not rely on historical empty-block parsing quirks.

## C# Attributes

Attach `[@AttributeName]` or `[@AttributeName(arguments)]` after a declaration
header. Multiple attributes are adjacent. These are CLR attributes, not Calor
effect or contract clauses.

```calor
module AttributeDeclarations
  class Data [@Serializable][@System.ComponentModel.Description("payload")]
    property string?:Email [@System.ComponentModel.Description("address")]
      get
      set

    func Read (int:value [@System.ComponentModel.Description("input")] = 7) → int
      !
      return value
```

`new Data().Read()` returns `7`. Reflection sees `SerializableAttribute` and
the description `"payload"` on `Data`, `"address"` on `Email`, and `"input"`
on the `value` parameter.

Attributes are supported on classes, interfaces, methods, properties, fields
and parameters; preserve the correct CLR target. Positional arguments use
`[@Name(value)]`; named arguments use `[@Name(Key="value")]`; mixed arguments
use `[@Name(value, Key="value")]`. Arguments must be valid attribute constants.
Escape a literal interpolation placeholder as `"\${value}"` rather than
turning an attribute argument into a runtime interpolated string.

Framework attributes such as ASP.NET routing and data-validation attributes
also require their real framework references. An attribute name alone does
not supply that dependency or implement its runtime behavior.

## Why Indent-Based Blocks?

Indentation makes lexical nesting visible without structural closing tokens.
Declaration identity is maintained by tooling in `.calor/ids.json`, not IDs
on source openers; use identity-aware rename and move tools.
These design properties do not by themselves establish a measured token
saving or a correctness improvement.

## Next

- [Types](/calor/syntax-reference/types/) — CLR types, arrays and nullable values
- [Inheritance](https://calor.dev/docs/syntax-reference/inheritance/) — classes and interfaces
- [Generated catalog](/calor/syntax-reference/current-language/) — shared native forms
