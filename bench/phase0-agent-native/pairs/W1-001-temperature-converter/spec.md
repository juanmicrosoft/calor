# W1-001 — Temperature Converter (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Existing behavior (already implemented in the starting fixture)

Integer temperature conversions between degrees Celsius, degrees Fahrenheit,
and kelvins. All arithmetic is 32-bit integer arithmetic; division truncates
toward zero.

Every conversion first clamps its input to a supported measurement ceiling
(inputs above the ceiling are treated as the ceiling), then converts:

1. `CelsiusToFahrenheit(c)` — clamps `c` to at most `5000`, returns
   `c * 9 / 5 + 32`.
2. `CelsiusToKelvin(c)` — clamps `c` to at most `5000`, returns `c + 273`.
3. `FahrenheitToCelsius(f)` — clamps `f` to at most `9032`, returns
   `(f - 32) * 5 / 9`.

Valid input domain (callers must respect it; enforce it where your language
can): `CelsiusToFahrenheit` / `CelsiusToKelvin` require `c >= -273`;
`FahrenheitToCelsius` requires `f >= -459`. Behavior below absolute zero is
outside the contract.

Guaranteed output ranges (must be preserved; declare them where your language
can): `CelsiusToFahrenheit` in `[-459, 9032]`; `CelsiusToKelvin` in
`[0, 5273]`; `FahrenheitToCelsius` in `[-273, 5000]`.

## Task

1. **Refactor:** the input-ceiling clamping logic is duplicated in every
   conversion function. Consolidate it into a single shared implementation.
   All existing observable behavior — including every declared input-domain
   and output-range guarantee — must be preserved exactly.
2. **Add** `KelvinToCelsius(k)` — clamps `k` to at most `5273`, returns
   `k - 273`. Valid input domain `k >= 0`; guaranteed output range
   `[-273, 5000]`.

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change any existing function's observable behavior.
- Extend the same domain/range guarantees to the new function wherever your
  language expresses them.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `CelsiusToFahrenheit(int) → int`,
`CelsiusToKelvin(int) → int`, `FahrenheitToCelsius(int) → int`,
`KelvinToCelsius(int) → int`, reachable through the arm's `TestShim.cs`
(provided by the harness; not editable by the agent).
