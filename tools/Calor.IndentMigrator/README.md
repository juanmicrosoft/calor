# Calor.IndentMigrator

Phase 4 bulk migration tool. Round-trips `.calr` files through the
migration `CalorEmitter` to convert legacy closer-form blocks
(`§/F{id}`, `§/M{id}`, …) into indent-only form.

## Usage

```bash
# Migrate a single file
dotnet run --project tools/Calor.IndentMigrator -- path/to/file.calr

# Migrate everything under a directory (recursive, skips bin/ and obj/)
dotnet run --project tools/Calor.IndentMigrator -- samples/

# Dry-run (show what would change)
dotnet run --project tools/Calor.IndentMigrator -- samples/ --dry-run --verbose

# Skip files known to be incompatible with the migrator
dotnet run --project tools/Calor.IndentMigrator -- tests/ \
  --exclude tests/E2E/scenarios/09_codegen_bugfixes/input.calr
```

## How it works

1. Lex with `Lexer.TokenizeAllForParser()` (indent-aware production path).
2. Parse to AST.
3. Re-emit through `Migration.CalorEmitter` (which now emits indent form).
4. Write the result back in place — unless the round-tripped output equals
   the input byte-for-byte (after trim), in which case the file is reported
   as `unchanged`.

Files that fail to lex or parse are reported as `skipped` with the first
diagnostic message; the process continues. Exit code is `1` if any files
were skipped, `0` otherwise.

## Known exclusion: 09_codegen_bugfixes

`tests/E2E/scenarios/09_codegen_bugfixes/input.calr` is intentionally kept
in closer form. It pins the codegen path for inline `§NEW ... §A initializer`
arguments to method calls — the migration emitter hoists those into temp
`§B{~_hoist000}` bindings, which is a semantic reshape (not pure
reformatting) and would defeat the regression coverage. Fix the hoist
behavior first; the fixture can migrate after.

## Flags

| Flag | Effect |
|---|---|
| `--dry-run` | Don't write back; only report what would change. |
| `--verbose`, `-v` | Print every rewrite / unchanged decision. |
| `--exclude <path>` | Skip files whose path ends with the given fragment. Repeatable. |
