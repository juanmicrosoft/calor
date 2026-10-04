# #1135 G3 PR 2 (#1500) — verification pass (Codex)

## Pass 1

**Reviewer:** Codex CLI (as in the review rounds). **Verified commit:** `1922cb9e`. **Verdict:**
NOT-VERIFIED. Checks (a), (b), (c), (e), and (f) passed; (d) failed on one overstated claim.

| Check | Result |
|---|---|
| (a) `result.json` | Reproduces byte for byte under the 1.2.0 protocol and harness hashes. Its verdict, completeness, class counts (1,580 / 557 / 0 / 0), attempt statuses (53 completed, 7 invalid, 90 missing), and four `OPEN` rows match the packet. Every completed Windows observation passes, and the report and fixture hashes equal the committed values. All four cut invocations kept the passing fixture value; three kept passing cells and report hashes |
| (b) `attempts.tar.gz` | 60 attempt records, 4 `env.json`, 4 start markers. Per job, (completed, cut, not started): x64 j1 (14, 1, 0); x64 j2 (12, 1, 2); ARM64 j1 (13, 1, 1); ARM64 j2 (14, 1, 0). Median/maximum durations: 156.3/201.2 s and 28.9/43.2 s. Setup took 1.23–2.98 minutes. The logs show colon-path upload failures on all 6 Linux and macOS jobs |
| (c) Ledger hashes | Both match |
| (d) Code behavior | Cleanup, probe placement, pruning, removal-failure handling, and the unchanged decider match the code. **Overstated:** "a harness cut makes the execution `NON-DETERMINISTIC`". The decider needs two distinct values for `DISAGREE`, and a synthetic execution whose only problem is a cut is `INCOMPLETE` |
| (e) Budget | `worst_case` gives (655, 115, 1965). API job durations total 388 minutes; 401 are recorded; the remaining worst case is 1,711 |
| (f) Hashes and validation | All nine `sha256.json` entries match. Both this tree's and main's validators report `protocol valid` |

Also flagged: the G2 README's "Already spent: 13" is stale as a current total.

**Fixed at the next commit:** `runPlan.harnessCutInconsistency`, the 1.3.0 limitation, the G2
README, and the PR body now say that a cut makes a case `DISAGREE` only where another attempt
observed a different value, and otherwise only makes the execution `INCOMPLETE`. The G2 README
now gives 13 as the registration-time charge and 401 as the amount recorded after g3-exec-1.
