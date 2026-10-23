# Codex review — round 2 (0.25 F3, #847)

- **Reviewer:** OpenAI Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family. Reduced independence; not an independent human review.
- **Input:** `git diff origin/main...HEAD` (evidence data and the scorecard baseline excluded) with a round-2 prompt: verify the 7 round-1 fixes and the compound-assignment fix, try variants that slip past them, and re-check name resolution, captures, effect laundering, verifier soundness and silent behavior changes.
- **Verdict:** **none issued.** Both round-2 runs ended before a final answer:
  - Run 1 (input at `c215c7bb`, 2026-10-07) stopped on a Codex usage limit.
  - Run 2 (input at `c215c7bb` + the run-1 fixes, launched after the reset on 2026-10-07 17:57) stopped on a Codex content-filter error after 159,890 tokens.
- Both runs logged confirmed reproductions in their progress notes before stopping. Every one was reproduced locally, fixed, and given a regression test. Round 3 reviews the result.

## Findings taken from the progress notes

| # | Run | Finding (as reproduced) | Disposition |
|---|---|---|---|
| 1 | 1 | An alias `§B{Func<i32, i32>:f} Help` invoked inside an escaping lambda read a same-named pure **field** `Help` in the effect inferrer, not the printing local function. | **Fixed.** The inferrer asks `IsLocalFunctionName` before the alias's AST type search; the method-group-argument path does the same. Test `Review2_AnAliasOfALocalFunction_IgnoresASameNamedField`. |
| 2 | 1 | An enclosing **pattern variable** (and other declarations that are not `§B`) was missing from the emitter's bare-name set, so a capture inside a local function could be qualified to a same-named module function instead of reaching C# as CS8421. | **Fixed.** The bare-name set holds every name the enclosing callable declares: bindings, parameters, lambda parameters, pattern variables, loop, catch and fixed variables. Test `Review2_AnEnclosingPatternVariable_IsNeverQualifiedToAModuleFunction` (CS8421). |
| 3 | 2 | The expression-call spelling `§C Help §A x §/C` in an escaping lambda read a same-named field's row (the binder does not attribute expression calls to a local function). | **Fixed.** A call the binder did not attribute that may still name a local function is charged both the local-function use and the ordinary resolution. Test `Review2_ExpressionCallSpelling_IgnoresASameNamedField` (Calor0424). |
| 4 | 2 | `§R (? c Help Help)` returned an effectful local function through a pure return row with no diagnostic: the row checker had no source row for a conditional. | **Fixed** (also covers method groups in general). A conditional value's row is the join of its branches; a branch that is not a nameable function value counts as Unknown. Test `Review2_AConditionalReturningALocalFunction_CarriesItsRow` (Calor0424). |

Run 2's notes also report that "the original witnesses are covered by targeted fixes" for all 7 round-1 findings.

After these fixes, F3 was split into two stacked draft PRs under the 0.25 PR-size ceiling: #1519 (the construct) and the converter PR. Round 3 and the verification pass cover both.
