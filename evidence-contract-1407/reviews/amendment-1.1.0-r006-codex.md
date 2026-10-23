# Amendment 1.1.0 merge — R006 contract-version rule (Codex review)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed commit: `402834bd699e0ca825e8918a004ce311f1e1649f`. Context: merging main (with #1476) into this branch made R1's R006 fail, because R006 required the registration's contract version to equal the current one.

## Codex output (verbatim)

No objections.

The version selects no historical rules. R005 still uses the current contract’s ceiling; R006 and R016 use its baseline commits; R008 uses its disposition vocabulary. Claiming an older version therefore cannot restore weaker rules.

Unrecorded versions fail R006. Amendment-log integrity remains covered by C010 and contract-packet hash checks. This check establishes version membership, not when registration occurred; merge and packet provenance remain responsible for that.

Amendment 1.1.0 introduces no conflicting R1 requirement that this change bypasses. No other R-code is weakened.

Tests were inspected, not executed, because the workspace is read-only.

VERDICT: CLEAN

## Disposition

Verdict CLEAN. No change made.
