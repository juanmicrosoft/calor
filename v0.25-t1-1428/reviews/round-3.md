# T1 (#1428) Codex review, round 3 (invocation 3 of at most 4; last adversarial round)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: round 2's
prompt for round 3, on PR #1562 at `534b2f832`. **Verdict:** CHANGES-REQUESTED (3 MAJOR, 1 MINOR).
Reduced independence (R0 §9).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R3-1 | MAJOR | CLI migrate's exit code and MCP migrate/batch `isError` are discarded; refusal is inferred only from `conversionPaths` | Fixed: every cell records `surfaceError` (exit code, `isError`, library `Success`), and it participates in the committed-observation comparison; refusal = `surfaceError` or a `refused` path outcome |
| R3-2 | MAJOR | A refused conversion whose output compiles is `rejected` without running it | Fixed: emitted output is always run and compared; a refusal whose output changes behavior is `rejected-mismatch` and always fails |
| R3-3 | MAJOR | S014 lets both copies replace requirement text, redirect sources or weaken `requires` | Fixed: the five required checks are frozen with their sources, and amendment 1.3.0's recorded preconditions are pinned by digest (`PreconditionsDigest130`); a later change has to be a later amendment, still under the floors. Controls: weakened requirement, redirected source |
| R3-4 | MINOR | `aggregate.py` finds uncited records only when their path contains the SHA | Fixed: it also searches evidence JSON content for the candidate SHA (`git grep`), then validates each record's content |

The next Codex invocation is the verification-only pass (`verification.md`).
