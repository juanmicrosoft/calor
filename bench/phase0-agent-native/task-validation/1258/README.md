# R8: actual arm-discrimination observations (#1258)

These are local compiler observations, not agent runs or a benefit result.
The three selected workflows represent **one** registered compiler shape:
#1136 row 7, direct invocation of a `this.`-qualified delegate field.

Each task's `evidence/r8/` contains both actual compiler commands, unabridged
stdout/stderr, the identical compiled Calor input, the emitted permissive C#,
and a stated verdict difference.

Both arms use the same actual v0.18.0 Release compiler, source commit
`514f538024df990af86054af25975b756ba42ab1`, DLL SHA-256
`8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0`.
A adds `--permissive-effects`; B does not. Input bytes, compiler, and all
other semantic options are identical. Output file names merely separate the
two observations.

| Task | A: permissive | B: strict |
|---|---|---|
| Quota adapter | exit 0, no diagnostic | exit 1, `Calor0410` unknown |
| Shipping quote | exit 0, no diagnostic | exit 1, `Calor0410` unknown |
| Frame fingerprint | exit 0, no diagnostic | exit 1, `Calor0410` unknown |

B also emits `Calor0411` unknown-call warnings. The disagreement is the
unknown-call policy, not propagation of the dependency's named `mut` row.
The dependency nevertheless declares and performs its real state mutation.
R4/R5 runtime observations are the separate #1257 artifact freeze; this
producer does not claim to rerun those suites.

## Exact input lineage

`run.py` reads the actual SDK-composed Calor input from the #1257 execution,
checks its recorded source hash and equality across both policies, verifies
the frozen product commit/DLL hash, and invokes that compiler twice.
It never hand-translates C# or guesses a source-fragment separator.

The recorded input uses reviewed instrument `40f34dcd`'s exact concatenation.
A changed production assembler must append fresh integration/contrast
evidence with its own actual input hashes; these observations must not be
relabelled as executions of the changed assembler.

Example from the repository root, using the recorded prebuilt product:

```bash
python3 bench/phase0-agent-native/task-validation/1258/run.py \
  --repo-root . \
  --product-root ../ppw-v018 \
  --suite-evidence bench/phase0-agent-native/task-validation/1257/evidence \
  --output .r8-reproduction
```

The output directory must be new and inside the worktree. The script refuses
a changed product or unexpected arm verdict. Different reproduction paths
and execution timestamps are not represented as byte-identical command logs.

This completes R8 observations for the one selected shape, not a claim that
all twelve shapes discriminate or that an experimental agent realizes this
shape. Operational code-only detection, public-API preservation, complete
registration/pins and funding remain separate collection prerequisites.
