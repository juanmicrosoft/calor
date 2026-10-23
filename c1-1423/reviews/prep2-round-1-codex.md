# C1 #1423 prep for the second freeze (PR #1533), review round 1 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `95ddf545` and read-only repository access. Under the contract §9
independence deviation this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **BLOCKING — Proposed candidate still cannot pass R2.** `docs/plans/evidence/evidence-contract-1407/artifact-inventory.json:295` and eight other entries remain `stale`. `scripts/verify_release_adjudication.py:281` unconditionally rejects all nine with `G008`; successful regeneration alone cannot change their frozen classification. I reproduced those nine failures with an otherwise valid synthetic terminal record. **Fix:** merge a reviewed #1407 inventory amendment before C1 freezes the candidate, naming the merged repairs and justified classifications, producers, and regeneration commands. Preserve unresolved findings honestly. Freeze a commit containing that amendment; adding it afterward invalidates the candidate.

2. **MINOR — Binding complexity claim exceeds the repair.** `CHANGELOG.md:320` and `website/content/changelog.mdx:326` say binding "no longer grows with the square of the module size." Quadratic paths remain: `Binder.cs:4715` copies the accumulating callable-state map, and `Binder.cs:2381` scans module-wide symbols for nominal types. The paragraph itself acknowledges these residuals. **Fix:** describe the specific regression removed, such as "Name binding avoids module-wide symbol-table copies at each loop," and retain the measured synthetic example.

3. **MINOR — Governing protocol overstates compiler coverage.** `docs/plans/evidence/g2-1421/protocol.json:550` describes coverage as the compiler classes that construct the verifier or Z3 directly. Six current S2 classes satisfy that description but are excluded, as the new README correctly explains. The README explicitly gives JSON precedence. **Fix:** qualify `scope.covered` as the 18 registration-base classes listed in `cases.json`, add the six excluded classes to `scope.notCovered`, and refresh the protocol hash within amendment 1.4.0.

The registry changes themselves check out: exact counts, UTF-8 sorting, multiplicities, unchanged class filter, and complete rename-back. Both validators pass D001–D016; 30 controls pass; a complete synthetic execution is accepted. Both release sections are identical and pass G012. Amendment 1.4.0 itself requires neither a #1407 amendment nor a ceiling exception; the separate inventory blocker does.

(The reviewer also reported that the 555-name compiler registry matches every result in C2's retained
compiler TRX files after the rename-back.)

## Dispositions

1. **BLOCKING: confirmed, not fixable in this PR. STOP and escalate.** `check_terminal` fails `G008`
   for each of the 9 artifacts that the inventory classifies `stale`. The contract's
   `reclassificationRule` allows a change only through a versioned #1407 amendment that names the
   repairing PR. `G005` requires the contract packet to be the same at the candidate and at the
   adjudication commit, so the amendment must be part of the candidate. C2's regeneration record
   (open item 5) had already listed this. The task rules require escalation to the maintainer for
   any contract amendment. Round 2 waits for that decision.
2. **MINOR: fixed.** The entry is now titled "Name binding no longer copies module-wide tables at
   every loop." The measured example and the two remaining costs stay as they were.
3. **MINOR: fixed in amendment 1.4.0.** `scope.covered` now names the 18 classes registered in
   `cases.json`. `scope.notCovered` lists the six S2 classes. The justification says so, and
   `sha256.json` is re-sealed.
