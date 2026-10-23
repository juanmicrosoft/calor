# Amendment 1.3.2 report-totals correction (429/156, 6 changed + 6 already-Assumed cells, hashes pinned) — verification pass (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `09761fbd..4c787e1f`, with the full amendment `origin/main...4c787e1f` (PR #1507).

The correction encodes what the S2 agent's implementation of #1502's 1.3.2 change actually produced, at local commit `c04b97b3157beabd82f06d53b32c580347cfaa0c` on `bdb430db`. The regenerated #1135 reports total 429 Proven, 156 Assumed, and 585 Refuted, not 423/162. Of the 12 allowed gate cells, only the 6 `scalar-type:i64/u64` cells change from Proven to Assumed (checked-arithmetic). The 6 `array-element-type:i64/u64` cells were already Assumed (reference-model) and now carry both assumptions. The regenerated report SHA-256s are pinned: json `aa1f86f9…`, md `24901f21…`. The reviewer was asked to verify them against the `c04b97b3` blobs.

## Codex output (verbatim)

VERDICT: REQUEST-CHANGES

- **MINOR — Stale demotion count remains.** [eng/test-manifest.json:10](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a0bc5a112e25cb6c6/eng/test-manifest.json:10) still describes the option A extension as “demotions of 15 Proven verdicts.” The appended correction leaves that claim intact. Replace it with 9 Proven demotions: 3 test rows plus 6 scalar cells; the 6 array cells were already Assumed and gain checked-arithmetic.

All other requested checks pass: report hashes and 429/156/585 totals, the 6/6 split, narrow condition 2 scope, framed validator hash, discriminating controls, packet hashes, duplicate-key checks, manifest 12778→12780, and diff whitespace.

xUnit was not run: the sandbox is read-only and no built test assembly is present.

## Resolution

| Finding | Disposition |
|---|---|
| MINOR stale "15 Proven verdicts" in the manifest note | Fixed. The manifest changes only through `manifest_bump.py`, which appends to a note and does not rewrite it. So a delta-0 bump appends a correction: the authorized demotions are 9 Proven verdicts, not 15 (3 `ProductionOverflowRuntimeTests` rows and 6 `scalar-type:i64/u64` gate cells). The 6 `array-element-type:i64/u64` cells were already Assumed (reference-model) and gain checked-arithmetic. The `expectedTotal` is unchanged at 12780. No contract text, hash, or test changed. |

The pass allowed one verification-only check on this correction. Its only finding was a MINOR in a non-contract note, fixed as above. The contract text it verified is unchanged.
