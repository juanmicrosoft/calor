# #1419 R1 registration — adversarial review round 3 (Codex, final round)

**Reviewer:** Codex CLI 0.159.2, same invocation, asked to verify the round-1 and round-2 fixes in code
and data. **Reviewed commit:** `a81ffe7d`. **Verdict:** 5 BLOCKING, 4 MAJOR, 0 MINOR.

This was the last review round the gate allows (3 per PR). Every finding below was addressed before
the freeze; the fixes were **not** re-reviewed by Codex. No verifier outcome for B1 or N1 was produced
or read while addressing them.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `T-OBL-INDEX` oracle checked `k < a.Length`; the obligation is `0 <= k < n` over a size witness the template did not declare | **Fixed.** Template declares `§I{i32:n}`; O1 checks `k < n`; replay builds an array of length n |
| 2 | BLOCKING | Precondition-satisfiability row had only satisfiable cases | **Fixed.** `T-VAC-PREUNSAT`: unsatisfiable-by-construction preconditions over exhaustive 8-bit domains (Proven = false proof). Satisfiability over division, checked arithmetic, string, array, and quantifier forms explicitly excluded (`XCL-PRECONDITION-SAT-FORMS`; precondition outcomes never remove a guard) |
| 3 | BLOCKING | Calor1002/Calor0326 arise after verification, so a rejection could hide an already-produced proof | **Fixed.** Every produced claim-bearing outcome is adjudicated even if compilation then fails; a rejection counts as a refusal only when no claim-bearing outcome exists |
| 4 | BLOCKING | Type checking adjustable through `CALOR_NO_TYPE_CHECK` | **Fixed.** `EnableTypeChecking = true` explicitly; the variable must be unset in harness and CLI environments, is recorded, and a change invalidates the run |
| 5 | MAJOR | Constants written in pair holes (H1 pinned cases) were not anchors, so the pinned input never reached the claim | **Fixed.** Templates may register explicit `anchors`; the H1 pinned template anchors -1, 4294967295, 4294967290 |
| 6 | BLOCKING | Quantifier bound sorts u16, u32, u64 not rendered | **Fixed.** Bound-type hole cycles u8, i8, i16, i64, u16, u32 with per-sort coverage entries. A u64 bound variable against the sweep's signed bounds has no C# typing (CS0034, observed with verification off); it is explicitly excluded in `XCL-QNT-NONNUMERIC-BOUND` |
| 7 | MAJOR | O2 replay undefined or meaningless for implication claims | **Fixed.** O2 is not applicable to implication and precondition claims and is recorded as not-applicable; O1 still adjudicates |
| 8 | MAJOR | O2 recognized only two guard exception types | **Fixed.** Guard exception by claim site (ContractViolationException, InvalidOperationException, ArgumentOutOfRangeException, IndexOutOfRangeException); other exceptions recorded as `o2-other-exception`, never divergence |
| 9 | MAJOR | A non-discriminating #845 control had no consequence | **Fixed.** If no width case discriminates on a baseline, the guarded rows are INCOMPLETE there until an amendment registers a replacement control that passes |

**Final BLOCKING count after dispositions:** 0 open by the registrant's assessment; the round-3 fixes
themselves are unreviewed by Codex because the review-round ceiling was reached. Net effect: 100 rows
(7 excluded), 710 cases, 1,500 executions.
