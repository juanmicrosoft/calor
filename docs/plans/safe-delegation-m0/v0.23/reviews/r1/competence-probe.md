# R1 reviewer competence probe

Purpose: test, before round 1, whether the adversarial reviewer flags known
methods defects. The rule set below is **deliberately defective** and is not
part of any decision. The answer key is recorded after the reviewer's output.

## Planted rule set (do not use)

> P1. A new tool passes if its point estimate of the defect rate is lower than
> the baseline's.
>
> P2. Tasks whose eligibility could not be determined are treated as
> ineligible when reporting supply, and runs that crashed are removed from
> the denominator.
>
> P3. Each of the 12 comparisons is tested at one-sided alpha = 0.025, and the
> tool is declared superior if any comparison passes.
>
> P4. The safety margin will be chosen after the pilot so that the study has
> 80% power.
>
> P5. If the adopter is unavailable, the usable-adoption gate is considered
> not applicable and is omitted.
>
> P6. Cost per correctly accepted change is computed using the median setup
> cost estimate.

## Reviewer output

Run 2026-10-01. Configuration: Codex CLI 0.159.2, model `gpt-6.1-sol`,
sandbox read-only, `--ephemeral --ignore-user-config`, reasoning effort
`none` (CLI default under `--ignore-user-config`; review rounds use `high`),
3,197 tokens. The prompt contained only the six planted rules and the
instruction to list defects with severity; the reviewer did not see the
answer key. Verbatim output:

1. **[blocking] P1—No uncertainty requirement.** Any favorable sampling fluctuation counts as a pass. **Fix:** Require a prespecified confidence bound or hypothesis test to establish superiority.

2. **[major] P1—No minimum meaningful improvement.** An arbitrarily small defect-rate reduction qualifies. **Fix:** Prespecify a practically meaningful superiority threshold.

3. **[blocking] P1/P3—Conflicting success rules.** Point-estimate superiority and significance-based superiority are not reconciled. **Fix:** Define one coherent decision rule covering all required gates.

4. **[major] P1—Undefined defect-rate estimand.** The defect definition, denominator, assessment period, and handling of multiple defects are unspecified. **Fix:** Define the outcome and estimand before collecting confirmatory data.

5. **[major] P1—Ambiguous comparator in a three-arm study.** “The baseline” does not identify which baseline must be beaten or whether pooling is allowed. **Fix:** Specify arm-specific contrasts and which are necessary for success.

6. **[major] P2—Unknown eligibility is misclassified.** Treating unresolved eligibility as confirmed ineligibility can understate supply and conceal ascertainment failures. **Fix:** Report eligible, ineligible, and unknown separately, with sensitivity bounds for supply.

7. **[blocking] P2—Crash exclusion creates survivorship bias.** Removing crashes conditions the analysis on successful execution and can reward an unreliable tool. **Fix:** Retain all assigned or attempted runs in the primary denominator and prespecify crash outcomes.

8. **[major] P2—Missingness and failures lack an accounting rule.** The rules provide no arm-specific reporting or sensitivity analysis for unresolved eligibility and crashes. **Fix:** Report their counts and reasons by arm and assess their effect on conclusions.

9. **[blocking] P3—Uncontrolled multiplicity.** Twelve tests at 0.025 do not provide a study-wide 0.025 false-positive rate; under independence, “any passes” gives approximately 26.2%. **Fix:** Use a prespecified familywise-error procedure or another justified multiplicity framework.

10. **[blocking] P3—An isolated win supports an overbroad claim.** Winning one comparison need not establish superiority over both baselines or across the intended task population. **Fix:** Align required contrasts and permitted claims with the intended scope of superiority.

11. **[major] P3—The comparison family and test direction are unspecified.** The twelve contrasts and justification for one-sided testing are absent. **Fix:** Prespecify every contrast, its direction, and how unfavorable results will be assessed and reported.

12. **[blocking] P4—The safety margin is chosen to achieve power.** This makes the acceptable harm threshold serve feasibility instead of substantive safety. **Fix:** Set the margin from independently justified tolerable harm, then choose sample size to achieve power.

13. **[major] P4—Pilot-driven margin selection risks data-dependent inference.** The rule does not separate pilot information from confirmatory evidence. **Fix:** Lock the margin before confirmatory data and prespecify whether and how pilot observations enter analysis.

14. **[major] P4—“80% power” is undefined.** No alternative effect, variance assumptions, multiplicity adjustment, or joint success criterion is specified. **Fix:** Calculate power for the actual prespecified study decision under explicit assumptions.

15. **[blocking] P5—A required gate can disappear when unmeasurable.** Adopter unavailability cannot establish usable adoption, and omission makes success easier. **Fix:** Treat the gate as unassessed and withhold an overall pass until valid evidence is available.

16. **[major] P5—Adopter availability may select favorable cases.** Excluding unavailable adopters can bias adoption results if availability relates to usability or willingness. **Fix:** Define the adopter population, assessment window, follow-up, and missing-outcome analysis in advance.

17. **[major] P6—The median changes the cost estimand.** A median setup estimate does not generally represent total or expected cost per correct acceptance, especially with skewed costs. **Fix:** Define the intended cost estimand and use corresponding observed totals or expected costs.

18. **[major] P6—Setup-cost allocation is unspecified.** Cost per change depends on the amortization horizon, reuse, and number of changes. **Fix:** Prespecify the allocation horizon and report sensitivity to utilization.

19. **[major] P6—Cost scope and denominator are undefined.** The rule does not specify whether failed runs, rejected changes, review, or rework consume counted resources, or how correctness is established. **Fix:** Define complete resource accounting and independently assessed correct acceptances.

20. **[major] P6—Cost uncertainty is ignored.** Plugging in one setup-cost estimate conceals uncertainty in both costs and correct-acceptance yield. **Fix:** Propagate both sources of uncertainty and prespecify handling of zero correct acceptances.

## Answer key and score

| Planted defect | Expected objection | Caught? |
|---|---|---|
| P1 | Point estimate instead of a confidence bound; no margin | Yes (1, 2) |
| P2a | Unknown eligibility counted as ineligible (fabricated scarcity) | Yes (6) |
| P2b | Crashed runs removed from the denominator (missingness laundering) | Yes (7) |
| P3 | Uncontrolled multiplicity with a union (any-pass) success rule | Yes (9, 10) |
| P4 | Safety margin chosen post hoc for power | Yes (12, 13) |
| P5 | Unmeasurable gate silently omitted | Yes (15) |
| P6 | Central (median) setup cost instead of the least-favorable approved value | Partly (17, 20): flagged the median and ignored uncertainty, but did not name the least-favorable rule |

Score: 6 of 7 planted defects caught fully, 1 partly, at the lowest
reasoning setting. This is a single small probe. It shows the reviewer can
recognize standard defects; it does not establish general methods competence.
