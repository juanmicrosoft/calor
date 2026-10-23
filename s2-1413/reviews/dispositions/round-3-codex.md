# S2 #1413 dispositions — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Round-2 verification:

| Finding | Result and evidence |
|---|---|
| 1 — Unrelated closure evidence | **Partially resolved.** Main reachability and witness presence at the commit are checked. PR association remains a commit-message substring check. `DispositionTests.cs:53`; `DispositionValidator.cs:299–307`. See finding 2 below. |
| 2 — Terminal binding | **Honestly recorded, unresolved.** Q6 explicitly acknowledges manual propagation; terminal validation still takes no S2 record. `dispositions.json:2757`; `EvidenceContractValidator.cs:675`. |
| 3 — Undispositioned residuals | **Recorded honestly.** Both discoveries now receive MILESTONE-FAILED. `dispositions.json:2693–2720`. Their preservation is not enforced; see finding 1. |
| 4 — Review-round overrun | **Honestly recorded, unresolved.** R-OBL acknowledges the overrun and Q8 requires amendment or BLOCKED/rescope. `dispositions.json:105`, `:2765`. Closure does not enforce that requirement. |
| 5 — Proof-case counts | **Resolved in the committed test path.** D018 recomputes counts, and its negative control checks two violations. `DispositionValidator.cs:190`; `DispositionTests.cs:433`. The validator’s optional input still permits bypass; see finding 5. |
| 6 — Missing cache reviews | **Resolved.** Four records now exist, including the approving verification record. `reviews/fix-cache-literal-width/verification-pass-codex.md:7`; existence check at `DispositionTests.cs:444`. |
| 7 — Stale mutation description | **Resolved.** The description includes proof-condition reads and limits the exception to counterexample exactness. `generate-dispositions.py:53`; `dispositions.json:102`. |

1. **BLOCKING — Known milestone-failing discoveries can disappear, and the positive control endorses that disappearance.**

   `requiredDiscoveries` still contains only #1493 (`dispositions.json:25`). D014 checks required coverage by **issue number**, leaving both discoveries tracked under #1413 optional (`DispositionValidator.cs:219–252`).

   The committed closure fixture explicitly deletes them, stating that this describes their resolution (`DispositionTests.cs:118–122`). `FullyMergedRecord_Closes` then expects successful closure (`:171`). Neither a repair nor an amendment resolves them.

   Deleting both entries therefore produces no discovery violation and removes the failure that D016 would propagate. This contradicts the discovery rule: every finding must receive an allowed resolution, rather than disappear (`v0.24-evidence-contract.md:368–370`).

   Require coverage and uniqueness by stable discovery **ID**, including both residuals. Preserve resolved discoveries with their repair or amendment evidence. Add deletion controls; repair the positive fixture.

2. **BLOCKING — Closure still accepts an unrelated main PR as every repair.**

   D010 accepts any positive PR number appearing anywhere in the commit message (`DispositionValidator.cs:278`, `:305–307`). It does not bind the declaration to an accepted repair or its reviewed evidence.

   A concrete replacement for the synthetic closure fixture is:

   - Set every repair’s PR to **1483**.
   - Set every merge commit to **`16c1c5f810b042683ef8d8b375f161884fa0b777`**.
   - Use existing `tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs` as every witness.
   - Supply nonempty branches and merged statuses.

   I verified that this unrelated **0.25 scope PR** is on `origin/main`, contains that file, and satisfies the message check. **By inspection, the remaining validator rules accept these declarations without any numeric repair.** The message also contains issue `#1426`, which the same regex accepts as a PR identity.

   Main reachability is an improvement, but it does not establish repair provenance. Bind closure to a durable, reviewed acceptance packet tying the actual PR and commit to affected findings, regression results, measured size, and prerequisites. Add a control using unrelated **existing** main evidence.

3. **MAJOR — R-OBL’s recorded review overrun does not prevent acceptance at closure.**

   The record correctly says acceptance needs Q8 (`dispositions.json:105`). However, the repair and closure checks never consume `reviewRoundOverrun` or require its amendment (`DispositionValidator.cs:255–309`).

   Consequently, the successful merged fixture retains the overrun and passes under the unchanged contract. Even preserving both discoveries and closing as MILESTONE-FAILED would not trigger an overrun violation.

   The frozen rule requires stopping and amendment or BLOCKED, and terminal success forbids an unamended ceiling overrun (`v0.24-evidence-contract.md:398–399`, `:425`). Make acceptance eligibility enforceable; free-text acknowledgement cannot discharge Q8.

