# W2-002 — Parity Encoder (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic. An element is **odd** exactly when
`element % 2 != 0` under truncating remainder semantics (so negative odd
numbers such as `-3` count as odd); it is **even** otherwise.

## Existing behavior (already implemented in the starting fixture)

`Encode(values)` → integer — encodes an integer array into a checksum:

```
Encode(values) = (number of odd elements) * 65536 + (number of elements)
```

The supported domain is arrays with fewer than 65536 elements.

## Task: implement the missing validation operations

1. `IsValidLength(values, expectedLength)` → boolean — `true` exactly when the
   number of elements in `values` equals `expectedLength`. **Requires
   `expectedLength >= 0`** (declare where your language can express it;
   callers must respect it).
2. `CountEven(values)` → integer — the number of even elements. The result is
   guaranteed to lie in `[0, number of elements]` — declare this guarantee
   where your language can express it. Returns `0` for an empty array.
3. `IndexOfFirstOdd(values)` → integer — the zero-based index of the first odd
   element, or `-1` if the array contains no odd element (including the empty
   array). The result is guaranteed to be at least `-1` and strictly less than
   the number of elements — declare where expressible.

## Constraints

- Pure computation only: no I/O, no global state, no floating point.
- Do not change `Encode`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Encode(int[]) → int`, `IsValidLength(int[], int) → bool`,
`CountEven(int[]) → int`, `IndexOfFirstOdd(int[]) → int`, reachable through
the arm's `TestShim.cs` (provided by the harness; not editable by the agent).
