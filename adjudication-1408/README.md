# 0.24 A1: terminal adjudication record (#1408): sign-off packet

**For:** @juanmicrosoft, the #1408 adjudicator under the contract §9 independence deviation.
**Prepared by:** an A1 agent. The agent proposes; you decide.

**Status: stopped at BLOCKED subjects.** The A1 instruction was to say so and stop if any subject
had to be `BLOCKED`. Nine do, so this branch was not pushed and no PR was opened. Review stopped
after Codex round 1.

## What the record says

**Proposed terminal outcome: `MILESTONE-FAILED`.** Conditions 3, 7, and 9 of the §8 predicate do
not hold.

All 75 required subjects are adjudicated exactly once:

| Kind | `BOUNDED` | `HISTORICAL-ONLY` | `BLOCKED` |
|---|---|---|---|
| Inventory artifacts (34) | 24 | 10 | 0 |
| Gates (13) | 6 | 0 | **7** |
| Registered claims (28) | 26 | 0 | **2** |

No row is `SUPPORTED`. Every row records `independence = reduced-maintainer-adjudicated` and the
published limitation verbatim.

### The three failed conditions

- **Condition 9 (no ceiling exceeded without an amendment).** `review-rounds-per-pr` (3) was
  exceeded with no exception. C1 PR #1508 had 4 change-requesting Codex passes after round 3, each
  followed by a fix. G2 PR #1479 had 2, and six more PRs had 1 each. Amendments 1.3.0 and 1.3.1
  counted exactly this pattern as an overrun that needs an exception. #1508 (8 passes) and #1479
  (6) also exceed a looser "3 rounds plus one verification pass" budget. See `capacity-audit.md`.
- **Condition 3 (no `BLOCKED` row).** Seven gates are `BLOCKED` under stopping rule 1, which says
  that when a ceiling is reached you either merge an amendment or record the gate `BLOCKED`. No
  amendment covers these overruns. The gates are #1423, #1421, #1276, #1311, #1410, #1413, and
  #1422; each row's `blockedBy` gives its records. Two claims are also `BLOCKED` on their own
  evidence:
  - `claim:reproducible-release-evidence-and-gate` says a release can be published *only* through
    the gate. R2's record (§5 item 1) says older tags keep an ungated `publish-nuget.yml` that can
    read the repository-scoped `NUGET_API_KEY`.
  - `claim:no-benchmark-results-published`: on the designed release path, `benchmark.yml` pushes
    the B2 headline (17 pairs, geometric-mean r 1.235) to a public branch and opens a PR.
- **Condition 7 (every surface consumes one identity).** The release PR precedes the candidate and
  cannot consume an identity (R2 §6). The ungated older-tag path also remains. #1408 cannot waive
  either after the freeze.

### No repair inside 0.24

- §9 forbids amendments from the #1424 raw-artifact freeze until #1408 closes.
- §8 forbids #1408 to add, drop, or weaken a condition.
- Amendment 1.5.0 makes a further failure terminal.

### Consequences

- A `MILESTONE-FAILED` record never passes the #1410 gate (`G006`). Signing it means 0.24.0 is not
  published under this contract: no NuGet package, GitHub release, website deploy, or benchmark PR.
- The epic's outcome sentence about independence is not met in any case
  (`epicIndependentAdjudicationMet = false`).
- R2 §7 defines no maintenance-release path. Any later release needs a new contract decision after
  #1408 closes.

## Terminal success predicate (contract §8)

| # | Condition | Holds | Evidence |
|---|---|---|---|
| 1 | Every child other than #1408 closed with closure evidence | At sign-off, once the issues are closed | Gate rows; `c1-1423/candidate-manifest.json` |
| 2 | R1 release-critical rows (B1 and N1) all have an outcome | Yes | 88 rows per baseline: 74 clean within budget, 10 finding, 4 false proof. The 7 `XCL-*` rows are not release-critical |
| 3 | No adjudication row `BLOCKED` | **No** | 7 gates (stopping rule 1) and 2 claims |
| 4 | Zero unresolved false unconditional proofs | Yes | 7 false proofs per baseline plus `D-NUM-WHILE-BOUND`, each fixed or demoted |
| 5 | Every #1413 finding dispositioned | Yes | B1 and N1: 34 each (13 fix, 21 demote); discoveries: 4 |
| 6 | Every authoritative and derived artifact regenerated on the candidate | Yes | Regeneration 3. `nuget-packages` is excluded by §7, and `candidate-packages` stands in for it |
| 7 | Every §7 surface consumes one matching identity | **No** | The release PR (R2 §6); the older-tag credential path (R2 §5) |
| 8 | Every row reduced-maintainer-adjudicated; none `SUPPORTED` | Yes | By construction (`G008`/`T002`) |
| 9 | No §9 ceiling exceeded without a merged amendment | **No** | `capacity-audit.md` |

