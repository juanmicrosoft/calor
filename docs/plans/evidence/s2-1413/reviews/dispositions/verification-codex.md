# S2 #1413 dispositions — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Two round-3 findings remain partially resolved. These concern closure enforcement, not the honestly OPEN disposition record.

1. **BLOCKING — Repair acceptance remains unbound (round-3 finding 2).** The exact merge-message check and S2 branch prefix reject the #1483 witness, but all identity fields still come from the editable record. Nothing binds them to reviewed acceptance evidence for the affected findings. By inspection, after #1499 merges, every repair could name that disposition-only PR, its S2 branch, its merge commit, and `DispositionTests.cs` as its witness; the closure rules would accept those declarations without compiler repairs. See `tests/Calor.Compiler.Tests/SoundnessDisposition/DispositionValidator.cs:299–319`. The new control supplies #1483’s message while retaining the original repair identities, so it does not exercise consistent replacement (`DispositionTests.cs:467–477`).

   The maintainer-review boundary is honestly documented at `docs/plans/v0.24-s2-dispositions.md:175–179`, but the requested durable acceptance binding remains absent. Bind closure to reviewed acceptance evidence, or explicitly retain this finding as an unresolved validation limitation.

2. **MAJOR — Any nonempty text clears the review overrun (round-3 finding 3).** D010 checks only whether `overrunAmendment` contains text (`DispositionValidator.cs:321–323`). The successful fixture inserts `"example: contract amendment recording the R-OBL overrun"` without changing the contract (`DispositionTests.cs:124–125`, `:187–190`). Thus an unamended overrun still permits successful closure. The frozen rule requires a **merged amendment raising the ceiling**, not an amendment description (`docs/plans/v0.24-evidence-contract.md:398–399`, `:425`).

   Q8 candidly records the outstanding decision (`docs/plans/evidence/s2-1413/dispositions.json:2765–2766`). That decision need not be resolved here, but the validator must either bind to its actual amendment evidence or honestly record the remaining manual enforcement boundary.

The other round-3 resolutions check out:

| Finding | Verification |
|---|---|
| 1 — Discoveries disappear | Fixed: external ID pins, exact-once validation, preserved positive fixture, deletion and failed-closure controls. `DispositionTests.cs:66`, `:129–136`, `:194–214`; `DispositionValidator.cs:252–255`. |
| 4 — Runtime skip | Fixed: deterministic unknown-commit Fact at `DispositionTests.cs:489–494`. `python3 -B scripts/check_test_quality.py` passes. |
| 5 — Optional/missing proof counts | Fixed: required input and explicit missing-row rejection. `DispositionValidator.cs:45–46`, `:192–196`. |
| 6 — Stale report | Fixed: D001–D018, main/PR/branch closure wording, and two obligation residuals. `v0.24-s2-dispositions.md:12–21`, `:61–63`. |

Q6 accurately records the terminal-binding gap: `EvidenceContractValidator.cs:675–676` takes no S2 record. The obligation residuals remain explicitly unobserved and `MILESTONE-FAILED` (`dispositions.json:2693–2720`); the repair review’s final response admits that its last fix was not re-reviewed (`reviews/fix-obligation-state/verification-2-codex.md:60–61`).

No production compiler files changed in this PR, so its round-3 changes introduce no executable false proof, false `Discharged`, or fabricated counterexample. The remaining defects concern false closure acceptance.

Verification passed for generator byte identity, all three source hashes, finding/row metadata, and proof counts. Each baseline has **34 findings, 100 rows, 13 FIX / 21 DEMOTE**, seven false proofs split **2 FIX / 5 DEMOTE**, and **44 clean rows with zero validated proofs**. There are **49 Facts**, no new skips, and **12,587 + 49 = 12,636**, matching `eng/test-manifest.json:8–9`. Dotnet was not run; the author’s passing-test claim remains independently unverified.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING repair acceptance unbound | Partly closed, and the rest is retained as a recorded limitation. The tests now pin the opened repair PR numbers outside the record (R-CACHE #1494 … R-QNT #1498). A repair that names another PR, such as the disposition-record PR #1499, gets D008; there is a negative control. Binding the merged contents to the reviewed repair and its affected findings is not mechanized: it stays the maintainer's merge review. The record's `validatorLimitations` and the report say so. |
| 2 MAJOR any text clears the overrun | Retained as a recorded limitation. The validator cannot check that `overrunAmendment` names a merged contract amendment that raises the ceiling. Q8 enforcement is manual, as `validatorLimitations` states. |
| Review budget | This is the record's verification-only pass after 3 rounds. No further review was run. |
