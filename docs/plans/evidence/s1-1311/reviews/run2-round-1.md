# S1 run 2 review — round 1

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (prompt + diff on stdin)
- Reviewed: eeed7a1b (diff e7cbce77..HEAD for the harness; d1e476b5..HEAD for packet and doc)

## Reviewer output (verbatim)

No BLOCKING or SHOULD-FIX findings. One NIT:

- **NIT — Historical gate citation points to the current status.** [v0.24-s1-soundness-results.md:118](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a15b19a09073e2529/docs/plans/v0.24-s1-soundness-results.md:118) cites `gate-status.json` for run 1’s **BLOCKED** status, but that file now says **MET**. Add an annotation pointing to the historical version at `d1e476b5`, preserving the quoted run-1 report.

The substantive checks pass:

- **Conditions 1–4:** Registration, generator, oracle, controls, and build settings are unchanged. Every registered case executed in order. Baseline pins match run 1. All six moved harness files have identical Git blobs. Removing the capture calls and reverting `1508` to `1500` makes `Program.cs` byte-identical to `e7cbce77`; the committed harness patch matches Git’s actual diff.
- **Capture:** The code enumerates the calling process’s dyld images. All ten records identify the expected baseline paths and registered hash, and their timestamps bracket the corresponding executions. The fixed callers supply all required hosts; the check requires exactly one matching image per host, so missing images cannot pass vacuously.
- **Conditions 5–7:** There are exactly **1,508 ledger entries and 1,508 attempt files**, with no duplicates, missing files, or unledgered attempts: B1 **744**, N1 **744**, P845 **20**. The records show one complete rerun. The requested run-1 raw-results diff is empty; its original packet and historical report remain preserved.
- **Condition 8:** Independent comparisons confirm identical tokens, per-attempt tokens, classes, added findings, O1 verdicts, and substantive finding records. The 56 previously executed reciprocal reruns agree; all eight previously missing reruns reproduce. The sole solver-model difference changes neither token, class, nor finding, so its observation-only classification is correct.
- **Conditions 9–10:** Execution timestamps meet the deadline. Reported resource estimates remain within the other ceilings; agent-hours are self-reported. Both O2 partial cases remain explicitly undecided.
- **Aggregation and numbers:** No pooling or missingness laundering found. Combined statuses match the raw per-baseline statuses. Execution, row, finding, control, O2, and witness counts reconcile. All **3,612** manifest hashes match, with complete packet/raw-file coverage. **MET** follows the contract’s S1 closure criteria and is plainly distinguished from verifier soundness.

APPROVE

## Disposition

- NIT (historical gate citation): fixed in 84dd314c. Run 1's gate status and report are now kept verbatim from d1e476b5 as `docs/plans/evidence/s1-1311/run1-gate-status.json` and `run1-report.md`; the results doc and `gate-status.json` point to them.
- Also changed after this round (reviewed in round 2): the results doc was compacted to fit the pr-size ceiling (1,491 changed lines outside evidence data, ceiling 1,500); `scripts/test_supply_chain.py` exempts the frozen harness from the lock-file rule, which failed CI (`calor-first-guard` job) once the harness moved under `bench/`.
