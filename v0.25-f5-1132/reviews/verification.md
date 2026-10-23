# Codex verification-only pass (#1132 F5)

- Reviewer: OpenAI Codex CLI 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`,
  input `git diff origin/main...HEAD` at `2ab9784d` (evidence data excluded). Cross-family, reduced
  independence. The first attempt hit the Codex usage limit; the run waited for the window to reopen.
- Verdict: **REQUEST-CHANGES**. This was the last review the R0 capacity allows (3 rounds plus one
  verification-only pass). The fixes below were made after it and have **not** been reviewed by
  Codex; the maintainer decides whether that is acceptable or the family is re-scoped.

| Round-3 finding | Verification | Note |
|---|---|---|
| 1 Array extension effects | NOT-VERIFIED | Member table matched names only: an extension `GetLength(this int[], string)` was certified pure |
| 2 Element conversions / method groups | NOT-VERIFIED | `{ L.S(1), new X(L.T(2)) }`: a hoisted constructor argument ran ahead of the first element's conversion (`stcc`, not `sctc`) |
| 3 Multi-dimensional index order | VERIFIED | |
| 4 ref/out/in address order | VERIFIED | |
| 5 Jagged rank order | NOT-VERIFIED (coverage) | Code fixed; the test did not exercise a declared `int[][,]` with a bare initializer |
| 6 Empty-level constant sizes | VERIFIED | |
| 7 Implicit rectangular creation | VERIFIED | |
| 8 SetValue boxing | VERIFIED | |
| 9 Escaped-name witness | VERIFIED | |
| Round-2 #6 Initialize | NOT-VERIFIED (regression) | Restoring the manifest let a receiver declared `System.Array` answer `Initialize` as pure |

Earlier-round spot checks (round 1 and round 2 fixes) were otherwise VERIFIED, and the README's 35/35
per-surface results were confirmed against its data and the `src` tree.

## Post-verification changes (unreviewed)

- Array elements are never hoisted, in statement position too (`RenderInPlace`); a multi-line
  statement element is re-indented with its line. Witness: `round3` row (`sctc`, anonymous object).
- `EffectResolver.IsArrayInstanceMember` matches argument count and integer index arguments, so an
  extension named like an array member stays unknown; `System.Array.Initialize` is unknown again.
  Witness: `ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure`.
- Finding 5 coverage: the `round3` witness now declares `int[][,] dj = { … }` with a bare initializer.
