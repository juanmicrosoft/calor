---
layout: default
title: calor fix
parent: CLI Reference
nav_order: 11
---

# calor fix

`calor fix` applies mechanical, byte-reversible rewrites to `.calr` files
under a root directory. Select exactly one operation:

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
| `--format`, `-f` | `text` (default) or `json` (envelope v1.1). |

The log records exact removed bytes. Reversion restores those bytes, not a
reformatted approximation. JSON mode emits one envelope with operation counts
and diagnostics. Exit codes are `0` for success and `2` for usage errors.

See [`calor format`](/calor/cli/format/) for canonical formatting and
[`calor ids`](/calor/cli/ids/) for declaration ID management.
