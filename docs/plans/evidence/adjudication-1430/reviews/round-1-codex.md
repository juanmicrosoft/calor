# Codex review round 1 (A1 #1430)

Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` with `round-1-prompt.md`, on branch head `d981ea64c`. Final message verbatim:

1. **MAJOR — Final acceptance retains an unresolved closure condition.** `docs/plans/evidence/adjudication-1430/tools/make_a1_records.py.txt:343` leaves C7 as `"AT-SIGN-OFF"` even in `FINAL/ACCEPTED` output. Unlike C8, it never becomes a completed attestation. The decision authorizes closing the nine blockers; it does not record their closure. **Fix:** retain a closure snapshot or maintainer attestation, require it before final generation, and emit C7 as satisfied with that evidence.

2. **MAJOR — The registry omits a substantive documentation claim.** `docs/plans/evidence/adjudication-1430/claim-registry.json:320` summarizes notes lines 346–365 but drops the distinct statement at lines 363–364: default compilation does not run the obligation solver, whereas `calor_refine` does. No other registered claim covers that distinction. **Fix:** register it, or explicitly extend the existing claim and its evidence; refresh the approval and generated hashes.

3. **MAJOR — Ordering option (a) breaks the classifier argument.** `docs/plans/evidence/adjudication-1430/README.md:197` proposes merging a test modification before the terminal-record commit. That modification is not classifier-exempt; it enters the candidate-to-adjudication history when A1 merges. A separate PR does not exempt it, and classifying only an earlier branch head misses it. **Fix:** remove option (a). Under the recorded ruling, explicitly authorize the single known failing assertion, merge A1 first, then merge gate-state. A green prerequisite needs a revised ruling and classification strategy.

4. **MINOR — Several summaries overstate or miscount the notes.** `docs/plans/evidence/adjudication-1430/claim-registry.json:181` says “All 57 … except one”; the notes say all 57 hold and separately identify a conversion limitation. Line 126 drops **new** from “no new benchmark results.” Line 315 says all 49 website programs compile without errors, although W0 records five intended negatives. **Fix:** preserve those distinctions in the registry, generator and README.

5. **MINOR — The limitation misdescribes an approved reading.** `docs/plans/evidence/adjudication-1430/tools/make_a1_records.py.txt:96` says `R-PERMISSIVE-EFFECTS-NATIVE` does not cover the struct-element field-read case. The reading explicitly covers `F1-REFOUT-08`, whose candidate result invokes it. **Fix:** say the reading permits counting that measured case as native while the underlying default-effect-checking limitation remains.

6. **MINOR — Claimed verification records are absent.** `docs/plans/evidence/adjudication-1430/README.md:180` and line 186 cite `reviews/r2-gate.txt` and `reviews/classifier.txt`; neither exists on the branch. **Fix:** add the retained commands and outputs, including the tested commits and scratch assumptions.

The in-memory generated record passes the checked G004–G014/G016 paths and all six S007 validations with the proposed adjudicated map. Hashes reproduce; READY copies change only `approver` and added `a1`. Benchmark `approved: false` correctly triggers G015 only when benchmark publication is requested. The gate-state changes preserve the explicit negative controls. .NET tests were not run.

Apply corrections by rebuilding the add-only commits; subsequent modification commits would invalidate the classifier result.

VERDICT: 0 BLOCKING, 3 MAJOR, 3 MINOR

## Disposition

1. MAJOR, fixed. The final mode now requires `closures.json`, a `gh` snapshot written by `--snapshot-closures`, with all nine blockers CLOSED. In the final record C7 holds `true` and cites that snapshot.
2. MAJOR, fixed. `website-example-fixes` now states notes lines 363-364. The id is unchanged and the count stays 57. The README asks the maintainer to confirm the reworded claims.
3. MAJOR, fixed. Option (a) is removed. The README now says A1 merges with the single known failing assertion; that needs the maintainer's explicit OK, and an admin merge if the check is required. The gate-state PR follows.
4. MINOR, fixed. Reworded `by-ref-contract-cases-hold`, `no-new-benchmark-results` and `website-examples-checked-in-ci`.
5. MINOR, fixed. The `known-open-defects` limitation now says R-PERMISSIVE-EFFECTS-NATIVE lets F1-REFOUT-08 count as native while the limitation remains.
6. MINOR, fixed. Added `r2-gate.txt`, `classifier.txt` and `gate-state-tests.txt`, with the scratch scripts under `tools/`.

Every fix rebuilt the add-only commit; no later commit modifies a file.
