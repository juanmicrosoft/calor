---
layout: default
title: Stable Identifiers
parent: Philosophy
nav_order: 3
---

# Stable Identifiers: Tooling-Owned Identity

Calor separates a declaration's **name** from its **identity**. Names belong in
source. Persistent identities belong in the tooling-maintained per-project
sidecar, `.calor/ids.json`.

```calor
§M{Calculator}
  §F{Calculate:pub} (i32:x) -> i32
    §E{}
    §R (* x 2)
```

There is no optional ID-first source form. Agents edit the program, not identity
fields. Build and project tools synchronize active compilation inputs
automatically. Commit the sidecar alongside the source.

## Why names and locations are not enough

| Reference | Failure mode |
|:----------|:-------------|
| File and line | Earlier edits move the line |
| Declaration name | Renames and overloads make it stale or ambiguous |
| Content hash | A body edit changes it |
| Preserved declaration ID | Identifies the same declaration after a supported rename, move, or body edit |

These IDs let navigation, refactoring, diagnostics, and verification refer to
the same declaration without adding bookkeeping to every source header.

## Preserve identity; do not guess it

| Operation | Identity behavior |
|:----------|:------------------|
| Reformat or change a body | Preserve the declaration's identity |
| Rename or move through a supported refactoring tool | Carry the exact identity with the edit |
| Add, copy, or extract a declaration | Create a new identity |
| Make plain-text edits | Synchronize and preserve only unambiguous matches |
| Make several indistinguishable changes | Refuse reconciliation until the ambiguity is resolved |

For example, two renamed overloads can look equally plausible to a matcher.
Choosing whichever appears first would silently redirect old references.
Calor rejects that ambiguity instead. Transactional refactoring can supply an
explicit old-to-new mapping when plain-text reconciliation cannot.

In-memory parsing does not mint persisted project identities. Snapshot-local
handles for unsaved declarations, locals, or lambdas are not durable sidecar IDs.
Do not store them as long-lived external references.

## Transactions and merges

Identity-aware writes journal the source and sidecar update together. After a
crash, synchronization can recover the pending transaction. If an external edit
conflicts with recovery, it stops instead of overwriting that edit.

Independent branch additions can merge without replacing existing identities.
Conflicting edits to one identity are not resolved by assigning new IDs or
choosing one side arbitrarily. Review and resolve the conflict explicitly.

LSP rename returns source and sidecar changes in one client-applied
`WorkspaceEdit`. It requires transactional text-edit support and saved source
buffers. This is not a filesystem compare-and-swap guarantee against unrelated
writers.

## Inspect or recover a project

```bash
calor ids sync .
calor ids check .
calor ids index src/Calculator.calr --output declaration-index.json
```

Directory arguments operate on registered inputs, not every `.calr` file in the
repository. Historical benchmark and experiment originals remain separate from
active compilation inputs and retain their original bytes.

See the [`ids` command reference](../ids.md) for explicit registration,
validation, and index export. The source-rewriting `ids assign` command is
removed.
