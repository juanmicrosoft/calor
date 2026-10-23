# PR 2 extra verification pass, Codex (#1132 F5)

- **Exception:** `EXC-F5-PR2-FIX-VERIFY` (R0 scope amendment 1.2.0, `scope.json` `capacity.exceptions`),
  recorded in commit `7b96762c4` before the fix and this pass. @juanmicrosoft, 2026-10-13: "Exception:
  fix + 1 pass". It allows exactly one fix and exactly one verification-only pass.
- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
  This was the one allowed pass; no other Codex invocation was made under the exception.
- **Scope:** the MAJOR left open by `pr2-verification.md` and its fix, commit `3be4ecee8`
  (`GetConstantValue(e).HasValue && !GetConversion(e).IsUserDefined` in `HoistComplexArguments.InPlace`),
  witness `L.UseY(L.C, new int[] { L.S() }, new Cell())` in
  `RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal`.
- **Verdict:** **APPROVE.** The finding is VERIFIED and revert-sensitive; no new BLOCKING or MAJOR.

## Review output (verbatim)

**VERIFIED** by read-only code tracing at `3be4ecee8`; no tests or Calor commands executed.

The exact retained witness is `L.UseY(L.C, new int[] { L.S() }, new Cell())` in `RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal`. Its `implicit operator Y(int)` logs `y`. The new guard excludes this user-defined conversion from the constant exemption; `KeepsMeaningInTemp` then rejects it, preserving the statement and clearing queued hoists. This fixes the recorded `L.Use(L.C, new int[] { L.S() }, new Cell())` ordering defect (`cs` versus `sc`).

**The retained witness would fail if the fix were reverted:** conditional-expression-hoisting losses would fall from 3 to 2, and converted output would become `3,4,1,2|cssy` instead of `3,4,1,2|csys`.

No new **BLOCKING or MAJOR** regression was identified in the touched code. `L.UseB(L.C, new int[] { 1 }, new Cell())` still stays native: its standard constant conversion passes the exemption, retaining the `byte?` overload and result `1` rather than `long?` and `2`.

VERDICT: APPROVE
