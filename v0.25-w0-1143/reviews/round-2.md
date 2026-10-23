# Codex review — round 2

Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral`, reasoning effort high), on
`git diff origin/main...HEAD` at `9f99c5d1`. Round-1 fixes #3, #4, #7, #8 confirmed to hold; the
Part 2 proof confirmed to behave as documented. Verdict: **BLOCKING findings remain** (4 blocking,
4 should-fix).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `- ```calor` and nested-quote fences render as code but are not extracted | Fixed by failing closed: a fence on a list-marker or blockquote line is rejected (`Calor1334`, "Unsupported fence form"). The website has none |
| 2 | BLOCKING | An implicitly closed container fence (`> ~~~text` then a top-level program) swallows the next program | Fixed by the same rule, plus: an indented fence whose body dedents past its opener, and an unclosed fence, are rejected |
| 3 | BLOCKING | A warning-only negative (`expect=Calor0417`) was excluded from the positive retry, so its own `Calor1002` stayed hidden behind a lexer-failing member | Fixed. The retry now keeps every member that reported no error (positive or warning-only) and drops only members that reported errors. Verified with the CLI that a lexer failure in one file suppresses generated-C# validation and the cross-module pass for the rest; test `WarningOnlyNegativeCannotHideGeneratedCSharpErrors` |
| 4 | BLOCKING | Compact JSON (`{"success":false,…}`) bypasses the response-key heuristic | Fixed: keys are matched anywhere. Test added |
| 5 | SHOULD-FIX | Retry broke a positive member that depends on a warning-only callee | Fixed by #3 (warning-only members stay). Test `PositiveMemberMayDependOnAWarningOnlyNegative` |
| 6 | SHOULD-FIX | refinement-types fragment still proves over local `newBalance` (`Unsupported`) | Fixed: proof stated on the expression, with the solver limitation noted |
| 7 | SHOULD-FIX | Captured `obligations` response showed line 5; the displayed request yields line 4 | Fixed: re-captured from the exact displayed source (`line: 4`) |
| 8 | SHOULD-FIX | Repeated annotations (`expect=` twice) silently ignored | Fixed: any repeated key is rejected. Tests added |
