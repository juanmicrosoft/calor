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

(Appended below when the probe runs.)
