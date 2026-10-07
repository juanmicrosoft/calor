# F2 #906 — Codex adversarial review, round 3 (last full round)

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced.
- Input: `git diff origin/main...HEAD` after the round-2 fixes.
- **Verdict: NOT ACCEPTABLE** (2 BLOCKING). Both findings were loud failures (the generated C#
  did not compile, `Calor1002`), not silent behavior changes, but conversion had reported success
  with zero interop.

## Findings and dispositions

| # | Severity | Finding (Codex) | Disposition |
|---|---|---|---|
| R3-1 | BLOCKING | `$"{Math.Max(1.5, x + 1)}"`: the converter kept the hole native, but the parser's literal check rejected the `double` argument, so the call stayed raw C# with Calor `(+ x 1)` inside (CS1073). `String.Concat(null, "a" + "b")` failed the same way. | Fixed. The parser's `SameLiteralValue` accepts `double` (not decimal/single). The converter's call-argument rule (`IsNativeHoleArgument`) excludes what lifting cannot read back: `null` and member access on a non-name (`typeof(T).Name`); those holes are kept as C#. Witnesses: shapes cases 8 (native: `Math.Max(1.5, x + 1)`, `Pad("ab", -x + 5)`, `x2(0.5)`) and 9 (preserved: `null` argument, `typeof(Holder).Name` argument). |
| R3-2 | BLOCKING | `$@"{x:000\kg}"`: the verbatim format clause was copied into a regular `$"..."`, where `\k` is an escape (CS1009). | Fixed. A backslash in a format clause of a verbatim or raw interpolated string escalates the expression (`string-interpolation-hole`), keeping the statement as C#. Regular strings keep their already-escaped clause. Witnesses: shapes cases 10 (verbatim, preserved) and 11 (regular `{x:000\\kg}`, native). |

The capacity ceiling allows 3 full Codex rounds plus one verification-only pass; the
verification pass is recorded in `verification.md`.
