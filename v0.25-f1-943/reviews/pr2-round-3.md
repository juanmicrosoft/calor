# F1 #943 PR 2 (#1571) — Codex adversarial review, round 3

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 3 of the PR 2 budget (the
  last full round; one verification pass remains).
- Input: `git diff origin/main...HEAD` at `14654cc24` (draft 3: `Parser.ParseBindStatement` read an
  initializer only on the binding's line; call statements native; chained assignments preserved;
  round-2 dispositions). Codex ran MCP probes through the Release build; it modified no repository
  file.
- **Verdict: REQUEST CHANGES** (1 BLOCKING). Codex confirmed the evidence hashes, the T1 seal, the
  ledger deltas, the 48-line count and that no source file was added.

## Findings and dispositions

| # | Finding (Codex) | Severity | Disposition |
|---|---|---|---|
| P2R3-1 | The parser rule silently changes valid hand-written Calor: `§B{n:i32}` followed by `§C{System.Math.Abs} §A INT:-7 §/C` on the next line was `n = 7` on `main`; it became `int n = default; System.Math.Abs(-7);` and returned `0`, with no diagnostic. An initializer beside the closing brace of a multi-line header was dropped the same way, and a previously rejected type mismatch became accepted. | BLOCKING | Fixed by reverting the parser change (and the #1104 cycle fixture edit that depended on it). The defect is fixed in the converter instead: every lowered out binding carries an explicit `default` initializer (draft 1's form, which the converter already uses for `int x;`), so no following statement, call or `§ARR` alike, is read as its initializer. Round 1's `@default` finding is closed by preserving the member when a symbol named `default` is visible; round 2's chained assignment stays preserved. Witnesses: `HandWrittenNextLineInitializer_KeepsItsMeaning` (Codex's reproducer, returns `7`), `OutDeclaration_IsLoweredWithAnExplicitDefault`, `P2-out-var-member-named-default` (now `preserved`), `P2-out-var-array-declaration`, `P2-OUTVAR-CALL-STATEMENT`, `P2-OUTVAR-DEFAULT-MEMBER`. |

## Effect of the round on the PR

Non-test lines 47 (src 34, CHANGELOG 13). Ledgers regenerated from `main`
(`RoslynSelectedAttempted` 34,371; opaque boundaries 148, expressions 7,746; per file in the README),
F1 evidence re-run (five P2 rows), T1 amendment 1.0.1 re-sealed.
