# #1419 R1 registration — adversarial review round 1 (Codex)

**Reviewer:** Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`,
run from the worktree root with the diff against `origin/main` on stdin (generated `cases-index.json`
and manifests omitted from the diff but readable on disk). The first attempt ended with "Selected
model is at capacity"; the retry completed.
**Reviewed commit:** `c21208cb`. **Verdict:** 11 BLOCKING, 4 MAJOR, 0 MINOR.

No verifier outcome for B1 or N1 was produced or read while addressing these findings. Template
changes were checked only by lexing, parsing, and compiling with verification off (error codes only).

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Rows could be clean with up to 20% harness-invalid cases; flaky/crashed unmapped | **Fixed.** `rowStatus`: any unexecuted, harness-invalid, flaky, crashed, unconfirmed, or still-timed-out case makes the row `INCOMPLETE`; no tolerance |
| 2 | BLOCKING | Exists claims (precondition satisfiability) had no adjudication; `R1-O1-reached` wrong for them | **Fixed.** Exists adjudication table in `oracle.semantics` (model replay, exhaustive no-witness = false proof, `spurious-model`, `unconfirmed-claim`, spurious refutation); non-vacuity for exists = the witness |
| 3 | BLOCKING | Whitelist coverage checked against template options, not rendered cases (array element types, `i16`, Power) | **Fixed.** R010 now checks generated case texts; `cycle`/`cyclePairs` holes with `minInstances` render every advertised member; Power and BitwiseNot get a refusal row `NUM-UNWHITELISTED-OPS` |
| 4 | BLOCKING | `§PROOF` probes used locals the obligation solver does not declare; index-bounds probes were public (Boundary) | **Fixed.** Parameter-only `§PROOF` templates; index-bounds functions private |
| 5 | BLOCKING | Field probes used the implicit receiver, which the adapted method does not declare | **Fixed.** Field rows use an explicitly declared object parameter (`p.Value`, inherited `p.Level`); receiver forms moved to refusal row `FLD-IMPLICIT-RECEIVER`; cache dependency probe uses the parameter form |
| 6 | BLOCKING | Cache probes could not discriminate stale reuse | **Fixed.** True-prime/false-final body and precondition pairs; field-width change judged warm-vs-cold; semantics-version probe tampers a primed `Failed` entry to `proven` (variant A must show `Proven`) and to `proven` plus a stale version (variant B must equal cold) |
| 7 | BLOCKING | Sampled domains omitted the case's own thresholds (e.g. `x == c`) | **Fixed.** Every numeric constant drawn into a case, with v-1 and v+1, joins its type's sampled column; constant-only tuples survive the 4,096 cap |
| 8 | BLOCKING | `T-IMPL-DIV` excluded the zero-divisor inputs it was meant to probe | **Fixed.** Division only in the implementer precondition; a throwing implementer check under the interface precondition is a violation |
| 9 | BLOCKING | Control mismatches had no consequence; P845 refuses the named ULONG shape, so the #845 pair did not discriminate | **Fixed.** Exact `expectedOutcome` per control; CTRL-POSITIVE/NEGATIVE are a solver-availability gate (mismatch invalidates the run, also on P845); other mismatches are `control-mismatch` findings blocking `VALIDATED` for the rows each control `guards`. #845 control rebuilt: 2 named-shape cases (documenting) plus 4 width cases that discriminate the defect #961 fixed (in-range `LONG:`/`UINT:` literals translated as signed 32-bit), with per-case P845 expectations from source reading |
| 10 | BLOCKING | No obligation probes for branch-scoped facts, loop bounds, assignment kill, sibling leakage, or reference forms in the ungated solver | **Fixed.** Rows `OBL-BRANCH-FACTS` (then, else, after-if leakage), `OBL-LOOP-FACTS` (inclusive bounds), `OBL-MUTATION-KILL`, `OBL-UNGATED-REFERENCE` (field, string, array forms; Assumed required) |
| 11 | BLOCKING | O1 discarded witnesses found before its budget ran out; invalid runs discarded candidates | **Fixed.** `IndependentOracle` keeps a conclusive partial verdict; invalid-run policy keeps false-proof candidates as `candidate-unconfirmed` and re-runs them first |
| 12 | MAJOR | CLI fallback cannot observe every outcome; implication direction ambiguous | **Fixed.** No fallback channel (in-process only; failure to load invalidates the run); CLI cross-check only checks for contradictions; IMPL templates carry one direction and the `Calor0815` text names it |
| 13 | MAJOR | Loop oracle used an exclusive bound; culture template negated an int | **Fixed.** Inclusive loop bound; `(>= (indexof …) INT:0)` on both sides. Refusal validation is now restricted to each refused row's frozen `frontEndRefusalCodes` |
| 14 | MAJOR | H1 cases missed the `int.MinValue` form and mixed signed literals with a u64 operand | **Fixed.** Two sign-consistent H1 templates (i64 and u64 operands) cycling every registered literal form |
| 15 | MAJOR | O2 replayed only violation witnesses, so D15-style emitter defects on non-violating inputs escaped | **Fixed.** O2 replays every witness plus the first 16 reached inputs; divergence in either direction is recorded |

While addressing #3 and #13, compiling the refused rows with verification off froze their front-end
codes and exposed one more template defect (`null` is not a contract identifier in `GEN-REFUSAL`),
fixed before the freeze. Net effect: 93 rows (was 87), 710 cases (was 708), 32 controls per
baseline (was 30), 6 P845 executions (was 4), reserve 74 (was 80); total still 1,500.
