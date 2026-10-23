# 0.25 release prep: Codex verification pass

- **Invocation 4 of at most 4** (3 rounds plus 1 verification pass). The review budget is spent.
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `9c6202b5d` (the round-3 fixes).
- **Verdict:** VERIFIED. Both round-3 findings fixed; no BLOCKING, MAJOR or MINOR regression.

Open after review: none from Codex on this PR's code or text. The four freeze blockers in
`../README.md` ("Open questions") were escalated in rounds 1 and 2 and await maintainer decisions.

## Codex output (final message, verbatim)

```text
Reviewed HEAD 9c6202b5d.

1. VERIFIED — round-3 finding 1. ReleaseCandidateInvalidation.cs:141–149 uses the injected runner. Exit 0 includes the landing itself and every ancestor, including merged branch commits; exit 1 covers every non-ancestor; other exits record an error and force UNDECIDED. Successful-call behavior matches the previous implementation, preserving earlier correct verdicts. A read-only comparison across 29 actual commits, including 23 outside the first-parent chain, confirmed identical phases. The phase theory row intercepts only (target, landing); removing its guard makes the row fail.

2. VERIFIED — round-3 finding 2. ReleaseCandidateInvalidationTests.cs:189–205 isolates both blob guards. The manifest row calls FindLanding directly; removing its guard returns the landing without errors, failing assertions. The downstream-record row confirms an intact landing, then requires the diff-specific blob error; removing that guard fails its assertion.

Additional checks:

- Manifest correct: 5 facts + 41 theory rows = 46 new cases; 14493 + 46 = 14539, matching expectedTotal.
- Generator unchanged: compaction preserves evaluation and conditional precedence. Old/new helpers matched across 36 git() and 8 sha() cases, including errors, binary output, UTC environment, CRLF normalization, and invalid UTF-8. Generator self-tests passed.

No BLOCKING, MAJOR, or MINOR regressions found. Guard-removal checks were by inspection; xUnit was not run because its scratch repositories require filesystem writes. No files modified.

VERDICT: VERIFIED
```
