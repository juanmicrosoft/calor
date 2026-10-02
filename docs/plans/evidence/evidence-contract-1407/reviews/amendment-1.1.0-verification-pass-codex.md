# Amendment 1.1.0 — verification-only pass after round 3 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed head: `509a4039290288ca33f4f006fb17ba2c83d5cb93` (round-3 fixes, PR number #1477 filled in, packet re-hashed, C010 requires `reviewedInPr > 0`).

## Codex output (verbatim)

- Round 3 #1 — **RESOLVED:** `GlobRegex` uses `\A` and `\z`; both glob and D003 controls include a trailing-newline path.
- Round 3 #2 — **RESOLVED:** `PendingUpdateChangesNoArtifact` attempts stale-to-authoritative reclassification, compares the entire artifact after validation, and requires the evidence row to fail E005.
- Round 1 #4 — **RESOLVED:** The contract and document name #1477, C010 requires `reviewedInPr > 0`, and all three recomputed packet hashes match `sha256.json`.

**NEW BLOCKING defects since round 3: None found.** Verification used source inspection and hash recomputation; tests were not rerun in the read-only sandbox.

VERDICT: CLEAN

## Disposition

Verdict CLEAN. Round 1 finding 4 (placeholders) is resolved by the fill-in and the tightened C010. No change made.
