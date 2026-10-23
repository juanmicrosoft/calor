# 0.25 Step A amendment PR (#1610): Codex round 1

- **Invocation 1 of at most 4** (fresh budget for this PR: 3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `3fd745a1b`, whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 BLOCKING, 3 MAJOR, 1 MINOR.

Codex confirmed: both protocol validators (main's and this PR's) pass against main's baseline; the five per-PR
corpus deltas match history; the two `[0.25.0]` sections are byte-identical; 14,663 + 19 = 14,682.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | A second execution of the candidate skipped the repair guard whatever the first verdict was, so a later DETERMINISTIC run could hide a NON-DETERMINISTIC or FAILING one. | Fixed: the second execution runs only after the first was decided INCOMPLETE with no DISAGREE and no AGREE-FAIL, as its decided record committed on protected main at `releaseEpoch.priorResultPath` (`docs/plans/evidence/g2-1421/epoch-results/{runId}.json`) says (schema, mode, commit, run id, verdict, class counts, protocol version). The plan step reads it with `committed_result`; D008 requires the path when `maxExecutionsPerCandidate` > 1. Eight refusal controls (no record, NON-DETERMINISTIC, FAILING, a DISAGREE, an AGREE-FAIL, another commit, another run id, another protocol version) and two D008 controls. |
| 2 | MAJOR | The gate sealed the re-registration but did not check the measurement or the counts. | Fixed: `reregistration_problems` ports T005's re-registration checks (amendment, measurement under F2's `ready/` at its hash, case and candidate, per-PR records, after = measurement, before and previous after = F2's `rows.json`, before + deltas = after). Gate controls: measurement edited, missing, deltas inconsistent (each re-sealed so only this check fires). The gate fixture's synthetic READY case files move to `ready/fixture-cases/`, so they no longer overwrite the real measurement. |
| 3 | MAJOR | A not-measured marking needed only a nonblank decision string, so it could be retargeted to another case. | Fixed in both implementations: the marking must be a pinned approved triple (`InteropScopeValidator.ApprovedNotMeasured`, `APPROVED_NOT_MEASURED_1426`: 1.5.0, `D-F4-ITER-04-NOT-MEASURED`, F4-ITER-04), and the decision in the same amendment must name the case (`notMeasured`). Controls: retargeted case, unapproved decision, decision no longer naming the case; two gate-parity rows. |
| 4 | MAJOR | The supersession exception turned any INVALID range into a pass. | Fixed: only `D-AMEND-REFREEZE` in amendment 1.5.0 counts, and while superseded every invalidating change must be to one of its `permittedPaths` (this PR's files, none under `src/`), with no classifier error. New fact `ASupersededCandidateStillFailsOnAnyOtherChange` (an unrelated `src/` change fails; a `src/` permitted path is refused). |
| 5 | MINOR | The notes called 35,177 -> 34,371 "native source expressions"; they are binder visits. | Fixed in both changelog sections, with a wording assertion in `public-claims.spec.ts`. |

Test manifest 14,682 -> 14,691.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **BLOCKING — `scripts/determinism_runner.py:197`**: A second execution bypasses the repair guard whenever the SHA matches, regardless of the first verdict. The new test explicitly permits a prior `failure`. Neither `cmd_decide` nor the single-execution decider carries that verdict forward, so a later DETERMINISTIC result can conceal NON-DETERMINISTIC/FAILING. Read and validate the prior result; allow continuation only after INCOMPLETE, and enforce the retained-failure rule with controls.

2. **MAJOR — `scripts/verify_release_adjudication.py:824`**: The gate seals the re-registration's declared measurement hash without checking the referenced file or porting T005's measurement/count checks. Returning `None` for that measurement produced **zero gate errors**. Verify its bytes, candidate, counts, amendment and deltas; add parity controls for missing, edited and inconsistent measurements.

3. **MAJOR — `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:250`, `scripts/verify_release_adjudication.py:720`**: A not-measured marking needs only a nonblank decision string. Replacing F4-ITER-04 with F4-ITER-02, setting `decision` to `UNAPPROVED-DECISION`, and naming F4-ITER-01 as coverage passes replay. S007 then accepts the substituted case as skipped. Resolve and bind the exemption to the approved decision's specific case; reject unknown decisions and retargeting in both implementations.

4. **MAJOR — `tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:68`**: Supersession turns **any** INVALID range into a passing default check. An unrelated `src/` change included with this amendment also passes; the helper merely finds a matching `supersedes` string. Explicit-target checks remain strict, but default CI loses this discrimination. Constrain the exception to the authenticated Step A amendment and its permitted changes, with an unrelated-source-change negative control.

5. **MINOR — `CHANGELOG.md:108`, `website/content/changelog.mdx:114`**: The notes still call 35,177 → 34,371 "native source expressions." These are binder visits; the baseline explicitly says they include generated references and opaque expressions and do not measure native fidelity. Correct both sections and add a wording assertion.

Both protocol validators pass against main's baseline. The five per-PR deltas match history; the release-note sections are byte-identical; net new compiler cases are **19**, giving **14,682**. Twenty-eight focused Python controls pass; full .NET/tempfile tests were unavailable under the read-only sandbox.
```
