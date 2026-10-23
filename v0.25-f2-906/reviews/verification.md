# F2 #906 — Codex verification-only pass

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced.
- Input: `git diff origin/main...HEAD` at `3f498ecec` (after the round-3 fixes).
- Scope: verify only the findings recorded in `round-1.md`, `round-2.md` and `round-3.md`; no new
  lines of attack.
- Method (as reported by Codex): code inspection and 14 MCP compile probes with auto-fix and
  effect enforcement disabled. Codex's sandbox blocked VSTest, so it did not compare runtime
  output; the runtime comparisons are the branch's own tests
  (`InterpolationTargetTypingTests`, 53 cases, passing).
- **Verdict: VERIFIED.**

| Finding | Status | Evidence (Codex) |
|---|---|---|
| R1-1 | FIXED | Hole-free `FormattableString` calls keep their original C# statement and compile. |
| R1-2 | FIXED | Native `String.Concat("${x}")` keeps the literal `${x}`; converted string arguments too. |
| R1-3 | FIXED | Indexing, conditional access, named/generic calls and `?:` become explicit `§CS{...}` holes; the combined witness compiles. |
| R1-4 | FIXED | Nested `Kind($"{x}")` keeps its original C# inside the hole and compiles. |
| R1-5 | FIXED | `Log($"a{x}" + $"b{x}")` keeps the complete handler call and compiles with both overloads present. |
| R2-1 | FIXED | Digit holes emit `${INT:n}` and regenerate `{1}`, `{3,4}`, `{7:D3}`; the single-hole FormattableString probe compiles. |
| R2-2 | FIXED | Decimal, long and integral-double arguments keep their C# literal types inside explicit interop holes and compile. |
| R2-3 | FIXED | Native `String.Concat("\x41")` regenerates the original C# escape without doubling the backslash. |
| R2-4 | FIXED | `feature-check string-interpolation-formattable` reports `partial` and names both limits. |
| R3-1 | FIXED | `Math.Max(1.5, x + 1)` lifts; `null` and `typeof(T).Name` argument witnesses keep C#; all compile. |
| R3-2 | FIXED | The verbatim backslash-format witness keeps its statement; the regular escaped-format control stays native; both compile. |

This pass verifies the recorded findings only. It is not an acceptance of the family: READY
needs the #1423 candidate, per-case evidence on it, and the approver's review.
