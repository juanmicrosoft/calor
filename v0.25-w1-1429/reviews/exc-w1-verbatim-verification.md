# EXC-W1-VERBATIM Codex verification-only pass (the single pass the exception allows)

- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`
- **Task:** verify W-V1b against its fix in `0ff3ff9ad`, and check the changed lines for new BLOCKING or MAJOR issues.
- **Verdict:** REQUEST-CHANGES. W-V1b is NOT-VERIFIED.
- **Independence:** reduced (R0 §9).

## What held

Codex reproduced the matcher in memory and confirmed:

- The current pages pass, with all four required markers (F1, F2, F3, F5).
- The marked C# blocks equal the complete fixtures verbatim.
- All four negative controls change the real guide text, and each one fails as it should.
- The check rejects indentation changes, a line replaced by a blank, non-contiguous text without an elision, and empty elision parts.
- The prose that replaced the inline comments matches converter behavior and the committed transcripts.

## Still open (MAJOR, W-V1c)

The elision splitter is too permissive. It uses `^[ \t]*// \.\.\.[ \t]*$`, which also matches an *indented* `// ...`.

- **Bypass:** replace the guide's `        x = x + 1;` with `        // ...`. The test reports zero problems, even though `Bump` now does nothing.
- **Same for padding:** a variant padded with tabs and spaces also passes.
- **Suggested fix:** match only the exact line `^// \.\.\.$`, or remove elision support, since the guide uses none. Add an indented-elision negative control.

No other new BLOCKING or MAJOR issue was found.

## Status

Not fixed. EXC-W1-VERBATIM allowed one fix and exactly one verification pass, and both are now used. The coordinator's instruction is to stop and report once.

The published pages are unaffected: their C# blocks equal the fixtures verbatim and use no elision. The gap is only in what the test would let a future edit get past.
