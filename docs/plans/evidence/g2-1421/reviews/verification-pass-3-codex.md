# G2 #1421 — verification pass 3 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Scope: commit `f08b7caa` (fixes for the 2 new BLOCKING defects in verification-pass-2), and any NEW BLOCKING it introduces. Run by the session lead.

## Codex output (verbatim, local link targets kept)

1. **RESOLVED — quoted/flow/anchor YAML gate keys ignored.** The [parser](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a943f71a9aa2a564d/scripts/determinism_protocol.py:76) records unsupported keys as `__unparsed__`; gate checks reject that marker on jobs and steps. All 36 in-memory mutations covering quoted keys, flow mappings, anchors, and merge keys across the six registered gates produced **D014 only**.

2. **RESOLVED — shard scripts skip tests via `exit 0` or `if false`.** The [hash check](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a943f71a9aa2a564d/scripts/determinism_protocol.py:173) compares each shard script’s nonblank lines against its frozen `scriptSha256`. Both mutations, independently applied to both workflows, produced **D014 only**, explicitly reporting a frozen-hash mismatch.

**NEW BLOCKING defects introduced by f08b7caa: none found.** The registered residual was excluded.

Validation: the committed validator returned no violations; all **22 registration tests passed**. The full suite ran 33 tests: 31 passed, and two errored because the read-only sandbox prevented temporary-file creation.

VERDICT: CLEAN

## Disposition

Verdict CLEAN. Both defects resolved; no new BLOCKING. The two test errors are the read-only sandbox's temp-file restriction; the suite passes locally (33/33).
