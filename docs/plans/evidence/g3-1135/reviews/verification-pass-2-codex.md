# #1135 G3 repair PR (#1492) — verification pass 2 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, run
from the worktree root with `git diff origin/main...HEAD` on stdin and the current PR body in the
prompt. **Verified commit:** `aeaf5bdc`. **Verdict:** VERIFIED.

The reviewer passed checks (a)–(g) again, with the same evidence as pass 1. Its other results:

- Pass 1's three overstated claims are fixed: the isolation fallback is stated, the claim of no
  verdict changes is limited, and the timing statement is called an estimate.
- No other false or overstated claim was found. The three `resolved` causes in
  `eng/z3-consumers.json` match the code, and both Windows divergence filters are empty.
- Cache format 1.19 rejects older entries. Native Z3 4.15.7 rejects `random_seed` as a context
  parameter.
- The validator reports `protocol valid`. The controls had 40 passes and 13 sandbox errors
  (temp-directory creation denied), with no assertion failures.

Not reproduced by the reviewer: earlier suite results, timings, the GC experiments, and a real
probe build (none of these were possible in its read-only sandbox).
