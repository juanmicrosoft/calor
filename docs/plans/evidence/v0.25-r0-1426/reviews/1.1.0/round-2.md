# Codex adversarial review, round 2 (#1426 amendment 1.1.0, PR #1553)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family adversarial review with reduced independence. It is not a human review.
- **Input:** `git diff 7d542b828...c1fe9c965` (this PR against #1552's head), the round-1 record,
  and read-only access to the tree.
- **Verdict:** REQUEST-CHANGES (round-1 findings 1, 3, 4, 5 FIXED; finding 2 NOT FIXED; 2 new MAJOR).
- **Count:** round 2 of at most 3 rounds plus 1 verification-only pass for this PR.

## Findings (verbatim)

1. **BLOCKING — Round-1 finding 1: FIXED.** `docs/plans/evidence/v0.25-r0-1426/gate/blockers-mapped-after-1413.json:23`: F2 now appears under both obligation repairs. Its blockers, amendment mapping, scope, and §6.1 agree with the S2 repair identities.

2. **MAJOR — Round-1 finding 2: NOT FIXED (validator constraint).** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:297`: The record now honestly identifies #1520/#1538 as operative, unconfirmed session-directed inferences. However, S010 accepts changing `allowedExtraPasses` from 1 to 99, retargeting the PR to 9999, or substituting unrelated `D-CAPACITY` as the basis. Neither the amendment's exception list nor the gate's `pendingConfirmation` list constrains these fields. **Fix:** retain the operative exceptions, but bind each allowance to its registered PR, ceiling, one verification-only pass, scope, basis, and confirmation classification. Add isolated controls for widening, retargeting, and substituting the basis.

3. **MAJOR — Round-1 finding 3: FIXED.** `docs/plans/evidence/v0.25-r0-1426/scope.json:1535`: X-INT-07 requires `native` and explicitly excludes whole-member preservation. Its amendment copy and requirement digest agree.

4. **MAJOR — Round-1 finding 4: FIXED.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:93`: Unknown mapped families now produce S012. The new F7 mutation isolates that check.

5. **MINOR — Round-1 finding 5: FIXED.** `docs/plans/evidence/v0.25-r0-1426/scope.json:1768`: DOC-F6-02 distinguishes default parse-failure rescue from compiler-rejection rescue requiring `passthroughOnError`. Both witnesses match the sealed results; the duplicate and documentation table agree.

6. **MAJOR — New: removing the mapping makes S012 pass vacuously.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:85`: Deleting `mappedBlockers`, or replacing `applicable` with an empty array, executes zero checks. S009 still accepts the retained gate record's `SATISFIED` assertion, and neither the amendment mapping nor S2 repair inventory is reconciled. Thus MET can survive removal of its substantive mapping evidence. **Fix:** require the mapping for MET, reconcile it with the gate and amendment records, and require every S2 repair to have an applicable or justified not-applicable disposition. Add deletion, empty-list, and dropped-repair controls.

7. **MAJOR — New: the promised regression-run prerequisite does not constrain READY.** `docs/plans/evidence/v0.25-r0-1426/gate/blockers-mapped-after-1413.json:6`: The rule requires each affected family's READY record to cite the repair merge and a passing regression-witness run on its candidate. `InteropScopeValidator.cs:390` accepts any nonblank blocker reference; its generic test summary requires neither the witness nor candidate identity. An otherwise valid F2 READY record can resolve #1496/#1503 with `"reference": "resolved"` and no regression-run evidence. **Fix:** require retained, structured evidence for each applicable repair, matching its merge, registered witnesses, READY candidate, and passing result. Add controls for missing, wrong-candidate, and failed runs.

**REQUEST-CHANGES**

## Dispositions

2. **Fixed.** Amendment 1.1.0's `capacityExceptions` now holds the full registered copy of each
   exception, and S010 requires each exception to equal its copy. Widening (`allowedExtraPasses`
   99) or retargeting (`pr` 9999) after registration fails. An unconfirmed exception must be on
   `review-rounds` with `allowedExtraPasses` 1. Its `basis` must be a recorded maintainer decision
   whose `exceptions` list grants a confirmed exception with the same ceiling and pass count.
   `D-F5-EXCEPTIONS` now lists its two exceptions; `D-CAPACITY` grants none. New controls cover
   widening, retargeting, an unrelated basis, and an exception wider than its basis, each
   re-registered where needed to isolate the check.
6. **Fixed.** Once the mapping condition is claimed (SATISFIED, or the gate MET), S012 does the
   following. It reads the cited S2 record and requires every repair there to have exactly one
   disposition. An applicable entry must carry that repair's PR, merge commit and regression
   witnesses; a not-applicable entry needs a reason. A mapped repair absent from the record fails.
   The amendment's mapping must equal the packet's applicable mapping, and so must the gate record
   when MET. New controls: mapping removed, mapping emptied, an S2 repair dropped, another merge
   commit, and an amendment mapping that differs.
7. **Fixed.** For each mapped repair affecting the family, S007 requires the READY record's
   `blockersResolved` entry to give the repair's `mergeCommit` and a `witnessRun` record under
   `docs/plans/evidence/`. That record must name the READY candidate and the repair, list exactly
   the repair's regression witnesses, and say `passed`. The test helper can now build READY records
   for any family, with a new positive for F2. New controls: missing run, run from another
   candidate, failed run, and another merge commit. §6.1, §10 and §11 describe the rule.

Local: `dotnet test tests/Calor.Compiler.Tests --filter InteropScope`, 98 passed.
