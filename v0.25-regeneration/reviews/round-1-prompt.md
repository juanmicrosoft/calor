You are an adversarial reviewer for PR #1581 in juanmicrosoft/calor (the current directory is a checkout of the PR branch `milestone-0.25/reproof-48cae911`; `git diff origin/main...HEAD` is the PR). Review read-only.

## What the PR claims

It re-runs, on the frozen 0.25 release candidate `48cae911700ceb0dfc358e00933650445d81604f`, the checks that scope 1.4.0 `acceptancePreconditions` require (`docs/plans/evidence/v0.25-r0-1426/scope.json`: proof-s2-witnesses, provenance-candidate-manifest, provenance-regeneration, provenance-determinism, release-gate). It also writes one READY record per family under `docs/plans/evidence/<family dir>/ready/`, meeting rule S007 (`tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs` ValidateReady, and its port `ready_problems` in `scripts/verify_release_adjudication.py`). The model is 0.24's C2 regeneration 3 (`docs/plans/evidence/c2-1424/regeneration-3/`). Start with `docs/plans/evidence/v0.25-regeneration/regeneration.md`, then `results.json`, `ledger.json`, the six `ready/ready-record.json`, and a sample of `ready/cases/*.json`. The producers are in `docs/plans/evidence/v0.25-regeneration/tools/*.txt` (make_ready.py.txt writes the case and READY records).

Constraints the PR must respect:
- No `src/` change and no change to any frozen input. Only newly added data or text files (.json .jsonl .md .txt .log .trx .csv .gz .sha256, with no dot-segment) under `docs/plans/evidence/v0.25-regeneration/` and the six family `ready/` directories. The V025 classifier in `tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs` diffs every commit, so a later modification of an added file invalidates the candidate.
- Every measurement comes from a fresh clone with an isolated HOME and CALOR_HOME.
- Records must not overclaim. "Independently reviewed" or "independently verified" is forbidden, and so is calling something passed when the evidence does not show it.

## What to look for (concrete, checkable)

1. A case record marked `passed` whose measurements do not support it. Check the judgement against the registered case (`scope.json` cases: note, expected, cells) and the raw outputs (driver results under `v0.25-regeneration/measure/drivers/`, the T1 `controls-results.json`, the TRX summaries in `measure/trx-summary.json`). Look especially at the analysis rows (F1-REFOUT-A1/A2, F3-LOCAL-A1, F5-ARRAY-06), F2-INTERP-09 (corpus counts), the X-INT rows, the DOC rows, and the F6 cells.
2. A READY record that fails S007 or the gate's `ready_problems` for any reason other than the approver URL (which is deliberately left for A1). Also: the `requirementSha256` formula, the witness-run `witnesses` lists, `blockersResolved`, the tests totals, the website pages, and the reviews.
3. A re-proof row whose status is not supported by its evidence: test totals vs `eng/test-manifest.json`, the Tier 2 counts vs the registered expectations, byte identity (`local/compare-*`, `publication-candidates.json`), and the determinism result (`determinism/result.json`) and its handling under protocol 1.6.0 (`docs/plans/evidence/g2-1421/protocol.json` budget.releaseEpoch, `scripts/determinism_runner.py` plan_problems).
4. The F4 BLOCKED decision for F4-ITER-04, and whether F4-ITER-01/02 are rightly passed when the strict `--no-fallback` surface is excluded.
5. Anything that would make the R2 dry run (`r2-dry-run/`) misleading.
6. Classifier risk: any path or extension that the V025 profile does not exempt.

Report each finding as `[BLOCKING|MAJOR|MINOR|NIT] <file>:<location> — <problem> — <why it matters> — <suggested fix>`. A finding is BLOCKING only if it makes a READY claim, a PASSED row or the candidate's integrity wrong. Finish with exactly one line, `VERDICT: <APPROVE|CHANGES-REQUESTED>`, and a count by severity. Be concrete and do not pad the list.
