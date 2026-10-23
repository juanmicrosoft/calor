# Amendment 1.3.2, #1502 overflow decision by rule with no solver — round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...e1bb7275` (PR #1507). The prompt gave the background, the maintainer decision of 2026-10-06 ("Amend: no solver, rule-based"), and the requested registration verbatim. It asked the reviewer to scrutinize these drafting choices:

- whether term simplification is allowed;
- whether existing tests may change only from Proven to Assumed;
- the "at most Assumed" wording;
- the value 6;
- keeping `amendment` 1.3.1 with `TextAmendment` 1.3.2.

It also asked for the validator, negative controls, and packet hygiene.

## Codex output (verbatim)

VERDICT: REQUEST-CHANGES

- **MAJOR — Condition 2 extends determinism beyond the no-overflow decision.** [contract.json:337](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a0bc5a112e25cb6c6/docs/plans/evidence/evidence-contract-1407/contract.json:337) requires “neither verdict can depend on any solver outcome”; [the document:685](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a0bc5a112e25cb6c6/docs/plans/v0.24-evidence-contract.md:685) likewise requires each verdict to remain identical whatever any solver answers. Condition 1 expressly retains normal verification, whose answer still determines Proven, Assumed, refutation, or an undecided result. Removing the overflow probe cannot satisfy this broader requirement. Scope condition 2 to the **no-overflow classification**, explicitly preserving normal postcondition verification, then update the registered text hash and packet hashes.

The other scrutinized choices are acceptable: term simplification performs no satisfiability check; Proven→Assumed expectation changes follow the rule; “at most Assumed” preserves ordinary refutation and undecided outcomes; six passes is correct; retaining `amendment: 1.3.1` with `TextAmendment: 1.3.2` follows the existing registration model.

Packet hashes and registered text hashes match. Changed JSON files have no duplicate keys. Versions, PR #1507, timestamp, and the +8 manifest delta agree. #1503 and base ceilings are unchanged. Static inspection confirms the negative controls mutate registered text or explicitly checked fields.

xUnit was not run: the checkout is read-only and has no built test assembly.

## Resolution

| Finding | Disposition |
|---|---|
| MAJOR condition 2 over-broad determinism | Fixed. Condition 2 of #1502's exception now pins the **no-overflow classification**. A form the width rule cannot decide is classified as able to overflow, so its postcondition is at most Assumed (checked-arithmetic). A form it decides is classified as unable to overflow. That classification cannot depend on any solver outcome. The normal verification of the postcondition itself, which may still use the solver, is stated as unchanged. The §9 text says the same. The registered text hash is now `bb1f5e48…`, and the packet hashes are updated. Tests and manifest are unchanged: 400 pass with 0 warnings. |

A second round follows.