## Decisions recorded

| Id | Ruling |
|---|---|
| **AD-4** | Condition 9 does not hold. The overrun gates are `BLOCKED`. Outcome: `MILESTONE-FAILED` |
| AD-1 | The release PR is not waived. Condition 7 is not met under the frozen text |
| AD-2 | Seven artifacts have cutoff defects with no recorded resolution. Four rows name the resolving PRs. Three carry the residual as a limitation and stay `BOUNDED`: `toolchain-pins`, `quality-ratchet-baselines`, `sdk-consumer-check` |
| AD-3 | `claim:no-benchmark-results-published` is `BLOCKED` |
| AD-5 | #1276 was closed before its closure PRs merged (same day). The closure is accepted |

## Child issues (condition 1)

Even with a failed outcome, every gate's work is done and its evidence is on `main`. Close these
with the comments in the agent's report:

- the contract children #1419, #1311, #1413, #1420, #1421, #1135, #1241, #1417, #1422, #1410,
  #1423, and #1424 (#1276 is already closed);
- #1407 (R0);
- #1493 (fixed by #1497).

After merge, close #1408 and the epic #1409 as failed. Leave #1536 open.

## Known limitations

These would have bounded a success. They are recorded as they stand:

- Byte reproducibility holds only with Microsoft's SDK 10.0.401 build. The publish jobs request
  `'10.0.x'`, and the next .NET servicing release is due 2026-10-13.
- The Google Fonts fetch at build time can change the website bytes or fail its build. It failed
  once in #1542's CI.
- B2-07: the headline bytes depend only on the candidate. Other bytes on `main` make a publish-time
  run refuse.
- Tier 2's FAIL is registered: 428 compile, 51 rejected, 30 known failures.
- Determinism is bounded by 30 attempts per environment. Rare non-determinism is not excluded.
- Enforcement executed 694 of 695 tests in CI and 695 of 695 locally.
- Open C1 items:
  - no maintenance-release path;
  - the release PR versus condition 7;
  - the branch-protection and secret-scope recommendations are unenforced;
  - the classifier is not a required check.
- The nightly performance gate #1536 is not a release claim. On ubuntu, candidate-equivalent code
  measured 16.1 s and 24.9 s against the 24.0 s ceiling.
- `sdk-consumer-check` tests a package it packs itself.
- Ordinary-CI minutes and maintainer review hours were never recorded. The Actions API sum is about
  29,500 job-minutes on 0.24 PR branches, against a ceiling of 15,000.

## After merge

There are no publish steps. The identity is known only after merge, because its commit is the merge
commit:

```bash
git fetch origin && C=$(git rev-parse origin/main)   # the merge commit of the PR that adds this record
H=$(git show "$C:docs/plans/evidence/adjudication-1408/terminal-record.json" | shasum -a 256 | cut -d' ' -f1)
echo "calor-adjudication:v1:$C:$H"
```

`H` is the SHA-256 of the record as merged. Dispatching `publish-nuget.yml` with that identity
fails at the gate (`G006`, `G008`) and publishes nothing. Do not tag, release, or dispatch.

## Checks run

| Check | Result |
|---|---|
| `ValidateTerminalRecord` (T001–T003) on the committed record | 0 violations. For a failed outcome it checks only T003: subjects and allowed outcomes (`r2-dry-run/record/terminal-validator.txt`) |
| Negative control: the earlier `MILESTONE-SUCCEEDED` draft with one row set to `BLOCKED` | `T001`, as expected (`r2-dry-run/negative-control-blocked-row.txt`) |
| R2 gate on the committed record, with clone A's notes, packages, metadata, website, and benchmark worktree | FAIL: `G006` and `G008` (`r2-dry-run/record/gate.txt`) |
| R2 gate on a scratch variant of the earlier draft with the outcome set to success | PASS on every surface (`r2-dry-run/counterfactual/`). This is a gate control only. It shows the publication hashes still match; it does not show the record could succeed. The gate does not read `failedConditions` |
| C1 invalidation classifier from the `b04e963f` anchor against this branch | `NOT-INVALIDATED` (`reviews/classifier.txt`) |
| Codex round 1 | REQUEST-CHANGES; findings applied (`reviews/round-1-codex.md`) |

