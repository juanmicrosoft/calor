# #1421 G2 determinism protocol — adversarial review round 3 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, diff
against `origin/main` on stdin (`cases.json` on disk). **Reviewed commit:** `d064d35c`. **Verdict:**
2 BLOCKING, 3 MAJOR, 0 MINOR — request changes. This is the last fix round (§9: 3 rounds); a
verification-only pass follows.

No registered case was run while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | The trusted step could read the validator from `HEAD` or append `|| true` and still pass | **Fixed.** D014 requires the exact trusted block (source ref `origin/main`, both validator calls, no masking) inside the "Check test manifest, skips, and assertion quality" step, with no `if:` or `continue-on-error`. Controls for `HEAD`, `|| true`, and removal. Residual: a PR that rewrites both the workflow and the validator is caught only by review; branch protection is the trust root |
| 2 | BLOCKING | A cell id could be repurposed to another form with the same counts | **Fixed.** D016 requires the baseline's cell identities as an unchanged prefix of the registry and every baseline artifact entry unchanged. Control added |
| 3 | MAJOR | `|| true` on the oracle command, or `if: false` on publish-nuget's test step, passed | **Fixed.** The oracle step may not contain `||`; every workflow job that runs a registered project is rejected when skipped, allowed to fail, or swallowing an exit status (`trap` cleanup excepted). Controls for both |
| 4 | MAJOR | Values of an `invalid` profile were discarded | **Fixed.** Values from an invalid profile (or attempt) are compared and can produce `DISAGREE`; they never count toward agreement. Control added |
| 5 | MAJOR | An unknown mode bypassed the single-execution rule | **Fixed.** `decide` rejects modes outside `execution` and `control` (INVALID); the CLI restricts `--mode`. Control added |
