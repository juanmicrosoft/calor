# #1135 G3 repair PR (#1492) — verification pass 1 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin. **Verified commit:** `21738b19`.
**Verdict:** NOT-VERIFIED. Every code check passed; three claims were worded too strongly.

| Check | Result |
|---|---|
| (a) Every solver check from `Z3Verifier`, `ObligationSolver`, `Z3ImplicationProver`, and the oracle's SelfRef binding goes through `IsolatedSolver`, and the only `Simplify` goes through `IsolatedSolver.Simplify` | PASS |
| (b) String literals go through `ToZ3StringLiteral`, whose output is ASCII-only; an independent native-Z3 comparison matched for 18 edge cases | PASS |
| (c) No `Environment.NewLine` or `AppendLine` in the oracle's report writers or the translator fixture | PASS |
| (d) `UserHome.Resolve` is used by `VerificationCacheOptions` and `ManifestLoader`, and the probe compiles that file | PASS (no real build in the sandbox) |
| (e) Protocol version, amendment log, and `sha256.json` agree; the validator reports `protocol valid`; the controls had 40 passes and 13 sandbox errors (temp-directory creation denied), with no assertion failures. The reviewer also confirmed with native Z3 4.15.7 that `random_seed` is rejected | PASS |
| (f) No test was added or renamed in a registered project or class | PASS |
| (g) `DeterminismRecord.cs` and `DifferentialModels.cs` are byte-identical to main | PASS |

Overstated claims, all fixed in wording at the next commit:

1. The CHANGELOG and the PR body described isolation as unconditional, but a context the
   factory did not create is checked in place. **Fixed:** both now state the fallback.
2. The PR body said no verdict changes on Linux or macOS, but unchanged snapshots do not prove
   that. The implementation allows a different valid counterexample and a different result near
   the timeout. **Fixed:** the PR body now says what the snapshots show and what can change. The
   CHANGELOG states both effects.
3. The round-2 record called an overhead estimate "no timeout risk". **Fixed:** it now says the
   estimate found no defect and is not a measurement on the runners.
