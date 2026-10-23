# R2 release-gate dry run (C2 regeneration 3)

**Synthetic, not an adjudication.** This checks that the regeneration-3 publication hashes, the
claim registry, and the raw-artifact freeze manifest satisfy the R2 gate
(`scripts/verify_release_adjudication.py` at the candidate) when a terminal record names them.

How it ran (`dryrun.sh.txt`, `make_terminal.py.txt`), in a scratch clone only:

1. A synthetic `docs/plans/evidence/adjudication-1408/terminal-record.json` was committed locally
   on top of the regeneration-3 records commit `0ceff9d49f9cf5fdd241a45d25444df9d4803cb1`. `refs/remotes/origin/main` was moved to
   that commit **locally** so the gate's "on protected main" check could run. Nothing was pushed.
2. The candidate `04e61f18` was checked out. The gate ran with `--expect-head --version 0.24.0`,
   with clone A's release notes, packages, metadata, and website.
3. B2's gate wrote the headline and stamp entry. R2's `--benchmark-worktree` check then ran.

| Mode | Record | Surfaces | Benchmark worktree |
|---|---|---|---|
| `success` | every inventory artifact `BOUNDED` (historical-only: `HISTORICAL-ONLY`), gate rows `BOUNDED`, the 28 claims `BOUNDED`; publication = `publication-candidates.json` | **PASS** | **PASS** |
| `with-blocked-determinism` | the same, with `gate:#1135` and `gate:#1424` `BLOCKED` | FAIL, `G008` only (as expected) | FAIL, `G008` only (as expected) |

The success mode shows that the regeneration-3 hashes, claim registry, and freeze manifest satisfy
every R2 surface check. It is not an adjudication: #1408 decides every outcome. The second mode
shows the gate refusing a record with blocked rows.
