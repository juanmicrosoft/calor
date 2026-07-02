# W3-002 — Config Store (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## File format

A config file is a plain-text file of `key=value` lines, one entry per line,
each line terminated by a single `\n`. Keys never contain `=`; values may be
empty. The first line whose key matches wins.

## Existing behavior (already implemented in the starting fixture)

`Get(path, key)` → string — returns the value of the first line whose key is
`key` in the file at `path`. Returns the empty string if the file does not
exist or no line matches.

## Task: implement the missing operations

1. `Set(path, key, value)` — writes `key=value` into the file at `path`:
   - If the file does not exist, it is created containing that single entry.
   - If a line with `key` already exists, that line is replaced in place
     (same position); all other lines are preserved unchanged, in order.
     Only the first matching line is replaced.
   - Otherwise the entry is appended as a new last line.
   - The resulting file consists of `\n`-terminated lines as described above.
2. `Has(path, key)` → boolean — returns whether the file at `path` exists and
   contains a line whose key is `key`. (Note: `Has` distinguishes a key with
   an empty value from an absent key; `Get` alone cannot.)

## Constraints

- Reads and writes go through the real filesystem (no in-memory fakes).
- All operations must correctly declare whatever your language requires for
  filesystem access.
- Do not change `Get`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Get(string, string) → string`, `Set(string, string, string)`,
`Has(string, string) → bool`, reachable through the arm's `TestShim.cs`
(provided by the harness; not editable by the agent).
