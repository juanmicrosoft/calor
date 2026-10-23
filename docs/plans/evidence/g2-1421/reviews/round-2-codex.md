# #1421 G2 determinism protocol — adversarial review round 2 (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, diff
against `origin/main` on stdin (`cases.json` on disk). **Reviewed commit:** `261fd303` (after the
split into two G2 PRs). **Verdict:** 4 BLOCKING, 5 MAJOR, 0 MINOR — request changes.

No registered case was run while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | An `invalid` attempt's values counted toward `AGREE-PASS`; such an execution returned DETERMINISTIC and complete | **Fixed.** Invalid-attempt values are compared (they can reveal `DISAGREE`) but never count toward agreement (`establishing` count); `complete` is false when any attempt is invalid, infrastructure-failure, environment-violation, or missing. Control added |
| 2 | BLOCKING | Inconsistent records accepted (exit 19 with `0|Completed`; no environment check; timeout profile with passing invocation) | **Fixed.** Every non-infrastructure record needs an environment attestation (`violations` list, `observed` object); a profile's status, exit code, invocation, and fill values must agree; the attempt status must equal the precedence of its profile statuses. 4 controls added |
| 3 | BLOCKING | An unresolvable baseline ref or partial packet silently disabled D016 | **Fixed.** `load_baseline` fails closed unless the ref resolves to a commit; it returns "no packet" only when the packet directory is absent at the ref; a partial packet fails. Control added |
| 4 | BLOCKING | D016 compared case keys only; multiplicity or oracle-profile reductions passed | **Fixed.** D016 compares the contribution matrix (every case's expected contributions per environment, with test multiplicity) and each row's cases and environments. Controls for multiplicity and `oracle=false` |
| 5 | MAJOR | `if: false` on the oracle step and removal of the D016 invocation passed; candidate code judges itself | **Fixed.** D014 rejects a conditional or masked oracle step and requires the trusted step. `calor-first-guard` now runs main's copy of the validator against the PR tree (`--root .`) before the tree's own copy. A PR that edits the workflow to drop both remains visible only to review (recorded residual) |
| 6 | MAJOR | C# recorder and `CaseResult` schema outside the freeze | **Fixed.** `DeterminismRecord` moved to its own file; it and `DifferentialModels.cs` are in `sha256.json` (D015); the three call sites are checked (D014). `ReportWriter.cs` is unchanged from `main` |
| 7 | MAJOR | Cells missing verdict fields accepted | **Fixed.** Exactly the 13 `CaseResult` fields with their types (`cases.cellFields`); otherwise `Malformed` and INVALID. Control added |
| 8 | MAJOR | Decider still pooled several executions | **Fixed.** Execution mode takes exactly one execution id and run id; CLI takes one of each; pooling is INVALID. Control added |
| 9 | MAJOR | Timing-sensitive set had no decider semantics and accepted any amendment string | **Fixed.** D011 keeps the set empty; adding semantics is a protocol amendment with a #1407 amendment and a complete new execution |
