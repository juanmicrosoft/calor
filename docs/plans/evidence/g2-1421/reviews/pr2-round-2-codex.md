# #1421 G2 PR 2 (execution machinery) — adversarial review round 2 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Reviewed commit:** `e29a8786`
plus the copied-workflow control simplification (PR #1486). The first attempt stopped at the usage
limit and produced no findings; the round was re-run after the limit reset. **Verdict:** 2
BLOCKING, 4 MAJOR — request changes. The reviewer confirmed that main's validator and the new
validator both accept the tree and that the round-1 dispositions it checked hold.

No registered case was run and no control run was dispatched while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | The `dotnet run --file` home probe caches under `SpecialFolder.LocalApplicationData`, which on macOS does not follow `HOME`, so it could write the real home | **Fixed.** The probe is a conventional project built with `-o` under the probe directory and run as `dotnet probe.dll`, with the full invocation environment (`invocation_env`) |
| 2 | BLOCKING | `workflow_problems` accepted `! decide`, `exit 0` before `run-job`, a retry loop in the build step, and removal of the home exports from every job | **Fixed.** Every job's keys and every step (script, action inputs, env, condition) are frozen by SHA-256 in `WORKFLOW_PARTS`; controls for each of the four mutations |
| 3 | MAJOR | `APPDATA`/`LOCALAPPDATA` (and NuGet caches) still pointed at the real Windows profile | **Fixed.** The first step of every job and every invocation set `APPDATA`, `LOCALAPPDATA`, `XDG_*`, `NUGET_HTTP_CACHE_PATH`, and `NUGET_PLUGINS_CACHE_PATH` under the isolated home; `check_isolation` refuses any other value |
| 4 | MAJOR | Ledger commit and mode were not reconciled with the API inventory | **Fixed.** `plan` and `ledger-check` reject a ledger entry whose commit or mode differs from the inventory |
| 5 | MAJOR | The record-read recovery discarded values already readable | **Fixed.** Recovery re-reads cells and each artifact separately; only the unreadable file's values are lost, and the invocation is `invalid` |
| 6 | MAJOR | A completed job without a start time charged zero minutes | **Fixed.** Fails closed unless the job was skipped; control added |

Also merged `origin/main` again (PRs #1483 and #1487); both validators still accept the tree.

Process note: during round 1 the author ran `dotnet run --file` once locally on macOS, in a scratch
directory with `HOME` overridden, to time the first probe design. Per finding 1, that SDK command
can write its build cache under the real `~/Library/Application Support/dotnet/runfile`. Nothing
was deleted; the maintainer may remove that cache if present.
