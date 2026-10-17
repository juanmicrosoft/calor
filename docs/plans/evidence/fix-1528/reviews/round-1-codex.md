# Fix #1528 — Codex adversarial review, round 1

- Context: #1528 was split out of #1540 per the maintainer's 2026-10-17 decision. This PR has a fresh budget of 3 rounds plus 1 verification pass, used as 1 round plus 1 verification pass because round 1 is clean.
- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt plus `git diff origin/main...HEAD -- src tests CHANGELOG.md` on stdin.
- Reviewed commit: `93a43fbfb`.
- Verdict: **APPROVE WITH MINOR**. No BLOCKING or MAJOR findings, and no production regression in the changed paths. Codex traced the code; the read-only sandbox could not run tests.

## Findings and dispositions

| # | Finding | Severity | Disposition |
|---|---|---|---|
| 1 | The default-width rows can mask an integer substitution: an int `0` passes `default(float) + 2f` (a `Single`) and `default(decimal) + 2m` (a `Decimal`). | MINOR | Fixed in the tests. Standalone rows `return default(float);` → `Single:0` and `return default(decimal);` → `Decimal:0` check the boxed runtime type directly. |
| 2 | The interpolation row depends on the host culture. Under `fr-FR` the original program returns `"2,5"`. | MINOR | Fixed in the tests. The oracle runs both programs under `CultureInfo.InvariantCulture` and restores the previous culture afterwards. |

Raw output: `results-round-1-codex.md`.
