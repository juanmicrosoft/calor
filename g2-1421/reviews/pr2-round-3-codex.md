# #1421 G2 PR 2 (execution machinery) — adversarial review round 3 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Reviewed commit:** `7b6b635e`
(PR #1486). **Verdict:** 1 BLOCKING, 2 MAJOR, 1 MINOR — request changes. The round-2 fixes were
not reported as incomplete except where listed.

No registered case was run and no control run was dispatched while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `TEMP`/`TMP` were inherited, so the Windows installer and NuGet could write the real profile's temp directory | **Fixed.** The first step of every job and every invocation set `TEMP`, `TMP`, `TMPDIR`, and `NUGET_SCRATCH` under the isolated home (`UNDER_HOME`, checked by `check_isolation`); the Windows installer gets an explicit `-ZipPath` in the work directory |
| 2 | MAJOR | A ledger entry with `mode: execution` turned a refused dispatch into an execution | **Fixed.** Only a ledger entry with `attemptsStarted: true` marks an execution; control for a ledgered plan refusal |
| 3 | MAJOR | Per-file recovery dropped readable `unregistered` cell findings | **Fixed.** Recovery keeps the recovered `unregistered` list with the cell values |
| 4 | MINOR | No controls for the probe's failure paths | **Fixed.** `test_the_home_probe_fails_closed`: failed build (probe never runs), failed run, empty output, foreign profile path |
