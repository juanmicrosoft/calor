# Declaration identity

Current Calor source contains **no declaration or structural IDs**. Tooling stores
persisted declaration identities in `.calor/ids.json`. Commit that file alongside
the source. Agents edit names, signatures, and bodies, not identity records.

```calor
§M{Calculator}
  §F{Double:pub} (i32:value) -> i32
    §R (* value 2)
```

An ID identifies a declaration independently of its source spelling and offsets.
Renaming a declaration can preserve its identity; adding a declaration creates a
new identity. IDs are not references that an agent must copy into source.
In-memory parsing and formatting do not create persisted project identities.

## Synchronization and commands

Build, check, watch, migration, and project indexing synchronize their actual
active inputs automatically. Their compilation or project inputs define the
boundary; synchronization does not discover historical inputs.

For explicit registration and recovery:

```bash
calor ids sync /path/to/project --register /path/to/project/active.calr
calor ids sync /path/to/project
calor ids check /path/to/project/active.calr --format json
calor ids index /path/to/project/active.calr --output declaration-index.json
```

`ids sync` without `--register` reconciles the already registered files.
`ids check` and `ids index` synchronize explicitly passed active files before
reading their effective sidecar IDs. Directory arguments select registered
files within that directory, not every `.calr` file underneath it. Register
new inputs explicitly or use the ordinary build/project input discovery.
Neither command writes IDs into source.

`ids check` validates sidecar identities, including duplicate IDs, kind prefixes,
and supported payload formats. `--allow-test-ids` allows short test identities in
the sidecar. `--format json` returns the shared diagnostic envelope, including
input, parse, and identity failures. Invalid source keeps its lexer/parser
diagnostics; unavailable or ambiguous persisted identity uses `Calor0806`.
An exported index is a report, not another authoritative identity store.
Index output cannot overwrite `.calr` source or files directly inside `.calor`.

The source-rewriting `ids assign` command is removed. Use `ids sync` instead.
MCP `calor_format` formats ID-free source; its former ID assignment action is
refused with synchronization guidance. In-memory source cannot establish or
validate persisted project IDs; use project-backed commands for that work.
Source-ID mismatch diagnostics (`Calor0101`) and legacy source-ID lint diagnostics
(`Calor0820`, `Calor0821`) are retired.

## Ownership and discovery

Building one registered file or adding a migration output in a subdirectory
reuses its owning project sidecar rather than creating a shadow store.
Unregistered inputs do not adopt a store above a nearer project boundary.
Automatic discovery stops at temporary, home, and filesystem roots before
opening any sidecar or lock there. Use a dedicated project subdirectory for
persisted identity, not an ambient root.

Compile-time recovery requires a confirmed registration in the current or
pending sidecar; merely finding an ancestor store never recovers its journal.
Ownership comparisons resolve physical directory aliases. Source registration
and transaction paths reject symbolic links inside the identity project.

Frozen original inputs, historical replay probes, and immutable ledgers retain
their original bytes. They are not active inputs to current identity tooling.
Do not register them merely because they have a `.calr` extension.

## Conservative reconciliation

Synchronization preserves identity for an exact declaration key: file, container,
kind, name, and signature. A vanished declaration can match a new declaration with
the same kind and signature in the same container or file, but only when both
ends have exactly one candidate. Ambiguous changes fail without changing the
sidecar. Refactoring integrations can provide explicit identity relocations,
including moves across both files and containers.

Container renames are reconciled before retrying unique signature edits, so a
class rename preserves a self-typed member such as `Echo(A) -> A`. Multiple
changed overloads remain ambiguous unless tooling supplies an explicit mapping.
Failed parsing or ambiguous reconciliation must not write a partial sidecar.

The shared declaration inventory includes delegates, events, records, unions,
fields, variants, local functions, and proof obligations, including declarations
inside accessor bodies and conditional branches. Unsupported declaration kinds
and indistinguishable unnamed proofs fail explicitly rather than disappearing.
Accessor-local declarations have separate get, set, init, add, and remove scopes.
Named modules use module identities; static classes use class identities.

## Transactions and refactoring

Source edits made through the identity transaction API and MCP file-write path
are journaled with their sidecar update. An interrupted transaction rolls forward
on the next synchronization. Recovery refuses to overwrite an external edit and
keeps its journal for investigation. Independent branch additions can be recovered
from ordinary or diff3 Git conflict markers by `ids sync`; conflicting edits to
one identity are rejected rather than guessed.

MCP `calor_file_write` retains its ordinary `path`/`content` call shape. Its
optional `action` also accepts `rename` and `move`, addressed by `projectDirectory`,
`filePath`, `line`, and `column`. Rename requires `newName`; move requires exact
`changes` snapshots plus `destinationFile`, `destinationLine`, and
`destinationColumn`. Set `preview: true` to validate without applying. Moves
validate parsing and identity, not behavioral equivalence. There is no separate
top-level MCP identity tool. Refactor actions reject stale source or sidecar
snapshots and parameters belonging to a different action.

MCP type rename retains the explicit `TypeReferencesNotIndexed` refusal; LSP type
rename uses its type-aware index. LSP rename returns source and sidecar changes
together in one client-applied `WorkspaceEdit`; server-side preparation writes
neither. Sidecar-backed LSP rename requires transactional text-edit support from
the client and refuses unsaved source buffers that differ from disk.
Transactional clients apply saved source and sidecar edits together; this does
not add a filesystem compare-and-swap guarantee against noncooperating writers.
