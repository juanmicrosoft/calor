# Codex review, round 1 of 3 (PR #1581)

**Reviewer:** OpenAI Codex, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. **Independence:** reduced.
No reviewer independent of the maintainer reviewed this work.
**Reviewed:** the PR at `20ec494c5` (measurement commit `0be9a9e0c` plus the record commit). Prompt: `round-1-prompt.md`.
**Verdict:** CHANGES-REQUESTED (BLOCKING 4, MAJOR 0, MINOR 0, NIT 0).

## Findings (verbatim)

- [BLOCKING] [docs/plans/evidence/v0.25-f1-943/ready/ready-record.json:304](docs/plans/evidence/v0.25-f1-943/ready/ready-record.json:304) — All six records cite nonexistent `v0.25-regeneration/reviews/round-1.md` — Running `ready_problems` against HEAD reports the missing-review violation for every family, in addition to the deliberately absent approval URL. Thus the five READY families do not meet the claimed S007 boundary. The dry run masks this with a synthetic review file — Retain the actual review at the cited path and rerun validation.

- [BLOCKING] [docs/plans/evidence/v0.25-f2-906/ready/cases/F2-INTERP-09.json:10](docs/plans/evidence/v0.25-f2-906/ready/cases/F2-INTERP-09.json:10) — The case passes despite unapproved corpus-count changes — Candidate values are bound **4,828**, visits **34,371**, boundaries **148**, opaque expressions **7,746**; registered “after” values are **4,827 / 35,160 / 137 / 6,994**. T1’s `readyNeeds` explicitly requires preserving these counts or an approved, case-linked amendment. Zero incomplete diagnostics and an unchanged candidate baseline do not satisfy that requirement — Keep this case and F2 blocked until the amendment and any required re-freeze are completed.

- [BLOCKING] [docs/plans/evidence/v0.25-f6-1144/ready/cases/F6-REPORT-12.json:16](docs/plans/evidence/v0.25-f6-1144/ready/cases/F6-REPORT-12.json:16) — Surface-specific cells are marked passed using library-only measurements — All six cited tests exercise the converter library, yet they supposedly establish CLI, MCP and migration cells. For example, `cli-migrate-both-enabled` passes although the retained migration tests never invoke `migrate --passthrough`. The producer assigns every F6 cell `True` at `make_ready.py.txt:288` without checking coverage — Map each cell to measurements that exercise its surface and options; leave unmeasured cells blocked and obtain an amendment for unavailable combinations.

- [BLOCKING] [docs/plans/evidence/v0.25-regeneration/results.json:139](docs/plans/evidence/v0.25-regeneration/results.json:139) — `provenance-candidate-manifest` claims PASSED using missing classifier evidence — Neither the promised `classifier.txt` nor the landing-clone classifier/test outputs are retained. The available `measure/v025-manifest.log` instead says “No test matches,” and its TRX summary records zero tests. This does not substantiate the claimed landing-clone run or the stated 107 passing tests — Add the fresh landing-clone run, explicit target, logs and TRX; keep the row blocked until that evidence exists.

Counts: BLOCKING 4, MAJOR 0, MINOR 0, NIT 0.

VERDICT: CHANGES-REQUESTED

## Disposition

Every file must be added once and never modified, because the V025 classifier diffs every commit in the range. So
the fixes rebuilt the PR's commits from the same raw measurements (force-pushed). No measurement was re-run for
these fixes.

1. **Missing review record (BLOCKING): fixed.** This file is the record the READY records cite
   (`docs/plans/evidence/v0.25-regeneration/reviews/round-1.md`). The R2 dry run now runs on a base that contains
   it, so it needs no placeholder.
2. **F2-INTERP-09 (BLOCKING): accepted.** The case now applies the T1 register's rule: the registered before/after
   counts must be preserved, or changed only by an approved, case-linked amendment. On the candidate, bound, visits,
   opaque boundaries and opaque expressions differ from the registered "after" values, and no amendment records the
   change, so the case is `failed`. F2 is BLOCKED (`ready/ready-record.json` reason and blockedBy). The measured values
   and the comparison are in the case record (`registerAfterPreserved`).
3. **F6 cells (BLOCKING): fixed by an explicit per-cell map; F6 stays READY.** Each F6 cell now names the tests and
   W1 transcripts that exercise its own surface and options (`tools/make_ready.py.txt` `F6_CELLS`), not the row's
   test list. CLI cells cite the CLI tests (F6_REPORT_01/02/10/12_CliWebsiteExample); MCP cells cite F6_REPORT_03/11/13;
   cli migrate F6_REPORT_04; MCP migrate F6_REPORT_05; batch F6_REPORT_06; the library cells cite the library tests.
   Schema-version and defaults cells also cite the W1 transcripts, which regenerate byte-identically on the
   candidate and carry `conversionPaths.schemaVersion` 1 and both option flags for cli migrate, MCP migrate and
   calor_batch. Two kinds of cell rest on a reading the maintainer must confirm (regeneration.md, "For the
   maintainer"):
   - **"both-enabled" cells on surfaces that do not expose both options** (mcp-both-enabled; cli-migrate-both-enabled;
     mcp-migrate-both-enabled; the precedence cells of F6-REPORT-04/05). The scope note measures both options "on every
     surface that exposes them". These cells record what the surface does with every option it does expose, and that
     it reports the others off (F6 decisions D1 and D3).
   - **SDK cells**, measured on the library: decision D4, and the T1 register lists SDK as not measured, with the
     library row standing for it.
   Codex's specific example, that `cli-migrate-both-enabled` passes without `migrate --passthrough`, is answered by
   the first reading: `calor migrate` has no `--passthrough` option.
4. **Classifier evidence (BLOCKING): fixed.** `classifier/` holds the fresh landing-clone run against the PR head: the
   explicit target, the range log with the verdict, and the TRX and logs of V025CandidateManifestTests and
   ReleaseCandidateInvalidationTests. A record cannot classify the commit that contains it, so this lands in a final
   commit that adds only classifier files. `results.json` now says why the measurement lane's `v025-manifest` run
   matched no test: the candidate predates those test files.
