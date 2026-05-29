---
layout: default
title: calor fix
parent: CLI Reference
nav_order: 11
---

# calor fix

`calor fix` applies **bulk, mechanically safe, byte-reversible** rewrites
across every `.calr` file under a root directory.

Each operation writes a `migration.log.json` that records the exact
bytes it removed. Passing the log back with `--revert` restores the
original file contents byte-for-byte.

---

## Subcommands

| Flag | What it does |
|:-----|:-------------|
| `--drop-structural-ids` | Strip the `{id}` block from structural closing tags (`§/M{…}` → `§/M`, etc.). |
| `--revert` | Reverse a prior `--drop-structural-ids` operation using `--log`. |
| `--log <file>` | Write (without `--revert`) or read (with `--revert`) the migration log. |
| `--dry-run`, `-n` | Report what would change without writing files. |

---

## `--drop-structural-ids`

Structural closing-tag IDs (`§/M{m001}`, `§/F{f001}`, `§/L{for1}`,
`§/I{if1}`, …) have been **optional** since v6+. Source written before
that release still carries them. This subcommand removes them in bulk,
leaving the openers (where IDs continue to live) untouched.

```bash
calor fix --drop-structural-ids src/                # rewrite in place
calor fix --drop-structural-ids src/ --dry-run      # report only
calor fix --drop-structural-ids src/ \
    --log .calor/migration.log.json                 # also write a log
```

### What it rewrites

The migrator targets only the recognized closing-tag shape and only
when the contents inside `{…}` are an ID-shaped token (ULID-style,
short test ID like `f001`, or compact). It leaves everything else
alone — closing tags without an ID block are skipped (idempotent), and
opening tags are never touched.

### Output

```
drop-structural-ids: files_changed=42 removals=178
wrote .calor/migration.log.json
```

Exit codes: `0` on success, `2` on usage error.

---

## Reverting

```bash
calor fix --drop-structural-ids src/ \
    --revert --log .calor/migration.log.json
```

The revert re-inserts the removed byte ranges at their original
offsets. The result is **byte-identical** to the pre-rewrite source —
the `BytePreservationVerifier` covering the migrator's tests asserts
exactly this property on every change.

---

## Why use it

The closing-tag ID was redundant: the parser already knows which
opener the closer is paired with via structural nesting. Dropping it
shrinks files, removes a class of "I changed the opener ID but forgot
the closer" bugs, and makes diffs smaller when IDs do change.

If a closing-tag ID is left in source, the opt-in lint
[`Calor0820 LegacyStructuralId`](/calor/ids/#9-diagnostics) flags it
and emits a `fix` patch that points back at this command.

---

## See also

- [ID Specification](/calor/ids/) — full rules for opener IDs.
- [`calor ids`](/calor/cli/ids/) — check, assign, and re-index opener IDs.
- [`calor format`](/calor/cli/format/) — canonicalize whitespace, attribute order, etc.
