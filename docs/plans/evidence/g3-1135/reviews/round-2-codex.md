# #1135 G3 repair PR (#1492) — adversarial review round 2 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Reviewed commit:** `cd8a923e`.
**Verdict:** APPROVE. No new findings.

The reviewer accepted all six round-1 dispositions:

| Round-1 # | Severity | Reviewer's check |
|---|---|---|
| 1 | BLOCKING | `protocol.json` registers that the seed is not applied and that the effective configuration is the Z3 defaults; amendment 1.2.0 and the README agree |
| 2 | MAJOR | Cache format 1.19; the validity check rejects older entries, including the affected Windows verdicts |
| 3 | MAJOR | `Z3ContextFactory` keeps a private settings snapshot, and `CreateLike` reuses it without reading later defaults. The `ConditionalWeakTable` holds no strong reference. The in-place fallback uses the supplied context and does not dispose it |
| 4 | MINOR | Lost incremental state and possible counterexample changes are documented. Replay keeps the active scopes and the incremental-engine choice. Every call site maps variables and extracts evidence before the check context is disposed |
| 5 | MINOR | The CHANGELOG leaves cross-platform determinism to the registered execution |
| 6 | NIT | The README separates protocol execution from local and ordinary-CI tests |

The reviewer also ran the validator against `origin/main` (D001–D016 and frozen hashes pass) and
23 selected Python controls (pass). It found no unregistered test additions. Not verified by
the reviewer, which was read-only: a real probe build, and timings on the five platforms. It
estimates the overhead at about 100 seconds per job for the two oracle invocations across 15
attempts and found no timeout defect; that estimate is not a measurement on the five runners.
