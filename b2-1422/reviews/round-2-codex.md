# B2 (#1422) review round 2 (Codex, read-only, reasoning effort high)

Verdict: **REQUEST-CHANGES** (4 BLOCKING, 1 MAJOR). The reviewer confirmed as addressed: input
injection, the `origin/main` baseline, the pinned B1 registration anchor, oracle sealing and
validation, and compatibility with R2's `steps.methodology.outputs.report`.

| # | Severity | Finding (summarized) | Response |
|---|---|---|---|
| 1 | BLOCKING | "Parent of the commit that changed results.json" does not prove a prior merge: amendment and results as two commits on one branch, merged together, passed. A withdrawal merged after the results was ignored. | Authorizations are read from `contract.json` at the first parent of the first-parent `origin/main` commit that landed the current `results.json` bytes, and must still be present and not withdrawn in the current contract. Tests: one branch merged with `--no-ff` is refused; a withdrawal after the results revokes. |
| 2 | BLOCKING | `supersedesComparabilityKeySha256` accepted a string, so `"DO NOT supersede <hash>"` matched as a substring. | It must be a list; membership is exact. Test with the string form. |
| 3 | BLOCKING | Concurrency does not stop an older passing checkout from publishing after a newer refusal. | Freshness fence (B2-08): the B1 packet, corpus, `tests/Calor.Evaluation`, `contract.json`, and the gate script at HEAD must equal `origin/main`'s, fetched in the same step. An older checkout with equal inputs yields the same headline; one with different inputs is refused. Test added. |
| 4 | BLOCKING | Cleanup ran before publication, so a later publication failure (including R2's replacement step) skipped it. | Cleanup is the job's last step, `if: failure()`. Test asserts it is last. |
| 5 | MAJOR | `--limit 1000` could omit PRs; one failed close aborted the rest; "closes any" overclaimed. | `gh api --paginate .../pulls?state=open&per_page=100`; every close attempted, any failure fails the step. CHANGELOG says "tries to close". Tests: listing failure, partial close failure. |
