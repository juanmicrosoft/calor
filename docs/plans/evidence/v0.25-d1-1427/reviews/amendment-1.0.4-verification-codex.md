# D1 #1427 amendment 1.0.4 (PR #1559) — Codex verification-only pass

- **Reviewer:** OpenAI Codex CLI 0.163.0 (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`). Reduced independence per R0 §9; this record does not claim independent review.
- **Budget:** PR #1559 gets a fresh budget (3 rounds plus one verification pass). At the maintainer's direction it uses one verification-only pass and no adversarial rounds. #1518's budget (`round-1…3-codex.md`, `verification-pass-codex.md`) was already spent.
- **Input:** `git diff origin/main...HEAD` at commit `45608243` (amendment 1.0.4 with the PR number recorded).
- **Prompt:** verify by source inspection that (1) the corrected D1-LV-03 source witnesses RO-LV-2 for a property (parser attribute order for `§PROP`, binder refusal with `Calor0212`); (2) the amendment meets D009 and changes nothing else that is sealed; (3) the contract document, registry, F1 evidence README and CHANGELOG agree and make no independence claim; (4) `RefOutImplementationTests`' property case agrees with the registry. Report only BLOCKING problems.

## Result: one BLOCKING finding, fixed

Codex confirmed points 1, 2 and 4: the parser reads `§PROP{p1:N:i32:pub}` as property `N` of type `i32`, the binder refuses a property operand with `InvalidByReferenceOperand` (`Calor0212`), and the expected outcome is unchanged.

| # | Finding | Disposition |
|---|---|---|
| 1 | `docs/plans/evidence/v0.25-f1-943/README.md` line 8 still named the contract as 1.0.3, while its updated 57-of-57 claim depends on 1.0.4. | Fixed: the line says F1 was specified against 1.0.3 and that amendment 1.0.4 later corrected D1-LV-03's source. |

No further Codex pass was run; the fix is a one-line wording change. The author ran `dotnet test --filter "FullyQualifiedName~RefOut"` (420 passed) and the full `Calor.Compiler.Tests` project. The maintainer, @juanmicrosoft, makes the reviewer judgments on adoption, citing this record.
