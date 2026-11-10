---
layout: default
title: Effects
parent: Syntax Reference
nav_order: 6
---

# Effects

Effects declare the side effects a function may have. This is unique to Calor - traditional languages leave side effects implicit.

---

## Why Declare Effects?

In traditional languages, you must read the entire implementation to know if a function:
- Writes to console
- Reads files
- Makes network calls
- Modifies a database

Calor requires explicit effect declarations:

```calor
func SaveUser (User:user) → bool
  ! db:rw,net:rw           // Declares: database and network operations
  // ...
```

Now an agent knows immediately what side effects to expect.

---

## Effect Syntax

```calor
! cw,fs:w                 // Console write and filesystem write
!                         // No effects (pure function)
```

Place the effect declaration on its own indented line after the signature.
This is a template, not a complete function:

```calor
func Name (Type:parameter) → ReturnType
  ! effects               // Here
  requires condition
  => condition
  // body
```

---

## Effect Rows for Callbacks

When a function accepts another function — a callback, handler, predicate, or
transform — an effect row says what that callback is allowed to do.

**The line matters.** An `!` row on its **own line** is the containing
function's effect declaration. An `!` row on the **same line** as a function
type belongs to that callback, binding, field, property, or return value.

```calor
module Callbacks
  func Apply (Func<int,int>:transform ! cw, int:value) → int
    ! cw
    return transform(value)
```

The `! cw` beside `transform` belongs to the callback type. The `! cw` on
its own line belongs to `Apply`. Calling `transform` charges its `cw` effect to
`Apply`, so both rows are required here.

For a delegate value, `transform.Invoke(value)` charges the same row as
`transform(value)`. The compiler uses the delegate's declaration, not just the
method name `Invoke`. A missing callback row still follows the unknown-effect
rules; `.Invoke()` does not make it pure.

Rows are part of the type. `Func<int,int> !` and
`Func<int,int> ! cw` have the same inputs and output, but different effect
permissions.

### Where a row can appear

```calor
module Rows
  func Use (Func<int,int>:callback ! cw, int:value) → Func<int,int> ! cw
    ! cw
    let Func<int,int>:saved ! cw = callback
    let local = (int:x) ! cw =>
      Console.WriteLine(x)
      return x
    return callback

  delegate Handler (int:value) → void
    ! cw

  class Callbacks
    field Action<int>:onChange private ! cw
    property Action<int>:Current ! cw
      get
      set
```

Rows are supported on:

- function and method declarations,
- lambdas and delegates,
- parameters in inline signatures,
- return types after `→`,
- bindings, fields, and properties.

A type carries one row. Put multiple permissions in that row
(`! cw,fs:w`), rather than writing multiple `!` rows.

### Effect placeholders

A higher-order helper often needs to have exactly the effects of the callback
passed to it. Declare an effect placeholder with `<! e>` and use it in the
callback and function rows:

```calor
module HigherOrder
  func Twice<! e> (Func<int,int>:callback ! e, int:value) → int
    ! e
    let int:first = callback(value)
    return callback(first)
```

`Twice` is pure when `callback` is pure and carries `cw` when the callback may
write to the console. The compiler resolves `e` at each call site.

Effect placeholders can be declared on functions and methods, including
interface methods. They can be used in that declaration's own row and its
parameter rows. A placeholder cannot use the name of a real effect code such
as `cw`.

### What the compiler checks

The compiler checks a row whenever a function value moves:

- binding or reassigning it,
- passing it as an argument,
- returning it,
- overriding a method,
- implementing an interface member.

A callback whose effects exceed the destination row is `Calor0424`, always an
error. If one side has no row and the compiler cannot decide, it reports
`Calor0425`; strict mode promotes that warning and permissive mode waives it.

Lambda rows are promises too. If a lambda body does more than its row allows,
the compiler reports the mismatch. If the lambda has no written row, the
compiler infers one from the body when it can.

### Common row mistakes

`Calor0405` means a row is attached to something that cannot carry one, is on
the wrong line, or duplicates another row:

```calor
field int:count private
! cw // Wrong: count is not a function value, and this is a separate line.
```

`Calor0404` means an effect placeholder is declared or used somewhere it is
not allowed, or is named after a real effect code.

### Current limitations

