# EXC-W1-EXCERPT Codex verification-only pass (the single pass the exception allows)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. The prompt asked Codex to verify W-V1 against its fix in `d492f149a` and to look for new BLOCKING or MAJOR issues in the changed lines.

**Verdict:** REQUEST-CHANGES. W-V1 is NOT-VERIFIED. Independence is reduced (R0 §9).

## What held

These results come from Codex's in-memory reproduction of the matcher:

- The current pages pass, with all four required markers present (F1, F2, F3, F5).
- Deleting `x = x + 1;` from `Bump` is caught.
- Deleting the shown `sb.Append(...)` is caught.
- The statement additions and changes Codex tried are caught.
- The new deletion control works on the real guide text. It would fail under the old ordered-subsequence rule, so it really tests the fix.

## Still open (MAJOR, W-V1b)

Comment stripping runs before blank-line detection. That opens a loophole:

- **The bypass:** commenting out a shown statement (`        // x = x + 1;`) normalizes it to an empty line.
- **Why it passes:** the empty line ends the chunk there, so the edit goes uncaught even though `Bump` now does nothing.
- **Same effect:** replacing the handler line with `// handler call omitted` also passes.

**Suggested fix:** take chunk boundaries from real blank lines before stripping comments, and add a comment-out negative control.

No other new BLOCKING or MAJOR issue was found in the changed lines.

## Status

Not fixed. EXC-W1-EXCERPT allowed one fix and exactly one verification pass, and both are used. The coordinator's instruction is to stop and report once. The current pages are line-for-line excerpts of the fixtures, so no published page misstates its input. The gap is only that the test would not catch a future edit that comments a line out.
