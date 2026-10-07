# Amendment 1.3.1, revert-only changes for #1502 and #1503 — round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...f37dbb88` (PR #1504). The prompt gave the maintainer decision verbatim. It asked the reviewer to check five things against the git history: fidelity to the decision, honesty (round counts, findings, pinned tests, the "no outcome stronger" claim given the reopened #1502 `while` residual), consistency with the frozen 1.3.0 texts, validator correctness, and packet hygiene.

## Codex output (verbatim)

VERDICT: APPROVE

**MINOR — Broad negative controls can pass on unrelated C011 failures.** At `tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs:1653`, all 22 mutation cases assert only the violation code. They would pass with the pre-amendment validator because the unchanged #1502/#1503 exceptions already produce C011 as unregistered entries. Assert that the pristine fixture validates before mutation, then check the mutated subject and expected violation. The committed positive control and specific structural-message controls make this non-blocking.

No BLOCKING or MAJOR findings. The cited history supports the hunk descriptions, base SHAs, review counts, and test claims. Value 5 is correct. The #1502 residual is disclosed accurately relative to main. Frozen 1.3.0 entries remain unchanged; packet hashes, text bindings, versions, duplicate-key checks, and +31 manifest accounting check out.

Tests could not execute: the read-only sandbox prevented MSBuild from creating its temporary directory.

## Resolution

| Finding | Disposition |
|---|---|
| MINOR broad negative controls | Fixed (test only). `RevertOnlyExceptionOtherThanTheRegisteredOneFails` now asserts that the committed contract validates with no violation. It also asserts that each mutation yields C011 on its own subject: the mutated PR, #1497 or #1498 for a moved or copied exception, and the duplicate-exception subject. Without the 1.3.1 validator change, the pristine-contract assertion fails, so no control can pass on the unregistered-exception C011. The case count is unchanged (+31). |

No contract text, hash, or validator rule changed. A verification-only pass on this fix follows.
