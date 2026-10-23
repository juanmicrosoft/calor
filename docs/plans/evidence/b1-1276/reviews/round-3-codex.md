# #1276 registration — Codex review, round 3 (final)

**Reviewer:** Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family.
**Input:** diff of PR #1473 at `5738f29e` vs `origin/main`, plus the round 1 and 2 records.
**Result:** 0 BLOCKING, 1 MAJOR, 0 MINOR. Codex also confirmed that all 506 inventory files match
their cutoff hashes and that the packet and implementation seals match.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | The clean-checkout preflight ignores untracked files, and SDK compile globs would build an untracked `.cs` into the compiler or oracle. | **Fixed.** Preflight now refuses any untracked, non-ignored file as well as any edited tracked file (`git status --porcelain --untracked-files=all`). `--output` is required and the registered command writes both runs outside the repository. Control: an untracked `.cs` probe at the current HEAD is refused (`PreflightRefusesAnotherCommitOrAMalformedSha`). |

Round 3 did not re-raise round 2 #1 (the reading of "implement the same task statement"); it
remains escalated to the maintainer as decision 4.

**Final state after three rounds:** no open BLOCKING finding from round 3; one BLOCKING finding
from round 2 (#1) is escalated to the maintainer rather than resolved, because resolving it needs
either per-pair task assertions registered by amendment or a #1407 amendment.

Test counts after round 3: Calor.Compiler.Tests +29, Calor.Evaluation +21.
