# S2 #1413 disposition record — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

1. **BLOCKING — Closure accepts invented repair evidence.**  
   `DispositionValidator.cs:83–88` checks a positive PR number, a nonempty witness array, `status = merged`, and a syntactically valid SHA. It does not establish that the PR merged, that the commit belongs to it, or that its witness exists and passed.

   The committed positive control demonstrates the bypass: [FullyMergedRecord_Closes](tests/Calor.Compiler.Tests/SoundnessDisposition/DispositionTests.cs:94) supplies PR `9999`, commit `aaaaaaaa…`, and nonexistent `tests/example.cs`, then expects successful closure. R-NUM can therefore become “merged” without any numeric repair.

   Closure needs verified, durable repair evidence tying the accepted PR, merge identity, affected cases, regression results, actual size, and dependencies together. The present validator enforces declarations, not §10 closure evidence.

2. **MAJOR — Two false-proof demotions are mislabeled FIX; SELFREF lacks case-specific resolutions.**  
   F-B1-014/-015 and F-N1-014/-015 explicitly change `Discharged` to **`Unsupported`, guard kept**, but receive `FIX-IN-0.24`. That is a visible demotion of the affected obligation, even though fixing stale facts is the implementation mechanism. The adjacent mutation findings use DEMOTE for the same resulting outcome. See [the disposition plan](docs/plans/evidence/s2-1413/generate-dispositions.py:108).

   Correcting these two entries alone changes each baseline’s finding totals from **17 FIX / 17 DEMOTE to 15 / 19**, and the false-proof summary from **3 fixed / 4 demoted to 1 / 6**. Row totals remain unchanged because OBL-MUTATION-KILL already takes DEMOTE.

   F-*-022 through -026 also all receive FIX with one shared reason offering three different outcomes: `Discharged`, `Assumed`, or `Unsupported`. Record the actual resolution per case and classify accordingly. In particular, SELFREF-005 has zero reached oracle inputs and `vacuous-in-domain`; an Unsupported resolution cannot be described as proving the claim.

   The row aggregation rule itself is sound: **MILESTONE-FAILED > DEMOTE > FIX**. Its inputs need honest finding-level classifications. R-CACHE is a genuine cache fix; the described R-IMPL and R-QNT outcomes are visible demotions. R-TEXT can honestly fix the encoding defect while retaining the string model’s existing legitimate Assumed limitation.

3. **MAJOR — `establishingOutcomeObserved` falsely reports establishment on 33 clean rows per baseline.**  
   [Generator line 149](docs/plans/evidence/s2-1413/generate-dispositions.py:149) sets this flag false only for *modeled* rows without `validated-proof`. Every assumed or refused clean row consequently receives `true`.

   For example, NUM-MIXED-U64-SIGNED has five `refusal-validated` cases and no `validated-proof`; GEN-REFUSAL has two `no-claim` and two `refusal-validated` cases. Both say an establishing outcome was observed.

   Across the 79 clean rows, **35 have a validated-proof case and 44 do not**. Only 11 of those 44 receive false. Compute the flag from observed establishing outcomes, or omit it where establishment is inapplicable. The document’s narrower count—10 release-critical modeled rows plus CTRL-NEGATIVE—is correct, but does not justify the other true flags.

4. **MAJOR — Discovery and reference completeness do not fail closed.**  
   In [DispositionValidator.cs](tests/Calor.Compiler.Tests/SoundnessDisposition/DispositionValidator.cs:214), D014 validates only discovery entries that remain present. Removing `discoveryFindings`, or replacing it with `[]`, produces no discovery violation. D-1493 can disappear from an otherwise closable record.

   Other unchecked relationships include:

   - Row `findings` and `repairs` arrays: deleting or falsifying them is accepted.
   - Repairs listed but referenced by no finding or discovery.
   - Finding `token` versus the S1 source.
   - Row criticality and establishing-outcome flags.
   - The stated contract version and registration identity.

   Require discovery coverage and uniqueness, validate both directions of the references, and check the provenance and observation fields the record presents as evidence. D-1493’s current entry appropriately distinguishes an unregistered, unobserved potential false proof from the S1 findings; the validator must preserve that distinction and its presence.

