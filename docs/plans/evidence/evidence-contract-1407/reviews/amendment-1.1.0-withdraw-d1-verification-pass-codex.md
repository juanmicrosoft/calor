# Amendment 1.1.0, withdrawal of decision 1 — verification-only pass (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `6f030f6d7902c2e6541538df97c7b02ba7797a19..8b31fbcb` (the withdrawal commit `273747e8` and the round 1 record). Checks: (a) no operative trace of decision 1; (b) `publicationSeparation`, terminal condition 7, and R2 closure evidence equal the 1.0.1 text byte-for-byte; (c) decisions 2–6 and every capacity ceiling unchanged, no exception added; (d) the amendment log records the withdrawal (date, three reasons, R2-merges-last sequencing) and `afterDecisionBearingInspection` is false with an accurate inspection statement; (e) packet hashes, no duplicate keys in `contract.json`, manifest change limited to `Calor.Compiler.Tests` (12408 -> 12336).

## Codex output (verbatim)

No new BLOCKING, MAJOR, or MINOR defects found.

Checks (a)–(e) passed: decision 1 has no operative remnants; restored passages match frozen 1.0.1 byte-for-byte; decisions 2–6 and capacity remain unchanged; withdrawal and inspection records are consistent; hashes, duplicate-key checks, and manifest changes are correct. Exactly 72 test cases were removed.

Tests were inspected, not executed, in the read-only workspace.

VERDICT: CLEAN

## Disposition

Verdict CLEAN. No change made.
