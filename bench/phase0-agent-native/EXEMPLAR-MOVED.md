# The syntax exemplar moved

The live agent-syntax exemplar (E1a experiment's `--exemplar` input) is now the
canonical single source at:

    src/Calor.Compiler/Resources/agent-syntax-exemplar.md

It is embedded in the compiler, served as the `calor://primer` MCP resource, and
drift-checked by `calor self-check docs`. To reproduce E1a, pass that path:

    ./run-pair.sh --pair pairs/<id> --arm calor \
        --exemplar ../../src/Calor.Compiler/Resources/agent-syntax-exemplar.md

The **as-run** sheet from the original E1a run is frozen (sha-pinned in pins.json)
at `epochs/e1a-attribution/exemplar-as-run.md` and is not affected by this move.

## E1a reproduction & the pins path

`epochs/e1a-attribution/run-driver.sh` now passes the co-located, sha-pinned
`exemplar-as-run.md` (the exact sheet E1a ran, in the same directory) — so the
epoch reproduces self-contained against its own frozen input, independent of the
live exemplar's evolution. Note: `pins.json` still records the original
`exemplarFile: "bench/phase0-agent-native/exemplar.md"` path (that file moved to
`src/Calor.Compiler/Resources/agent-syntax-exemplar.md`); the pinned **sha256**
matches `exemplar-as-run.md` in this directory, which is the authoritative
as-run artifact.
