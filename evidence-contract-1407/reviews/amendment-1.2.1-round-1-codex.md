# Amendment 1.2.1, S1 run-2 harness ceiling constant — round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...aabfa0a4` (PR #1488). The prompt asked for a hostile check of five things:

1. Only the condition-4 harness-constant permission changed in substance.
2. Raising `ExecutionCeiling` to 1508 permits no execution beyond the approved 1,508 and reallocates nothing 1.2.0 did not cover.
3. The inspection statement, justification, and 1,444 / 64 / 1,508 arithmetic are honest.
4. The validator binds the new text by SHA-256, and the negative controls fail on any other text, including the 1.2.0 text.
5. Versions, the amendment log row, `sha256.json`, and the test-manifest delta are consistent.

## Codex output (verbatim)

No BLOCKING findings.

- **NIT — `docs/plans/v0.24-evidence-contract.md:788` and `contract.json`, amendment justification:** “A conforming run 2 needs the same 1,508.” That assumes identical retry and finding counts. Say “1,508 accommodates the run-1 workload.” Likewise, “caps … at exactly that budget” should say “at most.” This does not expand authorization.

- **NON-BLOCKING — `eng/test-manifest.json:8`:** `"expectedTotal": 12441` correctly represents **12,434 + 7** against the PR’s merge base: two facts and five theory cases. Current `origin/main` has advanced to 12,511; preserve the +7 when resolving the integration conflict.

The requested checks otherwise pass:

1. **Substance:** Against `origin/main`, the JSON changes only condition 4, version metadata, and the appended amendment entry. Exception scope, justification, value **3008**, addedExecutions **1508**, conditions 1–3 and 5–10, all ceiling values, and other rules are unchanged. Markdown agrees.
2. **Capacity:** The ordinary sweep remains bounded by **1,426 allocated executions + 74 reserve = 1,500**. Raising the total constant does not enlarge the retry reserve; crossrun can consume the additional eight slots, stopping at 1,508. Condition 5 still charges retries and invalid-run restarts. “Adds no execution beyond the 1,508 already approved” is honest for conforming work.
3. **Arithmetic and inspection:** **1,444 = 1,426 + 14 P845 availability + 4 retries**; **64 = 56 executed + 8 stopped**; **1,444 + 64 = 1,508**. The inspection disclosure acknowledges prior result inspection and matches 1.2.0’s disclosure; personal inspection itself is not independently provable from these files.
4. **Validator:** Independently recomputed framed, LF-normalized SHA-256 matches `d9a57518…f896bac`. All five mutations change the hash, including exact restoration of 1.2.0 condition 4. `TextAmendment` requires logged 1.2.1 alongside original 1.2.0 authorization. The revised controls are not vacuous on this packet.
5. **Consistency:** Both versions are 1.2.1; JSON and markdown agree on **PR 1488 / 2026-10-03T23:29:07Z**. All three packet hashes match.

Validation was read-only; xUnit was not executed.

APPROVE

## Disposition

APPROVE, with no BLOCKING findings.

- **NIT (fixed).** The justification (JSON and markdown) now says the approved 1,508 accommodates run 1's workload, and that the unchanged constant would stop the same 8 re-runs if run 2 repeats it. The inspection statement now says the constant caps run 2's ledger at no more than the budget. The condition-4 text is unchanged, so the registered hash is unchanged.
- **NON-BLOCKING (handled).** `origin/main` was merged into the branch. The test manifest takes main's total and re-applies the +7 with the manifest-bump script.
