# W3-003 — Key-Value Journal (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Data model

A journal is a text file of lines, each of the form `key=value` followed by a
single `\n`. The file is append-only except for compaction. Callers
guarantee: keys are non-empty and contain neither `=` nor newlines; values
contain no newlines but **may contain `=`**. The separator between key and
value is therefore the **first** `=` on the line. The same key may appear on
many lines; the **last** occurrence is the current value.

## Existing behavior (already implemented in the starting fixture)

`Set(path, key, value)` — appends `key=value` followed by a single `\n` to
the file at `path`, creating the file if it does not exist.

## Task: implement the missing operations

1. `Get(path, key)` → string — the current (most recently appended) value
   for `key`. Returns the empty string if the file does not exist or the key
   never appears.
2. `CountKeys(path)` → integer — the number of **distinct** keys in the
   journal. Returns `0` if the file does not exist.
3. `Compact(path)` — rewrites the journal so that each distinct key appears
   exactly once, carrying its current value, with keys ordered by **first
   appearance** in the original journal. Each line keeps the `key=value`
   format with a trailing `\n`. If the file does not exist, `Compact` does
   nothing — in particular it must **not** create the file. Compacting an
   already-compact journal leaves its contents unchanged.

## Constraints

- Reads and writes go through the real filesystem (no in-memory fakes, no
  caching between calls).
- All operations must correctly declare whatever your language requires for
  filesystem access.
- Do not change `Set`'s observable behavior.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `Set(string, string, string)`, `Get(string, string) → string`,
`CountKeys(string) → int`, `Compact(string)`, reachable through the arm's
`TestShim.cs` (provided by the harness; not editable by the agent).
