---
name: confidence
description: >-
    Critically assess the work done so far in this session and report a confidence level
    (Low / Medium / Medium-High / High) with an honest gap analysis. Use when the user asks
    "how confident are you", "is this done", "assess this", or otherwise wants a self-review
    covering completeness, robustness, test coverage, and correctness.
user-invocable: true
---

# /confidence - Assess Work Completeness

When this skill is invoked, critically evaluate the work you have done in this session. Be honest and direct.

## What to Assess

1. **Completeness**: Does the implementation fully address what was asked? Are there any unfinished pieces?
2. **Robustness**: Are edge cases handled? Could this break under reasonable conditions?
3. **Comprehensiveness**: Is the test coverage adequate? Are all affected code paths exercised?
4. **Correctness**: Does the logic actually do what it claims? Are there subtle bugs?

## How to Respond

- State your confidence level: **Low**, **Medium**, **Medium-High**, or **High**
- List what works well with brief justification
- List any gaps, concerns, or limitations honestly
- Do NOT sugarcoat or hedge excessively — give a direct assessment
- If there are gaps, suggest what would close them
