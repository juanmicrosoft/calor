# Local functions: native positive control (#847, 0.25 F3)

This fixture registers the `local-function` promotion from `NotSupported` to
`Partial`. It requires **zero conversion losses** and certifies
`InteropPreserved` absent only for the native slice exercised here: non-capturing
local functions declared directly in a method body with plain by-value
parameters. Capturing, generic, async, iterator and nested local functions stay
outside this proof and are still preserved as `§CSHARP`.

`Run` checks three things, each returning a distinct negative result on failure:

- **Shadowing.** The class has a method `Add(x) => x + 1000`. A local function
  `Add(x) => x + 1` is declared after its call site. The call must bind to the
  local function: `Add(2)` is `3`, not `1002`.
- **A `static` local function** (`Square(4)` is `16`).
- **A recursive local function** (`Fact(5)` is `120`).

The final value is **31620**: `3 * 10000 + 16 * 100 + 120 % 100`.

`expected.calr` is the output of the exact registry conversion options with the
filename `local-function.cs`. `test.calr` repeats that converted program with a
`Run` postcondition requiring 31620. The manifest also executes the converted
output, not only the handwritten postcondition.
