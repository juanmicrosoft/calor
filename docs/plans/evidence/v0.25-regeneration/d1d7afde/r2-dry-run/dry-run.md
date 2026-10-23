# R2 release-gate dry run (0.25 re-proof on d1d7afde)

**Synthetic, not an adjudication.** This checks that the re-proof's publication hashes, READY records,
raw-artifact freeze and the frozen 0.25 scope packet satisfy the R2 gate (`scripts/verify_release_adjudication.py` at
the candidate, `calor.v0.25-scope-packet/1426` format, release-gate.md §10) when a #1430 terminal record names them.

How it ran (`../tools/dryrun025.sh.txt`, `../tools/make_terminal_025.py.txt`), in a fresh scratch clone only:

1. The clone fetched this PR's commit `f629a09a1` and committed, **locally only**, three synthetic changes: a
   `docs/plans/evidence/adjudication-1430/terminal-record.json`; a claim registry with one claim; and the `0.25.x`
   map entry flipped to `adjudicated`. It also wrote a placeholder approval URL (`pull/1#pullrequestreview-1`) into
   each family's READY record. When the base commit has no review record yet, a placeholder stands in for it, and
   `make-terminal.txt` says so. `refs/remotes/origin/main` was moved to that commit locally so the gate's "on
   protected main" check could run. Nothing was pushed.
2. The candidate `d1d7afde` was checked out. The gate ran with `--expect-head --version 0.25.0` against clone A's
   packages, metadata, website and release notes.
3. B2's gate wrote the headline and stamp entry, and then R2's `--benchmark-worktree` check ran. The synthetic
   record sets `publication.benchmark-results.approved` (G015), which is A1's decision.

| Mode | Record | Surfaces | Benchmark worktree |
|---|---|---|---|
| `success` | `ACCEPTED`: all six families `READY`, each citing its committed `ready/d1d7afde/ready-record.json` by path and SHA-256; all five re-proof checks `PASSED`; #1545-#1547 `CLOSED`; the claim `BOUNDED` | **PASS** | **PASS** |
| `with-blocked` | the same record with `reproof:provenance-determinism` `BLOCKED`, a synthetic negative control | FAIL, exactly that `G008` row (as expected) | FAIL, the same row |

Neither mode is an adjudication: A1 (#1430) decides every outcome.
