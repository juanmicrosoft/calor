# B1 #1276 results — review round 3 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, same hostile scope plus a check of each round-2 finding. Input: `git diff origin/main...HEAD` at `b37487a8`, with the raw oracle outputs and pair manifest read from the working tree. Run after a reviewer usage-limit wait (no credits bought). Local paths in links are the review worktree.

## Codex output (verbatim)

Round-2 findings:

1. **RESOLVED — Pair evidence copying.** The validator compares the complete evidence object with the reconciled verdict and requires the registered oracle reference. Evidence: [BenchmarkResultsValidator.cs:116](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:116).
2. **RESOLVED — Population and interval interpretation.** Both labels, the sampling unit, and the claim label are now checked. Evidence: [BenchmarkResultsValidator.cs:207](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:207).
3. **NOT RESOLVED — Execution provenance, partially fixed.** The environment file is required, but its execution identities remain insufficiently validated; see finding 2 below.

Remaining defects:

1. **BLOCKING — Impossible raw equivalence evidence passes when copied consistently.** Set HelloWorld’s `Surface=[]`, `InputCount=0`, and `ObservationsSha256=null` in both runs’ raw and reconciled arrays; copy those values into its manifest evidence and update hashes/seals. Every validator predicate still passes, and HelloWorld remains included in the metric. Reconciliation checks agreement between supplied objects, while the evidence guard merely copies their fields: [BenchmarkResultsValidator.cs:96](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:96), [BenchmarkResultsValidator.cs:122](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:122). The registered oracle cannot produce this `AGREE` verdict: an empty surface produces `SURFACE_EMPTY`, and successful comparison records a transcript hash. Evidence: [PairDifferentialOracle.cs:99](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Evaluation/Equivalence/PairDifferentialOracle.cs:99), [PairDifferentialOracle.cs:127](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Evaluation/Equivalence/PairDifferentialOracle.cs:127). Require reason-specific raw-verdict invariants.

2. **NON-BLOCKING — A provenance shell replaces the execution record successfully.** Remove machine details, commands, output identities, logs, and aggregation provenance; retain the checked exit codes and flags, and set the metric commit to 40 `z` characters. Resealing still passes. The commit check tests length alone, despite the claimed “40-hex” fix, and no check binds recorded commands or outputs to the runs. Evidence: [BenchmarkResultsValidator.cs:134](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:134).

3. **NON-BLOCKING — Published summary facts can be falsified without rejection.** A resealed `results.json` can report `byDispositionAndReason={"EQUIVALENT AGREE":226}`, 226 included TokenEconomics pairs, an EQUIVALENT known witness, and one oracle/metric run. These fields are never checked; only `byDisposition` and `denominator` are recomputed before validating metric labels and estimates. Evidence: [BenchmarkResultsValidator.cs:202](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:202). Derive and validate these summaries from the manifest and runs.

These bypasses follow from source inspection and in-memory predicate checks. The committed data independently reproduces; .NET tests could not start because the read-only sandbox denied MSBuild temporary-directory creation.

VERDICT: NOT CLEAN

## Disposition

Round-2 findings 1 and 2 were confirmed resolved; finding 3 was partly resolved and is covered by new finding 2. As in rounds 1 and 2, the reviewer reproduced the committed data independently. All three new findings are validator gaps, fixed in the commit that adds this record. No committed result changed.

1. **Accepted.** R003 now checks what the registered oracle can emit for each reason, in both raw arrays and the reconciled array. AGREE and OBSERVATION_MISMATCH need a non-empty surface, a positive input count, and a transcript SHA-256. Every other reason has no transcript hash. AGREE has no witnesses and an empty detail. OBSERVATION_MISMATCH has 1 to 5 witnesses. A pre-registered exclusion has no surface and no inputs. New control: "impossible AGREE verdict copied consistently".
2. **Accepted (NON-BLOCKING).** The environment check now requires a 40-hex metric commit, the oracle environment string equal to the runs' own, both oracle commands naming the registration directory and merge commit (run 2 with `--compare-with`), a `pair-metrics` command, and the registered output file names. New control: "non-hex metric commit".
3. **Accepted (NON-BLOCKING).** R005 now derives `generatorVersion`, `registrationPairManifestSha256`, `byDispositionAndReason`, `knownWitness`, `determinism`, and `includedPerCategory` from the manifest, the runs, and the registration. New control: "falsified summary counts".

Mutation check: disabling the verdict-invariant guard, the summary guard, or the hex commit check makes its control fail.

Remaining limitation, stated rather than fixed: a validator over committed files cannot exclude a packet fabricated consistently with every invariant without re-running the oracle. The guard against that is provenance: the oracle refuses to run unless HEAD is the registration merge commit and the checkout is clean. The commands are recorded, and anyone can re-run them at that commit. That re-run takes about 30 minutes, which is too slow for the unit-test lane.
