# S2 #1413 dispositions — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Round-1 verification:

| Finding | Result and evidence |
|---|---|
| 1 — Invented closure evidence | **Partially resolved.** Absent commits and files are rejected, but unrelated existing commits and files still satisfy closure. `DispositionTests.cs:41`; `DispositionValidator.cs:287`. See finding 1 below. |
| 2 — FIX/DEMOTE classifications | **Resolved.** 014/015 are DEMOTE; SELFREF has case-specific outcomes; 030 is now FIX with an oracle-supported refutation. `generate-dispositions.py:123`, `:124`, `:130–134`. Totals are **13 FIX / 21 DEMOTE**, including **2 fixed / 5 demoted false proofs**, per baseline. |
| 3 — Misleading establishment flag | **Resolved in the committed data.** `validatedProofCases` replaces the flag and matches the case results on every clean row. `generate-dispositions.py:168–173`. Its enforcement remains incomplete; see finding 5. |
| 4 — Discovery/reference completeness | **Partially resolved.** Required discovery coverage, row references, unused repairs, tokens, criticality, capacity usage, and identities are checked. `DispositionValidator.cs:122`, `:172`, `:196`, `:208`, `:247`. Observation counts remain unchecked. |
| 5 — Failure propagation | **Partially resolved.** D016 correctly forces the local failure result. `DispositionValidator.cs:303–309`. Terminal success still does not consume that result; see finding 2. |
| 6 — R-NUM precision | **Resolved.** Options name the registration amendment and preserve findings; capacity distinguishes five opened PRs from one reservation. `generate-dispositions.py:91–95`, `:225–226`. The draft contains **77 additions / 4 deletions**. The named test breakdown totals 19; execution remains unverified. |
| 7 — O2 wording | **Resolved.** Missing registered coverage and the pending condition-10 decision are explicit. `v0.24-s2-dispositions.md:142–153`. Keeping the limited VALIDATED row status is mechanically consistent with the registration. |
| 8 — Completed-repair and N1 claims | **Resolved.** Repairs are explicitly proposed and unmerged; published artifacts remain affected; guard removal, incorrect interface acceptance, and potential #1493 collisions are distinguished. `v0.24-s2-dispositions.md:72–78`, `:118–130`. |

Remaining and new findings:

1. **BLOCKING — Closure still accepts invented repairs using unrelated existing evidence.**

   `DispositionTests.cs:41` checks ancestry against **HEAD**, and `:51` checks only file existence. `DispositionValidator.cs:278–290` never connects either to the named PR, affected cases, passing regressions, actual size, or dependencies.

   A concrete replacement for the synthetic positive control is:

   - Set all repairs to `merged`.
   - Set their merge commit to checkout HEAD, `b5c0022465f03118a59097a30d01775ed3d87299`.
   - Give R-NUM PR `9999` and a nonempty branch.
   - Use existing `tests/Calor.Compiler.Tests/SoundnessDisposition/DispositionTests.cs` as every witness.
   - Set closure to CLOSED/SUCCESS.

   **By inspection, this passes the real callbacks and every validator rule without a numeric repair.** I checked that this SHA is an ancestor of HEAD and is **not** an ancestor of the checkout’s `origin/main`.

   This also contradicts the frozen provenance rule: HEAD/branch identities are rejected, and reachability alone is insufficient (`v0.24-evidence-contract.md:294–305`). The record itself promises merge commits “on main” (`dispositions.json:31`).

   Bind closure to durable evidence for each accepted PR: verified main identity, repair contents and affected cases, regression results, measured size, and dependencies. Add independent negative controls for unrelated existing commits/files and fake PR identities. The current absent-SHA control does not establish those properties.

2. **MAJOR — A locally failed S2 record still cannot prevent terminal success.**

   D016 fixes the local marker, but the claimed propagation remains prose only. `EvidenceContractValidator.cs:675–676` accepts contract, inventory, claims, and a terminal record—**no S2 disposition record or closure result**. Its success checks at `:719–737` do not inspect findings.

   Consequently, `FailedRecord_ClosesOnlyAsMilestoneFailed` can validate a failed S2 record (`DispositionTests.cs:147–156`), while the existing terminal success fixture independently assigns `gate:#1413` BOUNDED and `MILESTONE-SUCCEEDED` (`EvidenceContractTests.cs:1613–1625`).

   The contract requires zero unresolved false proofs and no MILESTONE-FAILED findings (`v0.24-evidence-contract.md:417–418`). Bind terminal validation to the actual validated S2 record and add a control where a failed S2 closure rejects an otherwise valid terminal success record.

3. **MAJOR — Known spurious-refutation residuals have no allowed disposition.**

   R-OBL explicitly retains spurious refutations from same-condition getters and properties hiding inherited fields (`dispositions.json:104`). The final repair response calls this a compile-error residual “left to the maintainer” (`reviews/fix-obligation-state/verification-2-codex.md:61`).

   These defects receive neither a finding/discovery disposition nor a failure decision: R-OBL’s discovery list is empty (`dispositions.json:119`), and the discovery inventory contains only D-1493 (`:2690–2706`).

   The retained reviews also confirm a **throwing-predecessor reachability limitation** (`verification-2-codex.md:50`), which the updated residual summary omits.

   The frozen discovery rule requires every finding to be fixed, visibly demoted, excluded by the preregistered scope, or fail the milestone (`v0.24-evidence-contract.md:368–370`). The report expressly applies that requirement to spurious refutations (`v0.24-s2-dispositions.md:84–88`). An unresolved compile-error defect needs an explicit disposition; a `residual` string cannot substitute for one.

   Inventory these discoveries, preserve their witnesses and uncertainty, and record their permitted resolution or failure path.

