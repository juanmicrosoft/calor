# Codex adversarial review, round 1 (#1426 amendment 1.1.0, PR #1553)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family adversarial review with reduced independence. It is not a human review.
- **Input:** `git diff 98e41e76d...b118c6d76` (this PR's own change, without the stacked #1552
  write-back), plus read-only access to the tree.
- **Verdict:** REQUEST-CHANGES (1 BLOCKING, 3 MAJOR, 1 MINOR).
- **Count:** round 1 of at most 3 rounds plus 1 verification-only pass for this PR.

## Findings (verbatim)

1. **BLOCKING — gate/blockers-mapped-after-1413.json:23: F2 is missing applicable obligation repairs.** F2's parser lifts opaque interpolation calls into native `CallExpressionNode`s. That removes the raw-C# opacity safeguard and invokes R-OBL's heap-mutation rules and R-OBL-RESIDUALS' throwing-predecessor rules. The same rationale used to map F3 applies to F2. Recording this condition SATISFIED is therefore premature. **Fix:** add F2 to both repairs and add #1496/#1503 to F2's blockers; reconcile the gate record, scope, amendment, and documentation.

2. **MAJOR — scope.json:417: Unconfirmed inferences are recorded as approved allowances.** Both #1520 and #1538 receive `allowedExtraPasses: 1`, appear in the accepted capacity gate's exception list, and say "maintainer-approved," despite `confirmed: false`. The supplied decision authorizes F5 only. S010 ignores confirmation, so the pending labels do not prevent treating these as valid exceptions. **Fix:** retain them as non-operative proposals outside the accepted exception lists until explicitly confirmed, and reject unconfirmed active allowances in the validator.

3. **MAJOR — scope.json:1529: X-INT-07 permits preservation of an in-scope native interaction.** Its shape is a returned rank-2 array and an array argument inside a native local function—the native work registered for F3 and F5. `native-or-preserved` lets whole-member preservation satisfy this interaction without exercising generated names or native element evaluation. **Fix:** require `native` for the supported intersection; register any explicitly unsupported boundary separately. Update the amendment copy and requirement digest.

4. **MAJOR — InteropScopeValidator.cs:90: Unknown mapped families bypass S012.** Replacing a repair's `families` with `["F7"]` satisfies the nonempty check and skips every blocker lookup. S001 does not catch this: it examines the packet's family declarations, which remain unchanged. The mapping check therefore passes vacuously. **Fix:** reject every unregistered mapped family with S012 and add an isolated negative control for this mutation.

5. **MINOR — scope.json:1748: DOC-F6-02 overgeneralizes the MCP observation.** F4-ITER-01 supports requiring `passthroughOnError` for that round-trip compilation failure. It does not establish that MCP rescues members *only* with that option: sealed F5-ARRAY-02 shows successful default-mode rescue with `post-validation-fallback`. **Fix:** distinguish parse-failure rescue from round-trip compilation rescue, cite both witnesses, and update the duplicated row and documentation table.

**REQUEST-CHANGES**

## Dispositions

1. **Fixed.** R-OBL (#1496) and R-OBL-RESIDUALS (#1503) now list F2. F2's blockers are #1426,
   #1423, #1497, #1496, #1503. The gate record, `mappedBlockers`, the amendment summary and §6.1
   agree. Both repairs are merged, so the condition still needs no unmerged code.
2. **Partly fixed.** The coordinating session directed that the #1520 and #1538 exceptions be
   recorded so their passes can run, and that the report flag them as an inference for the
   maintainer to confirm. They stay in `capacity.exceptions`. The text no longer says
   "maintainer-approved": each says it extends the F5 decision at the session's direction, is
   `confirmed: false`, and names its `basis` (`D-F5-EXCEPTIONS`). The `capacity-accepted` gate record
   now lists only the two confirmed F5 exceptions, plus a separate `pendingConfirmation` list. S010
   now requires a boolean `confirmed`, and an unconfirmed exception must name a recorded maintainer
   decision as its basis (new control). Keeping them operative is a maintainer item, not an agent
   decision.
3. **Fixed.** X-INT-07 expects `native`. Its note says whole-member preservation does not satisfy
   it, and that a shape outside either family's native slice needs its own row. The amendment copy
   and requirement digest are updated.
4. **Fixed.** S012 rejects a mapped family outside the six; new control "mapped blocker naming an
   unknown family".
5. **Fixed.** DOC-F6-02 now separates the two triggers. MCP default rescues F5-ARRAY-02, whose
   output fails to parse (`post-validation-fallback`). It returns isError on F4-ITER-01, whose
   output parses and is rejected with Calor0209, unless `passthroughOnError` is set. The row cites
   both witnesses (4 observations), and the §3.1 table matches.
