# Task: restock planning support

The `Inventory` module counts stock and formats stock lines. Its
counting/formatting functions are **pure** — no I/O, no shared state
(they are called from contexts where file access is forbidden).
`SaveStock` is the only function that touches the filesystem.

Add restock planning:

1. `FormatRestock(name, needed)` — returns `"restock <name>: <needed>"`.
   Pure, like the other formatters.
2. `RestockAmount(shelf, backroom, target)` — the number of units still
   needed to bring the total stock (exactly as `CountTotal` computes it)
   up to `target`; `0` when the total is already at or above `target`.
   Pure, like the other counters.

Rules:

- Public surface: keep every existing public function unchanged in name,
  signature, and behavior; add the two new public functions.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
