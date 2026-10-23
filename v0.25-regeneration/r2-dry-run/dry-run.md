# R2 release-gate dry run (0.25 re-proof)

**Synthetic, not an adjudication.** This checks that the re-proof's publication hashes, READY records,
raw-artifact freeze and the frozen 0.25 scope packet satisfy the R2 gate (`scripts/verify_release_adjudication.py` at
the candidate, `calor.v0.25-scope-packet/1426` format, release-gate.md §10) when a #1430 terminal record names them.

How it ran (`../tools/dryrun025.sh.txt`, `../tools/make_terminal_025.py.txt`), in a fresh scratch clone only:

1. The clone fetched this PR's commit `451a24592` (the measurement commit plus the review records) and committed, **locally only**, a synthetic
   `docs/plans/evidence/adjudication-1430/terminal-record.json`, a synthetic claim registry (one claim), the
   `0.25.x` map entry flipped to `adjudicated`, and a placeholder approval URL
   (`pull/1#pullrequestreview-1`) in each READY family's record. `refs/remotes/origin/main`
   was moved to that commit locally so the gate's "on protected main" check could run. Nothing was pushed.
2. The candidate `48cae911` was checked out. The gate ran with `--expect-head --version 0.25.0` against pub-a's
   packages, metadata and website and clone A's release notes.
3. B2's gate wrote the headline and stamp entry; R2's `--benchmark-worktree` check then ran. The synthetic record
   sets `publication.benchmark-results.approved` (G015), which is A1's decision.

| Mode | Record | Surfaces | Benchmark worktree |
|---|---|---|---|
| `success` | `PARTIAL_READY`: F1, F3, F5, F6 `READY` (citing their committed READY records by path and SHA-256), F2 and F4 `PENDING`; all five re-proof checks `PASSED` (determinism is a placeholder: it is BLOCKED in reality); #1545-#1547 `CLOSED`; the claim `BOUNDED` | **PASS** | **PASS** |
| `with-blocked` | the real state: `family:F2`, `family:F4` and `reproof:provenance-determinism` `BLOCKED` | FAIL, exactly those three `G008` rows (as expected) | FAIL, the same three rows |

The success mode shows that the READY records meet the gate's S007 port once an approver URL and the review record
exist, and that the publication hashes and the scope packet pass every surface check. The second mode shows the gate
refusing the record the current evidence would give. Neither is an adjudication: A1 (#1430) decides every outcome.
