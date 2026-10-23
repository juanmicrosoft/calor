# 0.25 release candidate: freeze machinery (#1430)

The 0.25 form of 0.24's C1 (#1423, `docs/plans/v0.24-c1-candidate.md`). Scope amendment 1.3.0
(`acceptancePreconditions.reproof`, check `provenance-candidate-manifest`) requires "a candidate
manifest in the 0.24 C1 form ... frozen for the 0.25 candidate SHA". 0.24's generator and classifier
are tied to 0.24 (contract 1.5.0, S2 closure, G3 execution 2, `c1-1423/` paths), and editing them
would invalidate the frozen 0.24 record, so 0.25 has its own. Nothing here publishes, tags or releases.

**PRs.** (1) Release prep (#1575): version 0.25.0, the byte-identical `[0.25.0]` notes in `CHANGELOG.md`
and `website/content/changelog.mdx`, the generator and the classifier. (1b) Pre-freeze decisions
(#1576): see "Freeze blockers"; the candidate is its merge commit. (2) Freeze: `inputs.json` (hand-recorded candidate, merge commits, issue states, observed
toolchain, decisions, open items), the generated `candidate-manifest.json`, and
`V025CandidateManifestTests.cs`, which re-verifies the manifest from git, checks the identity
through P1's `DurableProvenance.Verify`, and runs the scope, T1 and D1 validators on the
candidate's blobs.

## The re-freeze (#1430 Step B): `d1d7afde` supersedes `48cae911`

The first candidate, `48cae911`, is **superseded**. The re-proof on it (#1581,
`docs/plans/evidence/v0.25-regeneration/regeneration.md`) found three blockers, and the maintainer decided
`D-AMEND-REFREEZE` (@juanmicrosoft, 2026-10-20, "Amend + re-freeze"). #1610 amended all three and its merge
is the new candidate. No acceptance record, adjudication identity or publication may name `48cae911`.

| Cause | What the re-proof found | Amended by (#1610) |
|---|---|---|
| `DETERMINISM-INCOMPLETE` | G2 execution `v025-reproof-1` (run 39323628059) INCOMPLETE: 660 AGREE-PASS, 0 DISAGREE, 1,644 INCOMPLETE; osx-arm64 job 2 hit its 50-minute timeout | G2 protocol 1.7.0 (`D-G2-RAISE-CAP-2-RUNS`, "Raise cap to 2,100, 2 runs") |
| `F2-INTERP-09-COUNTS` | registered corpus counts measured on F2's branch differed on the candidate | T1 register 1.0.3 (`D-F2-INTERP-09-RECANDIDATE`, "Re-register to candidate") |
| `F4-ITER-04-UNMEASURABLE` | no CLI surface can measure the three FluentValidation modules as preserved | R0 scope 1.5.0 `notMeasuredCases` (`D-F4-ITER-04-NOT-MEASURED`, "Amend to not-measured") |

| Field | Value |
|---|---|
| Candidate | `d1d7afde672691613f7bb34d253e63401ee423ac`, the merge of #1610, committed 2026-10-20T20:37:38Z; the tip of `origin/main` at the re-freeze (2026-10-20T20:39:52Z) |
| Version | 0.25.0; notes dated 2026-10-20; no `v0.25.0` tag, local or remote |
| Tree | `d00b3c43bf1f7a6962354115622e024c80f6f203` (equal to #1610's head `167b8d02d`) |
| Release SDK | 10.0.401; `dotnetSdkBuild` `sha256:a5b223b4…` and compiler runtime 10.0.12 (Linux x86_64, reproducible-builds run 39486340748 on that tree, both builds, after `check-runtime --prune`) |
| Release gaps | #1545, #1546, #1547 closed (unchanged); the live credential check, re-run from a fresh clone at the candidate, reports `CLOSED` (Actions workflow permissions by maintainer attestation): `release-credentials-live-2026-10-20.txt` |
| Protocol, scope, register | G2 1.7.0, R0 scope 1.5.0, T1 register 1.0.3, D1 contract 1.0.7, all at the candidate |
| Residuals | #1578's two (`D-ACCEPT-FILE-FREEZE`, unchanged); `GATE-PREVIOUSAFTER-BOOL` (MINOR, `reviews/stepA-verification-codex.md`): the gate's T005 port accepts `previousAfter` = `false` for a recorded 0. The fix is in `scripts/verify_release_adjudication.py`, a candidate input, so it is a known residual of this freeze |

**Landing.** The landing is the first first-parent commit of `origin/main` after the candidate whose
`candidate-manifest.json` names the candidate (`candidate.commit`): this re-freeze PR's merge. The manifest
path is the same as the first freeze's, so the classifier skips a commit whose manifest names another
candidate (`FindLanding`, control `AReFrozenCandidateLandsWhereItsOwnManifestDoes`).

**The re-proof needs new paths for `ready/` records.** T1 register 1.0.3 pins
`docs/plans/evidence/v0.25-f2-906/ready/cases/F2-INTERP-09.json` (the measurement on `48cae911`) as the
re-registration's measurement, and the classifier allows only newly added `ready/` files. The re-proof on
`d1d7afde` therefore writes its F2-INTERP-09 record, and every other family's `ready/` records, under a new
path, for example `ready/d1d7afde/cases/`. The #1581 records name `48cae911` and do not count for this
candidate. The second G2 execution's prerequisite record goes to
`docs/plans/evidence/v0.25-regeneration/determinism/epoch-results/<runId>.json`.

## The first freeze (#1430 step 2, superseded)


| Field | Value |
|---|---|
| Candidate | `48cae911700ceb0dfc358e00933650445d81604f`, the merge of #1576, committed 2026-10-19T18:18:59Z; the tip of `origin/main` at the freeze (2026-10-19T18:23:52Z) |
| Version | 0.25.0; no `v0.25.0` tag, local or remote |
| Tree | `45d072a911d298b8c802219b206c80b351c32f19` (equal to #1576's head `f8413cc9e`) |
| Release SDK | 10.0.401; `dotnetSdkBuild` `sha256:a5b223b4…` (Linux x86_64, reproducible-builds run 39295103845 on that tree, both builds) |
| Release gaps | #1545, #1546, #1547 closed 2026-10-19 at 15:23:15Z, 15:23:17Z, 15:23:19Z; the live credential check reports `CLOSED` (Actions workflow permissions by maintainer attestation), output in `release-credentials-live-2026-10-19.txt` |
| Accepted residuals | `D-ACCEPT-FILE-FREEZE` (@juanmicrosoft, 2026-10-19, "Accept, file issue, freeze"): the gate's code-span link-text anchors (MAJOR) and the coarse S004 parity theory (MINOR), tracked in #1578; record `reviews/prefreeze-exception-codex.md` |

`candidate-manifest.json` is generated from `inputs.json`; `V025CandidateManifestTests` re-verifies it from git,
runs the scope, T1 and D1 validators on the candidate's blobs, and classifies the range from the candidate.
The first freeze's landing was #1579's merge (`51249109`).

## Generator

`generate_candidate_manifest.py` reads only git objects at the candidate; `--check` regenerates
and compares; every run executes its self-test (0.24's 13 tag-rule controls, serializer and SDK-pin
controls). It fails closed on:

- a shallow clone, unfetched `origin/main`, or a candidate that is not GitHub's merge of the prep
  PR on main's first-parent chain; a wrong version, or a `v<version>` tag (local or remote) that
  does not peel to the candidate;
- release sections that differ between the two changelogs or claim independent review; a `0.25.x`
  map entry that is not `not-yet-adjudicated`;
- a release-workflow job whose first `setup-dotnet` install is not 10.0.401, whose later installs
  are not the auxiliary 8.0.426, or that uses `global-json-file` (`ReleaseSdkPinTests`' rule);
- the scope packet not `FROZEN`/`MET`, its `evidenceSeal` not recomputing, or
  `acceptancePreconditions` without the own-SHA re-proof and the three release gaps; T1 approved
  hashes or approval seal not recomputing; T1 or W1 consuming another scope version;
- a packet seal unequal to the pin in the candidate's own tests: scope (`InteropScopeTests`), D1
  (`RefOutContractTests`), T1 baseline and amendments (`T1RegisterTests`), recomputed with a
  System.Text.Json-compatible serializer, with three controls on the real packets;
- a missing 0.24 S2 repair or witness (the re-proof runs them; the freeze does not);
- a gate set other than R0, D1, F1-F6, W0, T1, W1, the three release gaps and the prep PR (last), a
  gate without a merged PR, or a PR whose merge is not GitHub's or not an ancestor.

It records the P1-form identity (tree hashes for `/` and 11 directories, 20 pin files), gitlinks,
lockfiles, toolchain (global.json, per-job SDK pins, observed `dotnetSdkBuild` and runtime, the G2
protocol's toolchain and environments, seeds, Z3 pins), 7 producer groups, packets and seals,
readiness, the notes hash, issue states, decisions and open items. A dry run against `e075f848a`
recomputed all four seals equal to their pins; it failed only where expected for a stand-in.

## Classifier

`ReleaseCandidateInvalidation.cs` applies 0.24's rules through a `CandidateProfile` (`V025`).
Default deny; exempt only: this directory and the classifier and manifest test files before the
landing; added data or text files under `reviews/` at any time; the compiler-test count bump in
`eng/test-manifest.json` before the landing; added data or text files under
`docs/plans/evidence/v0.25-regeneration/`, `docs/plans/evidence/adjudication-1430/` and each family's
`ready/` (scope amendment 1.4.0). The landing
is the freeze PR's merge; after it, the profile's own files are frozen (a change returns to #1430).
Any git failure, including an unreadable landing manifest, gives `UNDECIDED`. As in 0.24, it is not
a required gate on later merges, and later runs come from a checkout of the landing.
Controls: `ReleaseCandidateInvalidationTests` (50).

## Freeze blockers: decided (2026-10-19, @juanmicrosoft)

Codex rated 1, 2 and 4 BLOCKING on the prep PR #1575. The maintainer decided them, and the pre-freeze
PR (#1576) implements them, so the freeze is that PR's merge commit. Recorded as scope amendment 1.4.0.

1. **READY write-backs: "Evidence dirs now, scope after A1."** A family's READY record lives under its
   evidence directory's `ready/`, outside the frozen inputs; A1 cites it; `scope.json` and the T1
   mirror are written back after A1. The classifier exempts newly added data files under the six
   family `ready/` directories (and nothing else in those directories).
2. **Gate: "0.25 scope packet."** `scripts/verify_release_adjudication.py` checks format
   `calor.v0.25-scope-packet/1426` for `0.25.x` (`docs/plans/evidence/r2-1410/release-gate.md` §10);
   the map entry names it and stays `not-yet-adjudicated`.
3. **#1545–#1547: closed.** #1577 moved publishing to nuget.org Trusted Publishing, and
   `scripts/check_release_credentials.py --live --attest-workflow-permissions …` reports `CLOSED`
   (Actions workflow permissions by maintainer attestation). #1545, #1546 and #1547 closed on
   2026-10-19 (15:23:15Z, 15:23:17Z, 15:23:19Z). The freeze manifest cites both.
4. **Determinism: "Amend: 2 runs for 0.25."** G2 protocol amendment 1.6.0 (`budget.releaseEpoch`):
   at most 2 executions after run 6, ceiling 1,600 (2 × 795 = 1,590), each on the commit that this
   directory's `candidate-manifest.json` names on protected main.

## Decisions recorded in the prep PR (2026-10-19, @juanmicrosoft)

In `docs/plans/evidence/v0.25-f1-943/README.md`: "Count the 85 against W1" (F1 at 900 of 900) and
"Accept as known residual" (P2V-1, #1573). Neither changes a case, ceiling or exception, so
`scope.json` is not amended.

## Review exception EXC-PREFREEZE-GATE (@juanmicrosoft, 2026-10-19)

The pre-freeze PR (#1576) spent its review budget with two MAJOR findings open (verification pass
NOT-VERIFIED). The maintainer granted, verbatim: **"Exception: fix + 1 pass"**, relayed in the agent
session (no GitHub record). Scope: one fix of those two MAJORs (the gate's S004 case-history replay,
and heading ids for an image inside a link) and the MINOR (the first-of-duplicate-rows control), then
exactly one Codex verification-only pass. That pass also covers the changes made after the budget: the
`[0.25.0]` notes on the #1545–#1547 closures and Trusted Publishing, and the no-auto-gc gate-fixture fix.
A BLOCKING or MAJOR finding in that pass stops the PR for a maintainer decision. Record:
`reviews/prefreeze-exception-codex.md`.

## Reviews

Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), at most 3 rounds plus
1 verification pass per PR: `reviews/prep-*.md`.
