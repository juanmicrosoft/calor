# Amendment 1.2.0, S1 conforming re-run and C2 determinism budget — review round 3 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...95706d49` (PR #1484). Brief: verify the round-2 fixes, including recomputing both registered hashes with the framed encoding. Then check for regressions and repeat the full hostile check.

## Codex output (verbatim)

No BLOCKING, NON-BLOCKING, or NIT defects found.

Independently recomputed both framed hashes from `contract.json`; they match the validator’s registered constants:

- S1 exception: `166a99f1bdda97509c3c29f329c69420ef405a84c8a94d5c67cd34078024df77`
- Charge rule: `603d579b53a09c0d8e756817b7fbe5acfee2b97a8bdbd6a5fced854132ae03d0`

The boundary-moving mutation now changes the hash. The multiline CRLF control discriminates. The charge-rule fallback preserves ordinary PR controls’ existing allocation.

The full hostile check found no authorization beyond A/B: run-1 findings and statuses remain preserved; restarts consume the fixed allowance; other harness changes require another amendment; disagreements become findings. B remains bound to the manifest candidate and regeneration ledger, with no added total capacity.

C011 matching, text binding, and duplicate identities are consistent. `ProposedContract` restores the affected closure text. Versions, C010 fields, Markdown/JSON agreement, and all three packet hashes check out; no new placeholders were found. The inspection statement accurately acknowledges decision-bearing inspection and the recorded S1 triggers.

Validation used source inspection and read-only reproductions; I did not execute the .NET tests.

APPROVE

## Disposition

APPROVE, with no findings. No change was made for this round; this commit adds only this record.
