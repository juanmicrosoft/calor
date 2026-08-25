# Executed experiments — effect rows, Draft v2

Every claim in `docs/design/effect-rows-in-the-type-system.md` about **how the compiler behaves
today** is produced by one of these scripts, not by reading source. Draft v1 reasoned from
`CLAUDE.md` and the parser source and was wrong in at least four places (the Calor0410 message
text, whether `§FLD … §E` parses, how many `§I`/`§O` dispatch arms exist, and whether `!e`
survives the attribute round-trip). v2 runs everything.

## Prerequisites

```bash
src/Calor.Compiler/scripts/download-z3.sh    # once per clone
dotnet build src/Calor.Compiler              # produces bin/Debug/net10.0/calor.dll
```

The scripts locate the repository root relative to their own path, so they run from anywhere.

## The scripts

| Script | What it does | Cases |
|---|---|---|
| `run.py` | Writes a `.calr` per case, compiles it, prints verbatim output | `X1`–`X14`: the `§O`/`§E` two-line and same-line forms, the `!e` sigil, the `T!E` fallible suffix, `^e`, a bare identifier, `eff` in the type-parameter list, `§LAM`/`§DEL`/`§I`/`§FLD`/inline-parameter rows, the Calor0418 and Calor0410 baselines, the silent-laundering baseline |
| `run2.py` | Discriminating cases | `Y1a/b/c` (which `§E` wins in the `§F` section loop), `Y2` (fallible brace form), `Y3` (`§B` suffix), `Y4`/`Y5` (`§O` and arrow suffixes), `Y6` (`§MT`), `Y7` (Calor0421), `Y8a/b` (`--permissive-effects` demotes Calor0420), `Y9` (`§B{f} §LAM` under permissive) |
| `facts.py`, `facts2.py` | Re-measure every number the three review lenses disputed | `BindStatementNode`, `eng/ast-schema.json`, the fourth `IsSubsetOf` site, `ProjectIndex` references, `Effects/*.cs` count, function-typed corpus positions, conversion-test pipeline, pin line numbers, ledger exact-equality, `BoundTypeTests` `DisplayString` pins, `calor-direction.md` lines, the roadmap's gate-1 enumeration |
| `compile53.py` | Compiles every committed `.calr` holding a two-line `§O`/`§E` pair — the form Decision 1 must not disturb — and records the verdict per file | writes `o53/baseline.json` |

Run a subset by name: `python3 run.py X3 Y1` (substring match on the case name).

## Committed results

`o53/baseline.json` — the 23 files, 54 occurrences, and each file's exit code and diagnostic
codes at `82338e37`. **22 of the 23 are already compile-red for reasons unrelated to effects**
(15 `bench/mcp/tasks/*` on Calor0830 legacy closers, 3 `benchmarks/security/*` on the #901 stale
subjects, 1 lint error fixture); exactly one is green. The E2 PR re-runs this and the diff is
gate 5's evidence.

Generated `.calr` and `.g.cs` files are written next to the scripts and are gitignored.
