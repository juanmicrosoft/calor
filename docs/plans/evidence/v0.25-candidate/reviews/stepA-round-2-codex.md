# 0.25 Step A amendment PR (#1610): Codex round 2

- **Invocation 2 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `4236399b1` (the round-1 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 2 MAJOR.

Codex confirmed: the round-1 reproducers are addressed; both protocol validators and 36 focused Python tests pass;
the per-PR attribution matches history; the changelog sections are byte-identical; 14,682 + 9 = 14,691.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | `priorResultPath` pointed under `g2-1421/`, which the 0.25 classifier does not treat as downstream evidence: committing the record would invalidate the candidate, and omitting it blocks the second execution. | Fixed: `priorResultPath` is `docs/plans/evidence/v0.25-regeneration/determinism/epoch-results/{runId}.json`, under the classifier's downstream `v0.25-regeneration/`. New fact `TheSecondExecutionsPriorResultIsDownstreamEvidence` reads the path from `protocol.json` and classifies an added record (exempt) and a rewritten one (invalidating). |
| 2 | MAJOR | The gate checked the corpus counts only when `reRegistration` existed, so removing it and editing a count passed. | Fixed: `corpus_problems` ports T005 unconditionally (case, F2's `rows.json` as the approved source, before and after equal to it, or, re-registered, to the measurement with the per-PR deltas). Gate controls: re-registration removed with an edited count, source redirected (both re-sealed). |

Test manifest 14,691 -> 14,694.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — `docs/plans/evidence/g2-1421/protocol.json:429`**: The second execution requires its predecessor's result committed under `g2-1421/epoch-results/`, but that directory is absent from `ReleaseCandidateInvalidation.V025.Downstream`. Adding `epoch-results/8.json` therefore invalidates the candidate; omitting it prevents the second execution. Store these records under the already permitted `v0.25-regeneration/` directory, and add a control combining the runner guard with candidate classification.

2. **MAJOR — `scripts/verify_release_adjudication.py:868`**: T005 remains bypassable because validation runs only when `reRegistration` exists. Reproducer: remove `corpusCounts.reRegistration`, set `opaqueExpressions.after = 1`, and recompute the existing approval seal and contract hash. `check_scope_packet` returns **zero errors**; C# T005 rejects those counts. Validate corpus counts unconditionally, including the fixed case/source and approved F2 evidence membership. Add removal and source-redirection parity controls.

The original round-1 reproducers are addressed, but these gaps remain. Both protocol validators and 36 focused Python tests pass. Attribution matches history; changelog sections are byte-identical; **14,682 + 9 = 14,691**. .NET and tempfile-dependent checks could not run in the read-only sandbox.
```