Effect rows do not yet fully verify callbacks reached through reflection or
`dynamic`, event-handler `+=`, or delegates returned by .NET libraries.
Unresolved cases fail closed: the caller is charged `unknown`, and an
under-declared caller fails the build. These boundaries are never silently
treated as verified. Permissive effects explicitly waive that fail-closed
behavior for unknown calls. Async rows remain limited; consult the diagnostics
and the [project index](https://calor.dev/docs/cli/query/) before relying on a higher-order
boundary.

---

## Effect Codes

Metadata property reads charge the getter's effect row whether the receiver is
fully qualified, imported, or aliased. Reading
`System.Environment.CurrentDirectory`, for example, requires `env:r`; spelling
the type's full name does not make the read pure.

An array's `.Length` read is pure when the binder has established an array
receiver, including an inferred local or its alias. Creating the array still
requires `alloc`. Other getters named `Length` keep their own effect rows:
`System.IO.FileInfo.Length`, for example, requires `fs:r`.

| Code | Effect | Description | C# Examples |
|:-----|:-------|:------------|:------------|
| `cw` | Console write | Output to console | `Console.WriteLine()` |
| `cr` | Console read | Input from console | `Console.ReadLine()` |
| `fs:r` | Filesystem read | Read from filesystem | `File.ReadAllText()` |
| `fs:w` | Filesystem write | Write to filesystem | `File.WriteAllText()` |
| `fs:rw` | Filesystem read/write | Read and write filesystem | `File.Copy()` |
| `net` | Network | Unspecified network access | Socket operations |
| `net:r` | Network read | HTTP GET, etc. | `HttpClient.GetStringAsync()` |
| `net:w` | Network write | HTTP POST, etc. | `HttpClient.PostAsync()` |
| `net:rw` | Network read/write | HTTP operations | `HttpClient.SendAsync()` |
| `db` | Database | Unspecified database access | Connection setup |
| `db:r` | Database read | Database queries | `SELECT` queries |
| `db:w` | Database write | Database mutations | `INSERT/UPDATE/DELETE` |
| `db:rw` | Database read/write | Database operations | ORM calls |
| `env` | Environment | Unspecified environment access | Environment operations |
| `env:r` | Environment read | Read environment variables | `Environment.GetEnvironmentVariable()` |
| `env:w` | Environment write | Write environment variables | `Environment.SetEnvironmentVariable()` |
| `env:rw` | Environment read/write | Read and write environment | Read-modify-write of environment variables |
| `proc` | Process | Spawn or control processes | `Process.Start()` |
| `mut` | Mutation | Write shared or caller-owned state | Fields, collections, `ref`/`out` parameters |
| `mut:col` | Collection mutation | Mutate a collection | Indexed dictionary update |
| `alloc` | Allocation | Allocate an object or buffer | `new`, array allocation |
| `unsafe` | Unsafe memory | Use unsafe memory operations | Pointer and fixed operations |
| `time` | Time | Read a nondeterministic clock | `DateTime.Now` |
| `rand` | Randomness | Generate random values | `Random.Next()` |
| `throw` | Exception | Intentionally throw an exception | `throw` statements |

Writing a `ref` or `out` parameter requires `mut` because it changes the caller's
storage. Reassigning an ordinary value parameter or a local does not.

---

## Examples

### Pure Function (No Effects)

```calor
func Add (int:a, int:b) → int
  // No ! row declares this function pure; an effectful body would be an error.
  return a + b
```

Or explicitly:

```calor
func Add (int:a, int:b) → int
  !                       // Explicitly no effects
  return a + b
```

### Console Output

```calor
func Greet (string:name) → void
  ! cw                    // Console write
  Console.WriteLine(name)
```

### File Operations

```calor
func CopyFile (string:source, string:dest) → bool
  ! fs:rw                 // Filesystem read and write
  // ...
```

### Network Call

The historical public `FetchData` fragment took a string parameter named `url`
and returned the retired `str!str` result carrier. There is no approved replacement signature for that
example here. This fragment preserves only its network effect row; it does not
claim that the original return type is currently authorable:

```calor
! net:rw                  // Network operations
```

### Database with Logging

```calor
func CreateUser (User:user) → int
  ! db:rw,cw              // Database and console (for logging)
  // ...
```

### Multiple Effects

```calor
func ProcessOrder (Order:order) → bool
  ! db:rw,net:rw,fs:w,cw   // Database, network, filesystem write, console write
  // ...
```

---

## Effect Patterns

### Read-Only vs Read-Write

```calor
// Read-only file operation
func LoadConfig (string:path) → Config
  ! fs:r                  // Only filesystem read
  // ...

// Read-write file operation
func UpdateConfig (string:path, Config:config) → void
  ! fs:rw                 // Filesystem read and write
  // ...
```

### Interactive Console

```calor
func Prompt (string:question) → string
  ! cw,cr                 // Console write and read
  Console.WriteLine(question)
  return Console.ReadLine()
```

This fragment retains the original non-null return signature. `ReadLine()` can
return null at end of input, so completing it as written reports `Calor0273`.
Choose the API's null-handling policy before using it as a complete program;
the effect row alone does not establish a non-null result.

---

## Benefits for Agents

### 1. Filtering by Effect

"Find all functions that access the database":
```
// Agent queries for functions whose effect rows include db:r, db:w or db:rw.
```

### 2. Refactoring Safety

"This function should be pure, but it has effects":
```calor
func Calculate () → int
  ! cw                    // Wait, why is Calculate logging?
```

### 3. Testing Strategy

- Functions with no effects: Unit test directly
- Functions with `cw/cr`: Mock console
- Functions with `fs:r/fs:w/fs:rw`: Mock filesystem
- Functions with `net:r/net:w/net:rw`: Mock HTTP
- Functions with `db:r/db:w/db:rw`: Mock database

### 4. Composition Analysis

```calor
// If f1 calls f2, f1's effects must include f2's effects
func ProcessAndSave () → void
  ! db:rw,cw              // Must include Process's effects
  Process()

func Process private () → void
  ! cw                    // Has console write effect
  // ...
```

---

## Effect Checking

Effect enforcement is on by default for compile, build, and watch workflows.
Two rules drive everything: a function must declare every effect its body has,
and a caller must declare everything its callees may do. A call the compiler
cannot look up is never quietly treated as harmless.

Collection mutators such as `Add`, `Remove`, `Clear`, `Insert`, `Sort`, and
`CopyTo` are not on the known-pure list.

### Asking the index

`calor query effects Name` reports a callable's declared row, inferred body
row and verdict: `fits`, `does not fit`, or `cannot tell`. The index reads the
same analysis used by compilation; it does not independently infer a more
optimistic answer. Assumed effects disclose their reasons. Use `--json` for
structured results.

`calor query impact Log --effects --row fs:w` asks which direct and transitive
callers would no longer fit if `Log` acquired that row. Omit `--row` to use
the current row, or use `--row ""` to ask about a pure replacement. A query
does not edit a declaration or discharge an unknown call.

### What the Compiler Reports

| Code | Level | What it means |
|:-----|:------|:--------------|
| `Calor0410` | Error | A function does something its `!` row does not list. The message often shows the chain of calls that got there. |
| `Calor0411` | Warning | The compiler cannot look up what an outside call does. Add the method to a `.calor-effects.json` manifest. `--strict-effects` makes it an error. |
| `Calor0417` | Warning | A public function has no `!` row at all, so callers in other files have nothing to check against. Raised by the cross-module pass, so it only appears when you compile more than one file together. |
| `Calor0419` | Warning | The effects are assumed rather than checked — the body holds raw C# interop, or something the compiler does not recognise, or a call to a function whose own effects are assumed. `--strict-effects` makes it an error. |
| `Calor0420` | Error | An override declares **more** effects than the method it overrides. Declaring fewer is fine. |
| `Calor0421` | Error | An interface implementation declares **more** effects than the interface method. Declaring fewer is fine. |
| `Calor0424` | Error | A callback's effects do not fit where it is being bound, passed, or returned. |
| `Calor0425` | Warning | The compiler cannot decide whether a callback fits, because one side carries no `!` row. `--strict-effects` makes it an error. |
| `Calor0418` | Error | You invoked something that is provably not a function — an `int`, a string, an array — so there is no effect list to charge. |
| `Calor0406` | Error | Effect checking stopped before it finished. New in 0.16; see below. |

Calling a function value — a callback parameter, a lambda you bound to a name,
a callback field — is not an error. Since 0.15 the compiler reads the
callback's own `!` row and charges it to whoever calls it. `Calor0418` is left
for the one case where the thing being invoked is not a function at all.

### When Effect Checking Gives Up: Calor0406

New in 0.16. Effect checking runs two loops, and each has a safety limit so
that a tangle of functions calling each other cannot make the compiler spin
forever:

- Working out the effects of a group of functions that all call each other.
  The limit is 100 rounds. From 100 functions upward the limit is instead one
  round per function in the group, plus one more round to confirm nothing
  changed — so a big but ordinary group never trips it.
- Carrying the real value of an effect placeholder up through a chain of
  callers. The limit is 10,000 steps.

If either loop reaches its limit while the answer is still changing, the
compiler reports `Calor0406`, naming which loop stopped, the limit, and the
functions involved. It is always an error and no flag waives it: a check the
compiler did not finish must not be handed back as a clean build.

Before 0.16 the first loop reported `Calor0600` — a code that belongs to a
different family — and the second stopped without saying anything at all.

### Turning the Checks Down

`--no-enforce-effects` switches effect checking off. Use it only as a
deliberate migration waiver.

`--permissive-effects` is the gentler step. It keeps checking on but assumes a
call the compiler cannot look up does nothing. In a project file:

```xml
<PropertyGroup>
  <CalorPermissiveEffects>true</CalorPermissiveEffects>
</PropertyGroup>
```

What it waives:

- `Calor0411` and `Calor0425` — the two "I cannot tell what this does"
  messages — are not reported at all.
- `Calor0410`, "this function does something it never said it would", is
  reported as a warning instead of an error. That holds both inside a single
  file and across files.

What it never waives — these stay errors under every flag:

- `Calor0424`, a callback whose effects do not fit where it is going.
- `Calor0420` and `Calor0421`, an override or interface implementation that
  declares **more** effects than the member it inherits from. Declaring fewer
  is always allowed.
- `Calor0418` and `Calor0406`.
- Three narrower forms of `Calor0410`: the one about a lambda's own `!`
  line, and two about effect variables.

Waiving "we cannot tell" is honest; waiving "we know this is wrong" is not.

Because unknown calls are counted as doing nothing, permissive mode gives up
the effect guarantee. [Review packets](https://calor.dev/docs/cli/review-packet/) disclose that
waiver on their first line.

---

## Next

- [Effect manifests and package import](/calor/guides/effect-manifests/) - External call coverage
- [calor query](https://calor.dev/docs/cli/query/) - Ask what a function declares, what it really does, and what a change would break
- [Adoption Playbook](/calor/guides/adoption-playbook/) - Applying effects in an existing solution
