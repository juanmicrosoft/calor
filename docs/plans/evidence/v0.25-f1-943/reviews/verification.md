# F1 #943 — Codex verification-only pass

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced. Invocation 4 of 4: the budget (3 rounds plus 1 verification
  pass) is now spent; no further Codex invocation was made.
- Input: `git diff origin/main...HEAD -- src tests` at `13fa2753` (round-3 fixes), with the three
  round records. Scope: confirm each of the 22 earlier findings and report only new BLOCKING defects.
  Codex replayed the 57 registered Calor cases through MCP (all match) and compiled the 10 conversion
  reproducers; VSTest could not run in the sandbox.
- **Verdict: REQUEST CHANGES**, for one reason: R3-5 was INCOMPLETE because its witness was missing.
  21 findings VERIFIED; **no new BLOCKING defects**.

| Findings | Status |
|---|---|
| R1-1 … R1-7 | VERIFIED |
| R2-1 … R2-8 | VERIFIED |
| R3-1 … R3-4, R3-6, R3-7 | VERIFIED |
| R3-5 (`§IDX2D` on a class with a two-index indexer) | INCOMPLETE: "Implementation fixed; regression coverage missing." Codex replayed the refusal (exactly `Calor0212`; an `i32[,]` control compiles) and asked for that pair to be committed. |

## Disposition after the pass

The pair Codex gave was added as
`InvalidOperand_IsRefusedBeforeCodegen_AndItsControlCompiles("two-index indexer element (Codex R3-5)")`
and passes. This is a test-only change; the implementation is the one Codex verified. Because the
budget is spent, the added witness itself was not re-verified by Codex; the maintainer's review is
the remaining check.
