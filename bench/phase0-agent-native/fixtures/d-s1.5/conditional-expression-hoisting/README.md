# Conditional expression hoisting: native positive control

This fixture retains `Partial` support and requires **zero conversion losses**.
It certifies `InteropPreserved` absent only for the native subset exercised here;
assignment operands and other unsupported shapes remain outside this proof.

`Run` checks the original false-`&&`/postfix reproduction before any other
mutation: `i` must remain zero. It then checks selected `&&`, skipped and selected
`||`, and both selections of `?:`. Each check verifies the operand's value and
the increment count, returning a distinct negative result on failure.
The final value is **329**: `i = 3`, selected postfix value `2`, alternate value `9`.

`expected.calr` is generated with the exact registry conversion options and the
filename `conditional-expression-hoisting.cs`. `test.calr` repeats that converted
program with a `Run` postcondition requiring 329. The manifest also executes the
converted output, rather than checking only the handwritten postcondition.
Original C#, generated C#, and the Calor value test were each executed twice
during fixture validation.
