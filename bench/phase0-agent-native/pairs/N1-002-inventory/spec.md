# N1-002 — Inventory (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Data model

An inventory is a mutable name → quantity map (`Dictionary<string, int>`)
owned by the caller and passed to every operation. Quantities in the map are
always positive: a name with quantity `0` is never stored — it is simply
absent from the map.

## Existing behavior (already implemented in the starting fixture)

`AddItem(items, name, qty)` — adds `qty` units of `name` to `items`: if
`name` is already present its quantity is increased by `qty`, otherwise a new
entry with quantity `qty` is created.

## Task: implement the missing operations

1. `RemoveItem(items, name, qty)` — removes `qty` units of `name` from
   `items`:
   - If `name` is not present, do nothing (no error).
   - If the remaining quantity would be `0` or negative, remove the entry
     entirely (quantities never go to zero or below — see data model).
   - Otherwise decrease the quantity by `qty`.
2. `TotalCount(items)` → integer — the sum of all quantities in `items`.
   Returns `0` for an empty inventory.

## Constraints

- Operations mutate the caller's map in place; no filesystem, network, or
  console effects. Declare whatever your language requires to express that.
- Do not change `AddItem`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `AddItem(Dictionary<string, int>, string, int)`,
`RemoveItem(Dictionary<string, int>, string, int)`,
`TotalCount(Dictionary<string, int>) → int`, reachable through the arm's
`TestShim.cs` (provided by the harness; not editable by the agent).
