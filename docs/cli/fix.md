---
layout: default
title: calor fix
parent: CLI Reference
nav_order: 11
---

# calor fix

`calor fix --apply` applies deterministic safe diagnostic patches to a `.calr`
file or directory (current directory by default). It rechecks after each patch
batch, up to three batches, and reports remaining diagnostics with a nonzero exit
when errors remain. It does not choose types, effect contracts, fuzzy name matches
or ambiguous control flow for you.

```bash
calor fix --apply
calor fix src/Broken.calr --apply --dry-run --format json
calor fix src/ --apply
```

Edits use 1-based, end-exclusive UTF-16 line/column spans. Invalid spans,
overlapping edits and cross-file patches are rejected before applying a batch.
Writes use the same atomic, stale-source-checked path as formatting and preserve
the source encoding, BOM and permissions. Files are replaced individually, not
as a directory-wide transaction. A dry run leaves files unchanged and returns
their original diagnostics, including fix payloads, with proposed change counts.
`editsApplied` counts individual text edits, not diagnostics.

The explicit migration modes below instead perform byte-reversible rewrites.
Select exactly one operation; they cannot be combined with `--apply`:

| Flag | Operation |
|:-----|:----------|
| `--heal-closers` | Remove legacy structural closers so blocks end by dedenting. |
| `--elide-call-closers` | Elide optional `§/C` on supported same-line calls. |

`--drop-structural-ids` and `--compact-ids` have been removed. They are not
compatibility options.

```bash
calor fix src/ --heal-closers --dry-run
calor fix src/ --heal-closers --log migration.log.json
calor fix src/ --heal-closers --revert --log migration.log.json
```

| Option | Meaning |
|:-------|:--------|
| `--log <file>` | Write a migration log, or read it with `--revert`. |
| `--revert` | Restore the removed byte ranges from an existing log. |
| `--dry-run`, `-n` | Report changes without rewriting source files. |
| `--format`, `-f` | `text` (default) or `json` (envelope schema 2.0). |

The log records exact removed bytes. Reversion restores those bytes, not a
reformatted approximation. JSON mode emits one envelope with operation counts
and diagnostics. Exit codes are `0` for success, `1` for remaining errors or
failed diagnostic writes, and `2` for incompatible operation options.

See [`calor format`](/calor/cli/format/) for canonical formatting and
[`calor ids`](/calor/cli/ids/) for declaration ID management.
