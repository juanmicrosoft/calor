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
| `run3.py` | The round-2 adversarial cases (Z-series) | `Z1`–`Z11`: the line rule's second branch at the four positions with no `§E` arm, wrapped parameter lists, `eff`-name collisions with the effect registry, `§DEL`/`§LAM` type-parameter lists, class/interface type params reaching members, rows on non-function-typed positions, `§E` inside a call argument list, the fallible-type suffix's real output |
| `run2.py` | Discriminating cases | `Y1a/b/c` (which `§E` wins in the `§F` section loop), `Y2` (fallible brace form), `Y3` (`§B` suffix), `Y4`/`Y5` (`§O` and arrow suffixes), `Y6` (`§MT`), `Y7` (Calor0421), `Y8a/b` (`--permissive-effects` demotes Calor0420), `Y9` (`§B{f} §LAM` under permissive) |
| `facts.py`, `facts2.py` | Re-measure every number the three review lenses disputed | `BindStatementNode`, `eng/ast-schema.json`, the fourth `IsSubsetOf` site, `ProjectIndex` references, `Effects/*.cs` count, function-typed corpus positions, conversion-test pipeline, pin line numbers, ledger exact-equality, `BoundTypeTests` `DisplayString` pins, `calor-direction.md` lines, the roadmap's gate-1 enumeration |
| `compile53.py` | Compiles every committed `.calr` holding a two-line `§O`/`§E` pair — the form Decision 1 must not disturb — and records the verdict per file | writes `o53/baseline.json` |

Run a subset by name: `python3 run.py X3 Y1` (substring match on the case name).

## Committed results

`o53/baseline.json` — a ledger in the shape the design doc demands of its own ledgers
(`schemaVersion`, `measuredCommit`, `scope`): the 23 files, 54 occurrences, and each file's exit
code and diagnostic codes. **22 of the 23 are already compile-red for reasons unrelated to
effects** — **18** `bench/mcp/tasks/*` on Calor0830 legacy closers, **3** `benchmarks/security/*`
on the #901 stale subjects, **1** lint error fixture — and exactly one is green. The E2 PR
re-runs this and the diff is gate 5's evidence.

`transcripts/` — the canonical stdout of each script. **These are the pinned artifact.**

Generated `.calr` and `.g.cs` files are written next to the scripts and are gitignored.

## This is a test, not a convenience

`tests/Calor.Compiler.Tests/Effects/EffectRowExperimentHarnessTests.cs` re-runs all six scripts
and diffs against `transcripts/` (design-doc pin **P29**), and shape-checks `o53/baseline.json`
(**P30**). It never skips: a missing compiler build is a hard failure.

If the compiler changes, the test goes red naming the script and the first differing line.
Then:

```bash
python3 docs/design/spikes/effect-rows/experiments/regenerate-transcripts.py
```

review the diff, **update the design doc's quoted output**, and commit both in the same PR —
the same discipline as `CALOR_REGENERATE_S5_LEDGER` for the metadata ledger.
