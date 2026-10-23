# C1 #1423 review round 1 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **BLOCKING — tests/Calor.Compiler.Tests/EvidenceContract/CandidateManifestTests.cs:53:** The documented command executes the classifier from the checkout being judged. A later commit can weaken `ClassifyRange` and exempt its own modification. `CandidateInvalidationTests.cs:233` tests changed source as inert text while executing the original implementation; it does not test this attack. **Fix:** execute a classifier pinned to the reviewed C1 landing commit, with a trusted bootstrap that verifies its code and policy hashes. Add a control that actually executes a weakened target implementation.

2. **BLOCKING — tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs:124:** Downstream directories exempt additions, modifications, and deletions forever. After C2 freezes its claim registry, changing that registered `.json` input still returns exempt. `scripts/verify_release_adjudication.py:244` consumes that registry to determine required adjudication subjects. File extensions do not prove these files cannot affect publication. **Fix:** distinguish initial evidence creation from subsequent frozen-input changes; invalidate modifications or deletions of registered downstream inputs, with controls covering the C2 freeze.

3. **MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs:111:** The test-manifest exception ignores both phase and file modes. Any later count increase plus appended note passes, including a simultaneous `100644 → 100755` change. No corresponding C1 test addition is required. This exempts changes to a registered release input after landing. **Fix:** restrict the exception to the reviewed C1 delta before landing, require regular modes, and test post-landing increases and combined content/mode changes.

4. **MAJOR — docs/plans/evidence/c1-1423/candidate-manifest.json:331:** `runnerImages` records runner labels, not environment-image identities. Ordinary production also retains SDK `10.0.x`, with realized versions deferred to C2. Thus the required immutable environment/toolchain fields remain unresolved; hosted-image or SDK drift can occur without any git diff for the classifier to detect. **Fix:** record exact toolchain and environment identities for every registered producer and check execution against them, or obtain an explicit contract amendment permitting deferred environment binding.

5. **MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/CandidateManifestTests.cs:135:** The required `amendmentLogSha256` is never checked. The comparison also ignores `afterDecisionBearingInspection`, despite the manifest claiming every entry is compared field by field. Either value can be falsified while the required C# check passes. I recomputed the current hash successfully; the defect is missing enforcement. **Fix:** recompute the canonical hash, compare every recorded amendment field, and add separate discriminating tamper controls.

6. **MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/CandidateManifestTests.cs:159; docs/plans/evidence/c1-1423/generate_candidate_manifest.py:138:** Child verification checks only supplied entries for ancestry. Deleting G1 from both `inputs.json` and the manifest passes both verifiers; substituting an unrelated ancestor for its PR merge also passes. Submodule verification similarly permits omitted entries. These checks cannot establish a complete accepted child set or correct PR identity. **Fix:** validate exact required sets against the contract and independently recorded acceptance identities; compare complete gitlink inventories. Add omission and wrong-but-ancestral merge controls.

7. **MINOR — docs/plans/evidence/c1-1423/generate_candidate_manifest.py:149:** Every child’s issue state is hardcoded `OPEN`, including B1/#1276, contradicting the manifest’s own `CHILD-ISSUES-OPEN` statement that #1276 is the exception. **Fix:** store per-issue observed states with their observation provenance, or omit unverifiable issue-state assertions.

## Dispositions

1. **Fixed.** The trust anchor is the C1 landing commit. With `CALOR_C1_CLASSIFY_TARGET` set,
   `RangeFromTheCandidateIsNotInvalidated` fails unless C1 has landed and the running checkout's C1
   files equal the landing's; the record documents running from `git worktree add <dir> <landing>`.
   After the landing, any edit to C1 files is `INVALID` (control
   `AfterLandingTheClassifierCannotExemptItsOwnEdit`). No control executes a weakened build: the
   anchor is procedural, so a weakened checkout is excluded by where the check runs.
2. **Fixed.** After the landing and in `c2-1424/` and `adjudication-1408/`, only newly added
   regular data files are exempt. Modifying or deleting a record invalidates
   (`ModifyingOrDeletingARecordInvalidates`).
3. **Fixed.** The test-manifest exception applies only before the landing, with regular mode on
   both sides (`TestManifestBumpAfterLandingOrWithAModeChangeInvalidates`).
4. **Recorded, not resolved.** Hosted runner images cannot be pinned, and ordinary CI requests SDK
   `10.0.x`. Open item `TOOLCHAIN-AND-IMAGES` now says the manifest records labels, not image
   identities, that the classifier cannot see environment drift, and that whether deferred binding
   satisfies #1423 or needs a #1407 amendment is a maintainer decision.
5. **Fixed.** `amendmentLogSha256` is a canonical line format recomputed by the test;
   `afterDecisionBearingInspection` is compared; two tamper controls added. The full log text is
   bound by the `contract.json` hash in `sha256.json`.
6. **Fixed.** The child set must equal `contract.json` `children` (other than C1, C2, A1) plus R0;
   every merge commit must be GitHub's `Merge pull request #N` commit for that PR (stacked #1474 is
   documented as resting on the API record); S2 repairs must equal `dispositions.json` and appear in
   the S2 child; the complete gitlink inventory is compared. Tamper controls: removed child,
   wrong-but-ancestral merge, removed submodule.
7. **Fixed.** Per-issue states (`#1276` CLOSED, others OPEN) with observation provenance in
   `inputs.json` and the manifest.

Also fixed while addressing these: the manifest's `invalidation.rule` claimed a net-diff check that
the classifier does not perform; the text now describes the per-commit rule only.
