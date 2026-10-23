# R2 (#1410) review round 3 — Codex

**Reviewer:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, cross-family
adversarial review. **Input:** combined diff of #1474 and #1475 after the round 2 fixes, against
`origin/main`, with the round 1 and 2 records available. **Result:** 1 BLOCKING, 2 MAJOR, 1 MINOR.
**After disposition:** 0 open BLOCKING. This was the last round allowed by the §9 ceiling (3 rounds
per PR); the round 3 fixes below were not re-reviewed by Codex.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `benchmark.yml` interpolates the `statistical_runs` dispatch input into shell code; a value with `$(...)` runs before the final gate with write credentials | **Fixed (#1475).** The input reaches the shell only through an environment variable and must match `^[1-9][0-9]{0,3}$`; the step fails otherwise. The new control `DispatchInputsAreNeverInterpolatedIntoShell` (4) rejects any `${{ github.event.inputs.* }}` or `${{ inputs.* }}` inside a `run:` block of a gated workflow. The same pattern in `verify-release.yml` (`inputs.version`) and the `allow_weaker_methodology` check was moved to the environment too |
| 2 | MAJOR | Package READMEs (rendered by nuget.org) escape the wording scan | **Fixed (#1474).** Every text entry in each `.nupkg` (`.nuspec`, `.md`, `.txt`, ...) is scanned. Control: `IndependentClaimInThePackageReadmeFails` |
| 3 | MAJOR | JSON decoding and markup removal did not compose | **Fixed (#1474).** Every source (the text and, for JSON, its decoded strings) goes through entity decoding, both markup removals, and emphasis removal. Control: `JsonEscapedClaimSplitByMarkupFails` |
| 4 | MINOR | Structural test accepted a fail-open gate (`|| true`, `continue-on-error`) | **Fixed (#1475).** The gate check rejects a verifier invocation whose command is suppressed (`|| true`, `|| :`, `|| echo`, `; true`) and `continue-on-error` on a gate step; mutation controls `GateCheckerRejectsASuppressedGate` (2) and `GateCheckerRejectsContinueOnErrorOnTheGateStep` prove the checker rejects both |

Tests after round 3: 81 verifier controls in #1474 and 19 workflow controls in #1475; 100
ReleaseGate cases in total, all passing locally. (The #1475 fixes for items 1 and 4 land in #1475's
round 3 commit; this record is updated there.)