5. **MAJOR — Failure closure has no enforced propagation to milestone failure.**  
   A concrete accepted mutation is: assign every finding `MILESTONE-FAILED`, update finding rows likewise, remove every repair, assign the discovery `MILESTONE-FAILED`, and set closure CLOSED. The validator returns no violations. Even `capacity.used = 6` may remain, because it is unchecked.

   Closing a failed gate can be legitimate. What is missing is an explicit failure result that prevents this record from satisfying a successful candidate-freeze or terminal milestone path. The terminal validator also does not consume S2 dispositions directly.

   Distinguish **closed successfully** from **closed with milestone failure**, and enforce contract terminal predicates 4–5 against the actual disposition record. An allowed failure disposition must never become a successful closure merely because validation returned zero violations.

6. **MINOR — R-NUM is honestly pending, but its options and capacity wording need precision.**  
   S1 confirms five `required-demotion-absent` findings per baseline with oracle-holding Proven results. None is a false unconditional proof. Keeping this decision pending in an explicitly OPEN record is not forbidden follow-up laundering; it cannot discharge S2.

   The amendment option must name the frozen **registration classifications/table** as well as the governing contract, preserve the original S1 findings and last statuses, and record the decision-bearing inspection. A contract amendment alone does not change `registration.json`, and the current handoff still forbids VALIDATED for an existing finding.

   “No capacity remains” should distinguish **five opened repair PRs plus one reserved slot** from six accepted PRs. R-NUM has no PR and no acceptance decision.

   The test-breakdown claim also needs correction or retained evidence: there are **six**, not seven, methods matching `*_Bounded_MustBeProven` in OverflowSoundnessBenchmark. `RunFullBenchmark` could supply a seventh affected test, but it must be named. The listed groups sum to 18; the differential-report test could explain a total of 19. The absent draft prevents verification of the claimed 77 lines or actual failure count.

7. **MINOR — Keeping GEN-REFUSAL VALIDATED is mechanically permitted; “O2 has nothing to corroborate” is false.**  
   Registration requires O2 on every witness and the first 16 reached inputs, except implication and precondition claims. Unsupported outcomes have no exemption. GEN-REFUSAL-002/-004 each have three reached inputs and retained guards in both emissions; the object input never reaches the compiled method.

   The registered row-status calculation does not use O2 completeness, so retaining the limited CLEAN-WITHIN-BUDGET/VALIDATED status does not itself contradict that calculation. The partial replay also establishes no semantic divergence.

   Remove the claim that O2 has no purpose here. Preserve the missing coverage and resolve condition 10 explicitly; treating corroboration as non-adjudicating does not silently waive its registered execution requirements.

8. **MINOR — The report overstates completed repairs and blurs the published-artifact warning.**  
   [The report’s “All 7 … are repaired” claim](docs/plans/v0.24-s2-dispositions.md:42) describes open PRs. Say that proposed repairs cover all seven findings; none has merged according to the record. Unsupported outcomes withdraw a claim—they do not affirm the violated proposition against the oracle.

   N1’s candidate-side treatment is otherwise honest and explicitly authorized by `dispositionHandoff`: 0.24 changes cannot repair the immutable 0.21.0 binary. The release-notes obligation remains a proposal, and D013 checks only that its text is nonempty.

   The eventual warning should distinguish the mechanisms. S1’s implication findings explicitly say **“no emitted guard”**: they incorrectly accept implementer contracts. Guard elision was observed for mutation obligations and cache proofs. #1493 remains a potential Windows collision, not an S1-observed false proof. Preserve those qualifications.

