# #1276 registration — Codex review, round 2

**Reviewer:** Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family.
**Input:** diff of PR #1473 at `5341dc3b` vs `origin/main`, plus the round 1 record.
**Result:** 1 BLOCKING, 5 MAJOR, 0 MINOR.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Round 1 #2's reasoning contradicts the frozen `equivalenceDefinition`, which says both sides "implement the same task statement"; register task assertions or amend. | **Escalated to the maintainer; not resolvable inside this registration.** The cutoff task statements specify no outputs, so no per-pair task oracle exists to register, and writing 217 sets of task assertions would be new decision-bearing content. `registration.json` `taskStatement.interpretation` now states the relational reading (both arms share one statement and compute the same observable function), states what it does not establish, and states the two ways to require conformance (per-pair task assertions registered by amendment before the oracle runs, or a #1407 amendment). It is decision 4 in the document and the PR body. Counted as an open BLOCKING finding until the maintainer decides. |
| 2 | MAJOR | Preflight checks HEAD but allows a dirty compiler or packet. | **Fixed.** Preflight also refuses when any tracked file differs from the registration commit (`git status --porcelain --untracked-files=no`), so the compiler, oracle, metrics, and packet are the committed ones; git failure refuses too. Controls cover a malformed SHA and a different commit on this checkout. |
| 3 | MAJOR | Exit codes are lost (`Environment.ExitCode` is overwritten by `InvokeAsync`'s result); a single run returns 0. | **Fixed.** The handler sets `InvocationContext.ExitCode`: 2 refused, 4 first run (not a result), 3 invalid second run, 0 valid second run. |
| 4 | MAJOR | `--compare-with` ignores the first run's provenance; equal-sized different pair sets pass. | **Fixed.** Reconciliation requires equal `oracleId`, `oracleVersion`, `inputGeneratorVersion`, `registrationCommit`, `pairsSha256`, and `environment`, and exactly equal pair-id sets. Synthetic controls for a different commit and a different pair set. |
| 5 | MAJOR | Parameterless constructors, object overrides, and inherited members can hide behavior. | **Fixed.** Only exported static classes are supported; any other exported type is unsupported (`UNCLASSIFIED`). In a static class only accessors and the type initializer are skipped (a failing type initializer is observed on the first call). Control: a public class with a throwing constructor is `UNSUPPORTED_SURFACE`. |
| 6 | MAJOR | Invocation and result rendering share one catch, so a lazy result that throws when enumerated looks like an immediate throw. | **Fixed.** Rendering failures are a separate observation kind (`throw-while-reading-result`). Control: immediate throw vs throw on enumeration is `NOT-EQUIVALENT`. |

Also in this round, to stay within the 1,500-line ceiling: the generator's `--check` mode was
dropped (the registration tests and the pinned seal detect any drift), and the nondeterminism and
timeout controls moved to C#-vs-C# theory rows (the timeout control now sleeps instead of spinning).

Test counts after round 2: Calor.Compiler.Tests +29, Calor.Evaluation +21.
