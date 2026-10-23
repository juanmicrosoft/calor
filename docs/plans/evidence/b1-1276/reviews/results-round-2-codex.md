# B1 #1276 results — review round 2 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, same hostile scope as round 1 plus a check of each round-1 finding. Input: `git diff origin/main...HEAD` at `6c2fa1af`, with the raw oracle outputs and pair manifest read from the working tree. Local paths in links are the review worktree.

## Codex output (verbatim)

Round-1 findings:

1. **RESOLVED — Reconciliation and oracle identity.** The validator now reads both raw arrays, derives cross-run reconciliation, and checks oracle identifiers against registration. Evidence: [BenchmarkResultsValidator.cs:81](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:81).

2. **RESOLVED — Metric provenance and values.** Both metric headers are checked; category, metric, sizes, ratio, and ratio bits are recomputed. The generator also checks the oracle hash and generator identity. Evidence: [BenchmarkResultsValidator.cs:151](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:151), [PairResultsCommand.cs:118](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Evaluation/Equivalence/PairResultsCommand.cs:118).

3. **RESOLVED — Non-discriminating negative controls.** The metric inclusion control changes both runs, and every mutation asserts its specific code and subject. Removing the targeted guard can no longer be masked by another subject. Evidence: [BenchmarkResultsTests.cs:39](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsTests.cs:39), [BenchmarkResultsTests.cs:104](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsTests.cs:104).

Remaining defects:

1. **BLOCKING — Pair-level equivalence evidence can be removed or falsified without rejection.** R003 checks only the evidence’s `reason`; R002 explicitly exempts the entire evidence object. Replacing HelloWorld’s `equivalenceEvidence` with `{"reason":"AGREE"}`, updating the manifest comparability hash, and resealing passes all applicable predicates. The included pair then lacks its oracle reference, input count, observation hash, and surface. Arbitrary replacements for those fields likewise go unchecked. This leaves the §5/§10 B1 evidence requirement unenforced. Compare every copied evidence field with the reconciled verdict and require the oracle reference. Evidence: [BenchmarkResultsValidator.cs:62](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:62), [BenchmarkResultsValidator.cs:105](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:105).

2. **BLOCKING — Registered population and interval interpretation are unchecked.** The validator verifies numeric aggregates but never checks `metric.population` or `metric.intervalLabel`. A resealed packet can replace them with “all C# and Calor programs” and “95% confidence interval for a language productivity advantage” while preserving every checked value. It therefore accepts generalization and an interval interpretation explicitly prohibited by registration and §5. Require the registered population and interval labels. Evidence: [BenchmarkResultsValidator.cs:178](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:178).

3. **NON-BLOCKING — The validator permits deletion of the metric execution provenance.** `environment.json` is neither required nor parsed. Removing it and its seal passes validation, although the metric outputs themselves contain no execution commit or command record. This removes the packet’s evidence for the registered same-commit run rule. Require the environment record and validate its execution identities. Evidence: [BenchmarkResultsValidator.cs:21](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a95998bbef0abe8ae/tests/Calor.Compiler.Tests/EvidenceContract/BenchmarkResultsValidator.cs:21).

The committed data independently reproduces: all 226 pairs and registered fields, seals, reconciliation, metric values, and bootstrap endpoints. The counterexamples above follow from source inspection; .NET tests could not start because the read-only sandbox denied MSBuild temporary-directory creation.

VERDICT: NOT CLEAN

## Disposition

All three round-1 findings were confirmed resolved. The reviewer again reproduced the committed data independently; the new findings are validator gaps, fixed in the commit that adds this record. No committed result changed.

1. **Accepted.** R003 now requires each pair's whole `equivalenceEvidence` to equal the object derived from its reconciled verdict: status, `oracle` (`b1-1276-pair-oracle@1` from `registration.json`), the committed run-2 hash, reason, input count, observation hash, surface, witnesses, and detail. New controls: "stripped pair evidence", "falsified input count".
2. **Accepted.** R005 now requires the registered wording for the metric name, per-pair value, population, sampling unit (`registration.json` `statistics.samplingUnit`), interval label (`statistics.interval.label`), and the results claim label. New controls: "generalized population", "interval relabeled as a confidence interval".
3. **Accepted (NON-BLOCKING).** `environment.json` is now a required, sealed results file, and R003 checks that it records both oracle runs at the merge commit with exit codes 4 and 0, and two metric runs at one 40-hex commit with exit codes 0 and bit-identical outputs. New controls: "metric runs not recorded", "missing environment record".

Mutation check: disabling the evidence, labels, or environment guard makes its controls fail.