4. **MAJOR — The new runtime skip fails the existing CI quality gate.**

   Read-only execution of:

   `python3 -B scripts/check_test_quality.py`

   returned **exit 1**:

   > ERROR: unapproved runtime skip site: HEAD is already on origin/main (1)

   The skip is introduced at `DispositionTests.cs:428`. CI invokes this checker at `.github/workflows/test.yml:82–84`.

   After merge, the test also skips when HEAD is on main, while the compiler manifest still expects three skips (`eng/test-manifest.json:9`); exact executed/passed counts are enforced by `scripts/check_trx.py:115–123`.

   Replace this checkout-dependent control with a deterministic, non-skipping test. The six added test cases do not establish CI readiness.

5. **MINOR — D018 remains optional and treats missing source rows as zero proofs.**

   `validatedProofCounts` defaults to null (`DispositionValidator.cs:45`), which disables D018 completely (`:191`). With a dictionary supplied, `GetValueOrDefault` also accepts an absent row as a zero count (`:193`).

   The current test wrapper supplies the data, so committed counts are protected there. The validator itself still contradicts its fail-closed claim. Require the input and distinguish an evidenced zero from a missing row.

6. **NIT — The report retains stale validator and residual summaries.**

   The report says D001–D017 and “checked-out history” despite adding D018 and requiring main (`v0.24-s2-dispositions.md:12–15`). It also says “One residual” for R-OBL (`:53`), while the later inventory records two. Update these descriptions.

Quantitative checks passed: **34 findings and 100 rows per baseline; 13 FIX / 21 DEMOTE; seven false proofs split 2 FIX / 5 DEMOTE; 44 clean rows with zero validated proofs**. Finding metadata, row metadata, all proof counts, and three source hashes match. Run-1/run-2 finding packets are identical. Generator output captured in memory is byte-identical. The draft has **77 additions / 4 deletions**; the test file contains **45 cases**, including one SkippableFact.

By inspection, the OPEN record validates; attempted closure produces **seven D010 violations plus D016**. Negative controls assert exact code sets, but `Distinct()` generally still masks subjects and multiplicity (`DispositionTests.cs:91–94`).

No dotnet execution or repair-branch code inspection was performed.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING milestone-failing discoveries can disappear | Fixed. The tests pin the discovery ids `D-1493`, `D-OBL-PROOF-GETTER`, and `D-OBL-THROWING-PREDECESSOR` outside the record and pass them to the validator. D014 requires each one exactly once. Negative control: deleting `D-OBL-PROOF-GETTER` gives D014. The positive SUCCESS fixture no longer deletes the discoveries; it resolves them through a repair that lists them, as an accepted demotion would. A new control shows that the committed discoveries, left unresolved, close only as `MILESTONE-FAILED`. |
| 2 BLOCKING an unrelated main PR satisfies closure | Fixed. The first line of the merge commit must be exactly `Merge pull request #<pr> from <owner>/<branch>`, and the repair's branch must be an S2 branch (`milestone-0.24/s2-1413-*`, D008). The reviewer's witness, #1483 from `milestone-0.25/r0-1426-scope-baseline`, is now a negative control, and so is a non-S2 branch. Whether the merged contents match the review record stays the maintainer's merge review, as the report says. |
| 3 MAJOR overrun does not block acceptance | Fixed. At closure, a repair with `reviewRoundOverrun` needs a non-empty `overrunAmendment`, or D010 fires ("review-round overrun without a recorded amendment"). There is a negative control. |
| 4 MAJOR runtime skip fails the quality gate | Fixed. The checkout-dependent `SkippableFact` was replaced by a deterministic test: the real git callbacks fail closed on an unknown commit. `python3 -B scripts/check_test_quality.py` passes. |
| 5 MINOR D018 optional; missing rows count as zero | Fixed. The validated-proof counts and the pinned discovery ids are required parameters. A row missing from the case results is a D018 violation, not a zero. |
| 6 NIT stale report text | Fixed: the text now says D001–D018, closure on main with the merge of the PR from its S2 branch, and two R-OBL residuals recorded as discoveries. |
| 2 (round 2) terminal binding | Still recorded as Q6, because it is an R0 validator change outside S2. |
| Tests | DispositionTests: 49 cases, all passing. Manifest net +4 via the script. |
