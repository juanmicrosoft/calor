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
