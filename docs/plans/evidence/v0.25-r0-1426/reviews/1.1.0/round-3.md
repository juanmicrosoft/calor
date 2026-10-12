# Codex adversarial review, round 3 (#1426 amendment 1.1.0, PR #1553)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family adversarial review with reduced independence. It is not a human review.
- **Input:** `git diff 7d542b828...4214faa77`, the round-1 and round-2 records, and read-only
  access to the tree.
- **Verdict:** REQUEST-CHANGES (round-2 findings 2 and 7 FIXED; finding 6 NOT FIXED; 1 new MINOR).
- **Count:** round 3 of at most 3 rounds for this PR. This is the last adversarial round; one
  verification-only pass remains.

## Findings (verbatim)

1. **MAJOR — Round-2 finding 2: FIXED.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:349`: S010 compares each exception with its full amendment registration, binding its PR, scope, allowance, basis and confirmation classification. Unconfirmed allowances must grant exactly one extra pass and extend a decision granting a matching confirmed exception. The widening, retargeting and unrelated-basis controls exercise these checks. No further fix required; #1520/#1538 remain operative, explicitly unconfirmed inferences.

2. **MAJOR — Round-2 finding 6: NOT FIXED.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:112`: The inventory check trusts whichever JSON file `mappedBlockers.source` names. Pointing it at a retained copy containing only the three applicable repairs, and clearing `notApplicable`, passes the reconciliation predicates. R-CACHE, R-IMPL, R-QNT and R-NUM then have no disposition against the actual S2 inventory, while MET survives. The existing dropped-repair control retains the canonical source and misses this bypass. **Fix:** require the canonical `docs/plans/evidence/s2-1413/dispositions.json` source and reconcile every repair from that record. Add a control substituting a cropped source.

3. **MAJOR — Round-2 finding 7: FIXED.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:461`: Each applicable READY blocker now requires its registered merge commit and retained evidence matching the READY candidate, repair, exact witness list and passing result. The F2 positive exercises mapped blockers; missing-run, wrong-candidate, failed-run and wrong-merge controls target the required fields. No further fix required.

4. **MINOR — Gate reconciliation passes when its evidence field is deleted.** `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:140`: Removing `applicable` from the gate record skips comparison because of `is { } ga`; S009 still accepts its SATISFIED header. `InteropScopeTests.cs:379` already exploits this accidentally: `GateReader` supplies no mapping, so "fully evidenced MET" passes without exercising gate reconciliation. **Fix:** reject a missing gate mapping when MET, require equality with the packet's dispositions, and make `GateReader` preserve the substantive gate evidence. Add an isolated gate-field deletion control.

**REQUEST-CHANGES**

## Dispositions

2. **Fixed.** `InteropScopeValidator.S2Record` pins `docs/plans/evidence/s2-1413/dispositions.json`.
   The mapping must cite that path, and reconciliation reads only that file. New control: a cropped
   copy with only the three applicable repairs and an empty `notApplicable` fails S012.
4. **Fixed.** When MET, the gate record must carry `applicable` and `notApplicable` equal to the
   packet's; a missing field fails. `GateReader` now starts from the committed gate record and
   overrides only `condition`, `state`, `issueState` and `closedAtUtc`, so the "fully evidenced MET"
   positive exercises reconciliation. New control: gate record without its mapping.

Local: `dotnet test tests/Calor.Compiler.Tests --filter InteropScope`, 100 passed.