The quantitative checks otherwise passed: **34 findings and 100 rows per baseline**, identical run-1/run-2 finding packets, matching IDs/classes/tokens/statuses, three correct source hashes, and byte-identical generator output when its write was captured in memory. No missing or duplicate registered finding, VALIDATED finding row, or NOT-INVESTIGATED misuse was found. The manifest’s **30 tests = 5 positive + 25 negative** is correct.

Mentally, the committed OPEN record returns zero violations; closure returns seven D010 violations. Missing findings, unknown dispositions, unmerged repairs, seven repair entries, and a declared 601-line repair trigger the intended codes. Simply deleting R-NUM while retaining its references triggers D007. The negative controls assert exact code sets and reject unexpected *different* codes, although `Distinct()` does not verify violation subjects or multiplicity. Their coverage does not address the bypasses above.

No dotnet execution or repair-branch code inspection was performed.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING closure accepts invented repair evidence | Fixed: at closure every merged repair's merge commit must be on the checked-out history (`git merge-base --is-ancestor <sha> HEAD`) and every regression witness must exist. Both are checked through injected `ClosureEvidence`. The positive closure control uses an accept-all evidence stub. `D010_InventedMergeEvidence_DoesNotClose` runs the real repository evidence against invented values and fails closure. |
| 2 MAJOR false-proof demotions labeled FIX; SELFREF not per case | Fixed: F-*-014/015 are DEMOTE (the outcome becomes Unsupported). SELFREF is per case: 023 and 025 FIX (Discharged); 022 and 024 DEMOTE (Assumed); 026 DEMOTE (Unsupported, vacuous-in-domain; the reason says the claim is withdrawn, not proved). After R-IMPL's round-1 rework, IMPL-DIVISION-TOTALIZED-001 (030) is a FIX: it is refuted with the oracle's witness. Totals: findings 13 FIX / 21 DEMOTE per baseline; false proofs 2 fixed / 5 demoted. Each repair's `kind` must equal its most conservative finding disposition (validator D008). |
| 3 MAJOR `establishingOutcomeObserved` misleading | Replaced by `validatedProofCases` (a count) on every clean row: 44 of 79 clean rows have 0 per baseline. The reason text states when no claim is supported. |
| 4 MAJOR discovery and reference completeness | Fixed: `requiredDiscoveries` (#1493) must each be dispositioned exactly once (D014, `D014_RequiredDiscoveryRemoved`). Row `findings`/`repairs` lists must equal the actual references, and every repair must be referenced (D015). Finding `token` and row `releaseCritical` must match S1 (D002/D003). `capacity.used` must equal the repair count (D009). `contractVersion` and `registrationCommit` (from the S1 pins) must match (D017). |
| 5 MAJOR failure closure | Fixed: a closed record states `result` SUCCESS or MILESTONE-FAILED (D016). Any MILESTONE-FAILED disposition forces MILESTONE-FAILED, and SUCCESS forbids it. `FailedRecord_ClosesOnlyAsMilestoneFailed` covers both. Only SUCCESS satisfies terminal predicates 4–5 (stated in the record). |
| 6 MINOR R-NUM precision | Fixed. The amendment option names the registration classifications/table and the decision-bearing inspection, and keeps the findings on record. Capacity reads "five opened + one reserved slot". The 19 failing tests are listed by name; there are 6 `*_Bounded_MustBeProven` tests, plus `RunFullBenchmark` and the differential-report test. The draft is committed as `r-num-draft.patch` (77 added, 4 removed). |
| 7 MINOR GEN-REFUSAL wording | Fixed. The record and the doc say that O2 coverage for -002/-004 is incomplete, that row status does not use O2, and that condition 10 stays the maintainer's decision. |
| 8 MINOR overstated repairs; N1 mechanisms | Fixed. "Proposed repairs (open PRs, none merged) cover all seven". A demotion "withdraws a claim". The N1 advisory distinguishes guard elision (OBL/CACHE), wrong acceptance with no guard (IMPL), and the potential #1493 collision. |
