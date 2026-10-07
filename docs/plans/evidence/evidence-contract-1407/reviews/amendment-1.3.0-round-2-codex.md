# Amendment 1.3.0, S2 discovery demotion slot and #1496 review overrun — round 2 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...81184f1a` (PR #1501). The prompt repeated the round-1 hostile check in full. It also asked the reviewer to verify that the round-1 fix closes the gap and creates no new one. The fix freezes #1496's repair at `674e3bdf` before and after the final pass.

## Codex output (verbatim)

No BLOCKING, MAJOR, or MINOR defects found.

- **Round-1 MAJOR resolved.** [Conditions 1–3](docs/plans/v0.24-evidence-contract.md:597) bind verification to `674e3bdf…` and freeze the repair from that commit through merge, before and after the pass. Additional repairs before verification are prohibited; a pass requesting changes cannot start another fix-and-review cycle.
- **Scope remains exact.** A permits one additional #1413 PR, total 7, solely to demote the two named discoveries. B applies only to #1496, value 5, with exactly one final clean verification-only pass.
- **No unrelated weakening found.** The semantic JSON diff contains only A/B, version changes, and the amendment entry. Existing #1311/#1473 validation is preserved or stricter. The inspection statement, `weakens` list, and overrun description match the supplied S2 summary.
- **Validator checks hold on inspection.** Both framed text hashes match. All 36 new theory rows have effective mutations and reach the relevant C011 rejection branches. Restoring the round-1 freeze wording changes the registered hash.
- **Consistency checks pass.** Packet hashes, version 1.3.0, PR #1501, UTC timestamp, document/log entries, and inventory agree. Three facts plus 36 theory rows give **+39**, matching `12602 → 12641`.

C# and rejection paths were reviewed statically; compilation and xUnit execution were not performed in this read-only environment.

VERDICT: APPROVE

## Resolution

No change was requested. This approving full re-review of the round-1 fix is the final review. No content changed after it; only this record was added.
