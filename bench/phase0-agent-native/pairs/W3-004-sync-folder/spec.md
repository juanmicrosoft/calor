# W3-004 — Folder Sync (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Data model

An **index file** describes the contents of one folder. It is a plain-text
file of `name|stamp` lines, one entry per line, each line terminated by a
single `\n`. Names are non-empty and contain neither `|` nor newlines; stamps
are non-negative decimal integers (modification times). **Invariant: each
name appears on at most one line.** Every operation below must preserve this
invariant and the line format. A missing file denotes an empty index.

A **tombstone file** uses the same `name|stamp` format (same invariant); an
entry means "`name` was deleted at time `stamp`".

## Existing behavior (already implemented in the starting fixture)

One-way synchronization from a source index to a target index:

- `IndexHas(path, name)` → boolean — whether the index at `path` exists and
  contains an entry for `name`.
- `IndexStamp(path, name)` → integer — the stamp of `name`'s entry, or `-1`
  if the file does not exist or `name` is absent.
- `WriteIndexEntry(path, name, stamp)` — upserts `name|stamp`: creates the
  file if missing; replaces an existing entry **in place** (same position,
  all other lines preserved unchanged, in order); otherwise appends as a new
  last line.
- `RemoveIndexEntry(path, name)` — removes `name`'s line, preserving all
  other lines in order. Missing file: does nothing. Removing the last entry
  leaves an existing file with empty content.
- `CopyNewer(sourcePath, targetPath)` → integer — for each source entry in
  file order: if its stamp is strictly greater than the target's stamp for
  that name (absent = `-1`), upserts it into the target. Returns the number
  of entries written. Equal stamps are never copied.
- `PruneOrphans(sourcePath, targetPath)` → integer — removes every target
  entry whose name is absent from the source (missing source = prune all),
  in target file order. Returns the number removed.
- `DryRunReport(sourcePath, targetPath)` → string — a report of what a
  `Sync` would do, **without writing anything**: one line `copy <name>` for
  each entry `CopyNewer` would write (source order), then one line
  `prune <name>` for each entry `PruneOrphans` would remove (target order),
  each terminated by `\n`. Returns the empty string when nothing would
  change.
- `Sync(sourcePath, targetPath)` → integer — `CopyNewer` then
  `PruneOrphans`; returns the sum of both counts.

## Task: implement the two-way conflict policy

1. `RecordDelete(tombstonePath, name, stamp)` — records a deletion. If the
   tombstone file already holds an entry for `name` with a stamp **greater
   than or equal to** `stamp`, the file is left unchanged. Otherwise the
   entry is upserted (same replace-in-place / append rules as
   `WriteIndexEntry`).
2. `SyncTwoWay(sourcePath, targetPath, tombstonePath)` → integer — two-way,
   newer-wins synchronization with tombstoned deletions. For a name, let `s`
   and `t` be its stamps in source and target (`-1` when absent), `d` its
   tombstone stamp (`-1` when absent), and `m = max(s, t)`:
   - If `d >= m`: the entry is **deleted** — its line is removed from each
     index where it is present, and each removal counts as one action.
   - Otherwise, if `s > t`: the entry is upserted into the target (one
     action).
   - Otherwise, if `t > s`: the entry is upserted into the source (one
     action).
   - Otherwise (`s == t`, present on both sides): no action.

   Processing order: every name in the source index, in source file order;
   then every name present only in the target **as of the start of the
   operation**, in target file order. The tombstone file is never modified.
   Returns the total number of actions.
3. `SyncWithPolicy(sourcePath, targetPath, tombstonePath, twoWay)` →
   integer — when `twoWay` is true, behaves exactly like `SyncTwoWay`. When
   `twoWay` is false, behaves **exactly** like `Sync`: one-way, and the
   tombstone file is completely ignored (entries are copied and pruned
   exactly as `Sync` would, even when a tombstone exists for them).

## Constraints

- The observable behavior of every existing operation must not change; the
  new operations must preserve the index-file invariants stated above (the
  existing operations rely on them).
- Reads and writes go through the real filesystem (no in-memory fakes, no
  caching between calls).
- All operations must correctly declare whatever your language requires for
  filesystem access.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `IndexHas(string, string) → bool`,
`IndexStamp(string, string) → int`, `WriteIndexEntry(string, string, int)`,
`RemoveIndexEntry(string, string)`, `CopyNewer(string, string) → int`,
`PruneOrphans(string, string) → int`, `DryRunReport(string, string) → string`,
`Sync(string, string) → int`, `RecordDelete(string, string, int)`,
`SyncTwoWay(string, string, string) → int`,
`SyncWithPolicy(string, string, string, bool) → int`, reachable through the
arm's `TestShim.cs` (provided by the harness; not editable by the agent).