## Row table

#### Inventory artifacts (34)

| Subject | Outcome | Main limitation |
|---|---|---|
| `verifier-runtime-differential` | BOUNDED | Totals 429 Proven / 156 Assumed / 585 refuted over 1,170 cells of the 65 whitelisted forms only; no claim beyond the whitelist. |
| `modeled-forms-whitelist` | BOUNDED | Validated (ModeledFormsTests 9 passed in two fresh clones); the whitelist bounds what the differential covers. |
| `release-test-suites` | BOUNDED | 13 of 13 release-critical projects at the manifest's counts, one CI run (ubuntu-24.04) plus Ids and Performance locally on macOS. |
| `ci-test-reports` | BOUNDED | PASS-ONLY: a single CI run's reports, retained in git and on evidence/v0.24-archives. Enforcement 694/695 in CI vs 695/695 locally. |
| `test-manifest` | BOUNDED | Validated: every observed count equals expectedTotal/expectedSkipped (Compiler 13,038 with 3 skipped). |
| `z3-upstream-pins` | BOUNDED | Re-materialized and verified, never rebuilt. |
| `z3-release-binaries` | BOUNDED | 7 of 7 mirror assets match .github/z3-binaries-4.15.7.sha256; the release is a pinned mirror, not rebuilt. |
| `toolchain-pins` | BOUNDED | Lockfiles verified by restore --locked-mode in two clones and every CI restore. |
| `corpus-submodules` | BOUNDED | Re-materialized: the candidate's gitlinks MediatR fb309026, serilog 0597ddfb, FluentValidation 71b3c60c. |
| `roundtrip-reports` | BOUNDED | SINGLE-RUN on macOS (clone A); test.yml's roundtrip-verification job skipped itself by its change filter. |
| `roundtrip-baselines` | BOUNDED | No regression against eng/roundtrip-baselines.json, which records the current state, not a known-good state. |
| `quality-ratchet-baselines` | BOUNDED | Performance ratchet passed on macOS (median 12.869 s against 24.000 s). It was not run on ubuntu for the candidate. |
| `release-quality-reports` | BOUNDED | Every release-quality step ran once, locally in clone A on macOS, in order, all exit 0; not on ubuntu-latest (no workflow runs that job without the adjudication gate). The coverage step used Homebrew's dotnet host (planned). |
| `sdk-consumer-check` | BOUNDED | PASS-ONLY: five RID job conclusions on the candidate; the job logs are retained in git by C2. |
| `tier1-verification` | BOUNDED | PASS-ONLY: one CI job (verify-phase1) on the candidate; no AST round-trip claim is made (#1481). |
| `tier2-corpus-verification` | BOUNDED | Registered FAIL verdict: 428 compile / 51 rejected / 30 known failures on ubuntu and macOS, matching eng/tier2-fixture-expectations.json. The 30 known failures stay failures and never count as passes. |
| `ledger-provenance-index` | BOUNDED | Validated: LedgerCommitStampTests 5 passed in two clones against origin/main b04e963f. |
| `benchmark-corpus` | BOUNDED | Validated by the B1 packet validator (58 passed in two clones). Only 17 of 226 registered pairs are EQUIVALENT; equivalence never means either side is correct (contract §5). |
| `benchmark-results` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `benchmark-provenance` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `benchmark-results-md` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `website-benchmark-pages` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `benchmark-workflow-output` | BOUNDED | The headline bytes depend only on the candidate (no headline exists at the candidate). If main later carries headline or stamp bytes other than these, a publish-time run refuses (B2-07); it never writes other bytes. |
| `benchmark-publication-pr` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `llm-and-agent-results` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `changelog` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `github-release-notes` | HISTORICAL-ONLY | Historical-only by the inventory. The 0.24.0 notes candidate is rendered byte-identically (R2 digest 05b8c219...) and passes the G012 wording scan; it is adjudicated as a publication surface below, not as evidence. |
| `nuget-packages` | BOUNDED | Not regenerated by rule (contract §7); nothing is on nuget.org for 0.24.0 yet (newest: 0.21.0). The 0.24.0 bytes that may be pushed are exactly the adjudicated candidate-packages hashes, through the #1410 gate. |
| `candidate-packages` | BOUNDED | Byte-identical in two macOS clones and two Linux CI builds, all with Microsoft's SDK 10.0.401 build. Homebrew's source-built 10.0.401 packs different bytes (#1526). Bytes are not committed; hashes only. |
| `release-metadata` | BOUNDED | Generated from the candidate packages; identical in A, B, and Linux CI. Inherits the SDK-build bound of candidate-packages. |
| `website-deployment` | BOUNDED | Built, never deployed: 253 files, tree digest e684dcab... in A, B, and Linux CI. |
| `v019-audit-disposition` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `pre-cut-evidence-packets` | HISTORICAL-ONLY | Historical-only; withdrawn from 0.24 evidence. |
| `evidence-contract` | BOUNDED | Validated: EvidenceContractTests 324 passed in two clones, including the packet-hash check for contract 1.5.0. No amendment was made after the #1424 raw-artifact freeze. |

#### Gates (13)

| Subject | Outcome | Main limitation |
|---|---|---|
| `gate:#1419` | BOUNDED | Registration only: it fixes what S1 runs; it establishes no verdict. |
| `gate:#1311` | BLOCKED | Stopping rule 1: S1 PR #1480: 1 change-requesting pass after round 3 followed by fixes in e7cbce77 (s1-1311/reviews/round-verification.json). |
| `gate:#1413` | BLOCKED | Stopping rule 1: S2 record PR #1499: 1 change-requesting pass after round 3 followed by a test change (s2-1413/reviews/dispositions/verification-codex.md). |
| `gate:#1420` | BOUNDED | Consumer and RID inventory eng/z3-consumers.json; check-packaged-z3.py --all-rids passed on the candidate packages; the consumer matrices passed in one CI run. |
| `gate:#1421` | BLOCKED | Stopping rule 1: G2 PR #1479: 2 change-requesting passes after round 3, each followed by a fix; no exception (g2-1421/reviews/verification-pass, -2). |
| `gate:#1135` | BOUNDED | Closed on the candidate by execution c2-1424-regen-3: DETERMINISTIC, complete, every determinism row RESOLVED. Executions 1-3 stay on record and establish nothing. |
| `gate:#1241` | BOUNDED | Tier 2's verdict is FAIL by registered design (428/51/30); the gate's closure is the truthful, checkout-pinned run, not a pass. |
| `gate:#1417` | BOUNDED | Identities resolve in a fresh clone against fetched main; existing short stamps stay as history beside their durable identities. |
| `gate:#1276` | BLOCKED | Stopping rule 1: B1 PR #1473: 1 change-requesting pass after round 3 followed by a fix; amendment 1.1.0 raised only its PR size (b1-1276/reviews/verification-pass-codex.md). |
| `gate:#1422` | BLOCKED | Stopping rule 1: B2 PR #1527: 1 change-requesting pass after round 3 followed by fix bca13caf (b2-1422/reviews/pr2-verification-pass-codex.md). |
| `gate:#1410` | BLOCKED | Stopping rule 1: R2 PRs #1474/#1475: 1 change-requesting pass after round 3 followed by a fix (r2-1410/reviews/verification-pass-codex.md). |
| `gate:#1423` | BLOCKED | Stopping rule 1: C1 PR #1508: 4 change-requesting passes after round 3, each followed by a fix; no exception (c1-1423/reviews/verification-pass, refreeze-verification, -2, -3). |
| `gate:#1424` | BOUNDED | Regeneration 3 of 3 complete: no BLOCKED or FAILED row; 583 of 1,500 regeneration runner-minutes. Regenerations 1 and 2 stay as history; regeneration 2 (PR #1535) was closed unmerged. |

#### Registered claims (28)

| Subject | Outcome | Main limitation |
|---|---|---|
| `claim:release-adds-no-syntax` | BOUNDED | Checked against v0.22.0 (72a0a855): no new token kind or AST node; Token.cs, Lexer.cs, and ExpressionNodes.cs only add the internal WidthInferred flag (R-NUM). Parser.cs changes are the #1485 fix, which changes how existing ... |
| `claim:release-history-0.22-0.23` | BOUNDED | Contract §2 row B1 records the failed 0.22.0 publish run 34999741476. nuget.org lists calor and Calor.Sdk up to 0.21.0 only (checked 2026-10-10). No v0.23.0 tag exists. |
| `claim:evidence-bounded-not-independent` | BOUNDED | Holds by this record: every row is BOUNDED or HISTORICAL-ONLY with independence reduced-maintainer-adjudicated. |
| `claim:no-benchmark-results-published` | BLOCKED | On the designed release path, publish-nuget.yml dispatches benchmark.yml, which pushes the B2 headline (17 pairs, geometric-mean r 1.235) to a public branch and opens a publication PR (contract §7 benchmark publication ... |
| `claim:advisory-7-false-proofs-in-4-areas` | BOUNDED | 4 FALSE-PROOF rows per baseline holding the 7 false-proof findings; each fixed or demoted by S2. Found by a sampled sweep on one platform; other false proofs may exist. |
| `claim:sweep-limits` | BOUNDED | The numbers restate the S1 record; whole-compiler soundness is not established. |
| `claim:all-34-findings-fixed-or-demoted` | BOUNDED | 34 findings per baseline: 13 FIX-IN-0.24, 21 DEMOTE-IN-0.24; regression witnesses run in the candidate's suites. A demotion withdraws a claim; it does not prove the property. |
| `claim:obligation-stale-facts-demoted` | BOUNDED | R-OBL #1496 and R-OBL-RESIDUALS #1503 with their witnesses; demotion only. |
| `claim:cache-literal-width-fixed` | BOUNDED | R-CACHE #1494; witness S2CacheLiteralWidthTests passes on the candidate. |
| `claim:interface-checks-throwing-preconditions` | BOUNDED | R-IMPL #1495; quantifier-free integer contracts only; string, array, and user-type cases are Assumed (Calor0819). |
| `claim:non-ascii-solver-symbols-escaped` | BOUNDED | R-TEXT #1497 (#1493). The Windows collision was never observed; the fix is by construction, and the determinism protocol passed on win-x64 and win-arm64. |
| `claim:warning-calor0819-added` | BOUNDED | Added by R-IMPL #1495; present in the candidate's diagnostics and tests. |
| `claim:reproducible-release-evidence-and-gate` | BLOCKED | The claim says a release can be published only through the #1410 gate. R2's own record (release-gate.md §5 item 1) says older tags still hold an ungated publish-nuget.yml that can read the repository-scoped NUGET_API_KEY, and ... |
| `claim:benchmark-publication-refuses-incomparable` | BOUNDED | Refusal paths are tested end to end; the freshness check covers only the headline's inputs, as the notes say. |
| `claim:agent-refactoring-job-no-commit` | BOUNDED | benchmark.yml at the candidate: the job is contents: read with no commit step; ReleaseWorkflowGateTests.EveryPublishingCommandFollowsTheGateInItsJob passes. |
| `claim:historical-benchmark-numbers-labeled` | BOUNDED | The old numbers stay published as history (inventory: website-benchmark-pages historical-only); the label does not make them valid. |
| `claim:z3-single-verified-path-five-rids` | BOUNDED | check-packaged-z3.py --all-rids passed on the candidate packages; consumer matrices passed in one CI run. |
| `claim:fixture-checks-truthful` | BOUNDED | 428 + 51 + 30 = 509 tracked fixtures; Tier 2's overall verdict is FAIL by design. |
| `claim:numeric-forms-refused-or-demoted` | BOUNDED | R-NUM #1502 (amendment 1.3.2 rule, no solver query); D-NUM-WHILE-BOUND demoted. |
| `claim:differential-totals-429-156-585` | BOUNDED | Byte-equal in two clones and ubuntu CI; totals over the whitelisted forms only. |
| `claim:text-utf16-code-units` | BOUNDED | R-TEXT #1497; proofs touching strings stay Assumed and keep their runtime checks. |
| `claim:nested-quantifier-unsupported` | BOUNDED | R-QNT #1498; a conservative restriction, also for nested forms the compiler could check at run time. |
| `claim:determinism-causes-fixed` | BOUNDED | The three causes are fixed by G3 #1492/#1500; protocol 1.5.0 on the candidate returned DETERMINISTIC (30 attempts per environment; rare non-determinism not excluded). |
| `claim:unreachable-counterexamples-withheld` | BOUNDED | R-OBL #1496 and #1503; withholding may also hide real counterexamples, as the notes say. |
| `claim:empty-clause-body-fixed` | BOUNDED | #1491 (issue #1485, closed); the regression tests run in the candidate's compiler suite. |
| `claim:cache-format-1.22` | BOUNDED | VerificationCacheEntry.CurrentFormatVersion is "1.22" at the candidate. |
| `claim:packages-and-website-reproducible` | BOUNDED | Only with Microsoft's SDK 10.0.401 build; the Google Fonts fetch at build time can change the website bytes or fail the build (it failed once in #1542's CI). |
| `claim:binding-performance-restored` | BOUNDED | #1525; Binding_MediumModule_Under500ms passes on the candidate. The suite-median ratchet still exceeds 24 s on some ubuntu runners (#1536, not a release claim; fix after 0.24.0). |