4. **MAJOR — R-OBL exceeded the frozen review-round ceiling without a recorded exception.**

   The accepted ceiling is **three review rounds per PR, then close and rescope** (`contract.json:317`; `v0.24-evidence-contract.md:488`). Stopping rule 1 requires recording the overrun and an amendment before further work, or BLOCKED (`v0.24-evidence-contract.md:398–399`).

   R-OBL underwent three adversarial rounds, then **two additional adversarial REQUEST-CHANGES passes**, each followed by compiler changes and new regressions. Those changes are recorded at `reviews/fix-obligation-state/verification-codex.md:48` and `verification-2-codex.md:60`.

   These passes performed substantive review-and-repair iterations. Calling them “verification-only” does not supply an exception to the frozen ceiling. The contract records no review-round exception.

   Record and resolve this capacity overrun through the contract’s amendment/stopping mechanism before presenting the repair as eligible for acceptance.

5. **MAJOR — `validatedProofCases` can still be fabricated or removed without rejection.**

   The committed counts are correct, but the validator never reads this field. Its row checks cover status, criticality, disposition, reason, and finding references (`DispositionValidator.cs:171–200`); its inputs contain no case-results packet (`:30–38`).

   For example, NUM-MIXED-U64-SIGNED correctly records zero proofs (`dispositions.json:695–700`). Changing that count to `1` or `999`, deleting it, and replacing the reason with a positive proof-count statement triggers **no violation**, by inspection.

   Thus the round-1 observation-field enforcement gap survives under the replacement field. Recompute these counts from pinned S1 case results and reject missing or mismatched counts.

6. **MINOR — R-CACHE’s claimed review evidence is missing from the checkout.**

   `dispositions.json:56–57` names `reviews/fix-cache-literal-width/` and claims round-3 and verification APPROVE verdicts. **That directory does not exist.** The checkout contains repair records for the other four opened PRs.

   The report nevertheless says every repair’s review records are retained and includes #1494 among verified approvals (`v0.24-s2-dispositions.md:58–68`).

   Add the missing records or qualify the unsupported review claims. The validator currently does not check review-path existence.

7. **MINOR — R-OBL’s change description preserves the exemption that its final fixes removed.**

   `generate-dispositions.py:53` says member/element reads **outside a proof condition** count as possible heap writes. The final response instead says **every** such read, including proof-condition reads, participates in mutation and fact killing; only counterexample exactness has the span exception (`reviews/fix-obligation-state/verification-codex.md:48`, `verification-2-codex.md:45`).

   Correct the generator and JSON description. The present wording describes the exemption responsible for the repaired sequential-getter false proof.

Quantitative checks passed: **34 findings and 100 rows per baseline**, matching finding metadata, identical run-1/run-2 finding packets, correct disposition totals, correct proof-case counts, three matching source hashes, and byte-identical generator output captured in memory. The test inventory is **39 = 6 positive + 33 negative**. Negative controls assert exact code sets, but `Distinct()` still does not verify subjects or multiplicity.

Mentally, the committed OPEN record has no violations; closing it produces **seven D010 violations plus D016**. No dotnet execution or repair-branch code inspection was performed.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING closure accepts unrelated existing evidence | Fixed in the validator. `ClosureEvidence` now has three checks. The merge commit must be reachable from main (the real callback uses `origin/main` and fails closed if the ref is missing). Each regression witness must exist in that commit (`git cat-file -e sha:path`). The commit message must name the repair's PR (`#<pr>`). New negative controls cover each check: a commit reachable only from a branch, a witness absent from the merge commit, and the merge commit of another PR or an invented PR number. A real-git check confirms that the branch HEAD is not on `origin/main`. What remains outside the validator is whether the PR's contents and its passing regressions match the record. That is the maintainer's merge review, and the closure note says so. |
| 2 MAJOR terminal success does not consume the S2 result | Not changed here: recorded as open question Q6. The terminal validator is R0's (#1407) `EvidenceContractValidator`, and binding it to this record is an R0 change outside S2. The record and the report say the maintainer must carry `closure.result` into the terminal adjudication until that binding exists. |
| 3 MAJOR known spurious-refutation residuals undispositioned | Fixed. Two discovery entries were added: `D-OBL-PROOF-GETTER` and `D-OBL-THROWING-PREDECESSOR`. Both are `registered: false` and `observed: false`, with the witnesses and the source. Each is `MILESTONE-FAILED` by the frozen default until decision Q7, whose options are an amendment, the reserved slot, or accepting the failure. With these entries, closure under the current record is `MILESTONE-FAILED`, and the report says so. |
| 4 MAJOR R-OBL review-round overrun | Recorded. `repairs[R-OBL].reviewRoundOverrun` describes the two post-round-3 passes with code changes. Open question Q8 applies stopping rule 1: amend before accepting R-OBL, or treat it as BLOCKED and rescope. No further R-OBL changes were made. |
| 5 MAJOR `validatedProofCases` unenforced | Fixed. New rule D018: when the S1 case results are supplied, the validator recomputes the validated-proof count of every CLEAN row from `bench/correctness/false-established/v024/run2/<baseline>/case-results.jsonl` and rejects a missing or different count. The tests always supply the case results. Negative control: a changed count and a deleted count give exactly two D018 violations. |
| 6 MINOR R-CACHE review records missing | Fixed. The four #1494 records were added. A new test asserts that every opened repair's `reviews` directory exists and contains records. |
| 7 MINOR R-OBL change text describes the removed exemption | Fixed. Every member/element read, a proof condition included, counts as a heap write. Only the exactness of a `§PROOF` counterexample ignores reads inside its own condition. |
| Tests | DispositionTests: 45 cases (+6), all passing. Manifest +6 via the script. |
