# #1421 G2 determinism protocol — targeted verification pass 2 on the D014 fix (Codex)

Requested by the session lead under the maintainer's delegation. The lead declined to accept the D014
residual left open by the first verification pass, and asked for a structural fix in this PR and
one targeted pass.

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, with
the diff since `53b38223` on stdin. **Reviewed commit:** `f30a9cdd`. **Verdict: NOT CLEAN.** Both
open findings were resolved, but the pass found 2 new BLOCKING defects.

| Item | Result | Evidence |
|---|---|---|
| Open 1: shell `if false; then … fi` around the trusted validator call | RESOLVED | The mutation produces D014 |
| Open 2: `if: ${{ 1 == 2 }}` on publish-nuget's "Run project tests" | RESOLVED | The mutation produces D014 |
| New BLOCKING 1 | Quoted control keys (`'if':`, `"continue-on-error":`) on a gate step were ignored by the parser | `validate` returned no violation |
| New BLOCKING 2 | A shard step could stop running tests: `exit 0` after `set -euo pipefail`, or `if false; then … fi` around the rest of the script | `validate` returned no violation |

## Fixes made after this pass (commit after `f30a9cdd`; not re-verified by Codex)

The lead asked for exactly one targeted pass. Both new findings were mechanical, so they were fixed
and covered by negative controls without a further review:

1. **Parser.** Any line at a job or step key position that the parser does not read as a plain key
   is recorded as unparsed and fails the gate. This covers a quoted key, a flow mapping, an anchor,
   and a merge key. Controls: `'if': ${{ 1 == 2 }}` and `"continue-on-error": true` on the
   main-validator step.
2. **Shards.** Each shard step's script is frozen by the SHA-256 of its non-blank lines
   (`gates.shardSteps[].scriptSha256`), in addition to the tokenizer checks. Controls: `exit 0`
   after `set -euo pipefail`, and an `if false; then … fi` wrapper, in publish-nuget's
   "Run project tests".

Whether these two fixes are complete is for the maintainer's review. Codex has not checked them.
