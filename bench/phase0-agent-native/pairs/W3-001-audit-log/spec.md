# W3-001 — Audit Log (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Existing behavior (already implemented in the starting fixture)

`Append(path, entry)` — appends `entry` followed by a single `\n` to the file at
`path`, creating the file if it does not exist.

## Task: implement the missing operations

1. `CountEntries(path)` → integer — returns the number of appended entries
   (lines) in the file at `path`. Returns `0` if the file does not exist.
2. `LastEntry(path)` → string — returns the most recently appended entry
   (without its trailing newline). Returns the empty string if the file does
   not exist or has no entries.

## Constraints

- Reads and writes go through the real filesystem (no in-memory fakes).
- All operations must correctly declare whatever your language requires for
  filesystem access.
- Do not change `Append`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Append(string, string)`, `CountEntries(string) → int`,
`LastEntry(string) → string`, reachable through the arm's `TestShim.cs`
(provided by the harness; not editable by the agent).
