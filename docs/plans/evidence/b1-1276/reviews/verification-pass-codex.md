# B1 #1276 — verification-only pass after round 3 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Scope: whether each round-3 objection is resolved, and any NEW BLOCKING defect introduced by the round-3 fixes. Reviewed head: `fe1c7be3cc00362c3f7fd775c031c9e3a7e61683`.

## Codex output (verbatim)

- **Round-3 objection #1 — RESOLVED.** `Preflight` rejects tracked edits and untracked, non-ignored files using `git status --porcelain --untracked-files=all` ([PairOracleCommand.cs:67](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aad5f85ba426acb2d/tests/Calor.Evaluation/Equivalence/PairOracleCommand.cs:67)).

- **NEW BLOCKING — Required `--output` breaks every child invocation.** Round-3 makes `--output` required ([line 29](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aad5f85ba426acb2d/tests/Calor.Evaluation/Equivalence/PairOracleCommand.cs:29)), but `RunChildAsync` omits it ([line 153](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aad5f85ba426acb2d/tests/Calor.Evaluation/Equivalence/PairOracleCommand.cs:153)). Command-line validation rejects every child before evaluation; all 217 runnable pairs become `ORACLE_CRASH`, including `DomainProblems/CsvParser`. The known-witness check consequently prevents any valid reconciled result.

Verified by code inspection; tests were not run.

VERDICT: NOT CLEAN

## Disposition

- Round-3 objection #1: resolved (confirmed).
- NEW BLOCKING (child invocation omits the required `--output`): **accepted and fixed.** `--output` is now required only for the parent run, enforced in the handler before preflight; the child command line is `PairOracleCommand.ChildArguments`, used by `RunChildAsync`; regression test `ChildCommandLineParsesWithoutParentOnlyOptions` parses that exact command line (mutation-checked: it fails when the old required option is restored). The registration re-pins the oracle hash and the packet seal; the registration is unmerged, so this is not a post-registration oracle change. The fix takes the PR to 1,520 changed lines excluding `docs/plans/evidence/`, 20 over the 1,500 ceiling; contract amendment 1.1.0 records the overrun per §8.
