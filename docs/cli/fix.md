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

Known missing effects can offer a native `!` row edit for manual review.
These edits change a declaration's contract, so safe repair never applies them.
Unknown call effects offer no row edit: resolve the call's effect contract first.

`Calor1305` offers a conditional return for two branches returning matching
primitive literals. Safe repair checks the declared return type, leaves comments
and nonliteral branches alone, and retains blank lines to preserve later source
mappings. Whitespace edits are applied before an overlapping structural patch.
Both steps recompile and must retain identical generated C#.
`Calor1306` (an `if` chain that `match` states more briefly) has no patch,
because the match lowers to a C# `switch`; rewrite it by hand.

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

The archived section-tag migrations (`--heal-closers`, `--elide-call-closers`,
`--revert`, `--log`) were removed in 0.30 with the `§` syntax, as were
`--drop-structural-ids` and `--compact-ids`. They are not compatibility options:
passing one is a usage error. The original 23 elision and healing tests still run
with their recorded compiler as a historical replay:

```bash
python3 scripts/registered_ledger_historical_replay.py archived-closers
```

This replay builds compiler `d82f3ae95175ecd09581c6922701706be72b0ca9` from Git
history and requires .NET 10 and verified Z3 assets. It does not enable legacy
syntax in the current compiler.

| Option | Meaning |
|:-------|:--------|
| `--apply` | Apply the safe patches (required). |
| `--dry-run`, `-n` | Report changes without rewriting source files. |
| `--format`, `-f` | `text` (default) or `json` (envelope schema 2.0). |

Exit codes are `0` for success, `1` for remaining errors or failed writes, and
`2` when `--apply` is missing.

See [`calor format`](/calor/cli/format/) for canonical formatting and
[`calor ids`](/calor/cli/ids/) for declaration ID management.
