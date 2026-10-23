# #1135 G3 PR 2 (#1500) — adversarial review round 1 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root. Input on stdin: `git diff origin/main...HEAD`, excluding
`executions/g3-exec-1/` (the reviewer read those files from the checkout). **Reviewed commit:**
`309333da`. **Verdict:** 1 MAJOR, 2 MINOR. Changes requested.

No execution or control run was dispatched while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | MAJOR | Cleanup suppresses errors. If a rejected path survives, `fill-missing` fails but the `always()` upload still sends the same directory, and the job's records are lost again. Suggested: upload the records separately from scratch | **Fixed in the runner; residual recorded.** After removing homes, `fill-missing` now removes every remaining path whose name the upload rejects. No record name can contain such a character. The removed paths are listed in `upload-pruned.txt`, and the step fails only if a removal fails. The control now injects a removal failure and checks that the attempt records are untouched. The workflow is not split into two uploads: its job and step structure is fixed by `D013` in main's validator, which the required `calor-first-guard` runs on this tree, so a new upload step would be rejected. Residual, recorded in `workflow.deviations` and the G2 README: a removal that fails still loses that job's records |
| 2 | MINOR | "About 6 minutes of setup" contradicts the job timestamps (1.2–3.0 minutes before `run-job`) | **Fixed.** The G3 README, the G2 README, `budget.amendment130`, and the PR body now give 1.2–3.0 minutes plus about 3.1 minutes per attempt (up to 201 s plus 43 s) |
| 3 | MINOR | Cut invocations did not make every covered case `DISAGREE`: they kept passing values, and `artifact:translator-fixture` is `INCOMPLETE` | **Fixed.** The G3 README now says the 1,580 `DISAGREE` cases come from harness-generated `Timeout`/`Missing` values differing from the retained observations. It names the passing values the cut invocations kept and says the fixture is `INCOMPLETE` |
