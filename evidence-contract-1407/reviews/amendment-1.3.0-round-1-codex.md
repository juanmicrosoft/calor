# Amendment 1.3.0, S2 discovery demotion slot and #1496 review overrun — round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...6759bc53` (PR #1501). The prompt asked for a hostile check of five things:

1. Only decisions A and B changed in substance.
2. Their scoping is exact.
3. The inspection statement and the `weakens` list are honest.
4. The validator binds both exceptions by SHA-256, and the negative controls work.
5. Versions, the amendment log, hashes, and the test-manifest delta are consistent.

## Codex output (verbatim)

- **MAJOR — Decision B allows additional repairs before the final pass.** [Conditions 1–3](</Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a4c584065e11a13e1/docs/plans/v0.24-evidence-contract.md:597>) require a final diff that *includes* commit `674e3bdf…`, but freeze compiler/test changes only **after** verification. An author can append another compiler fix before that pass, obtain APPROVE, and satisfy the literal conditions. The prohibition on further fixes applies only if the pass requests changes. This exceeds authorization to verify the existing last fix and undermines the claim that B adds no repair capacity. Explicitly freeze the repair at the named commit before verification, then update the registered text hash and controls.

Otherwise, the semantic JSON diff contains only A/B and version/log changes. A’s scope is exact; earlier exception validation is preserved or stricter. Packet hashes and framed exception hashes match. Versions, PR number, UTC timestamp, and the **+38** test-count delta are consistent. The overrun description matches the supplied S2 summary.

C# and negative controls were inspected statically; compilation and xUnit execution were not performed in the read-only environment.

VERDICT: REQUEST-CHANGES

## Resolution

- **MAJOR (B allows repairs before the final pass): fixed.**
  - Condition 1 now defines the pass as a verification of the repair as of its last fix (`674e3bdf`).
  - Condition 3 is now "Repair frozen at the last fix". From `674e3bdf` until merge, before and after the pass, the PR's changes under `src/` and `tests/` stay exactly as they are at that commit.
  - The registered text hash of the #1496 exception is updated, and the document's §9 conditions match.
  - The positive control asserts the new condition. A new negative control (`repair-not-frozen-before-pass`) restores the round-1 text and fails `C011` (+1 case).
