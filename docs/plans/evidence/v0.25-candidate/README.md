# 0.25 release candidate: freeze machinery (#1430)

The 0.25 form of 0.24's C1 (#1423, `docs/plans/v0.24-c1-candidate.md`). Scope amendment 1.3.0
(`acceptancePreconditions.reproof`, check `provenance-candidate-manifest`) requires "a candidate
manifest in the 0.24 C1 form ... frozen for the 0.25 candidate SHA". 0.24's generator and classifier
are tied to 0.24 (contract 1.5.0, S2 closure, G3 execution 2, `c1-1423/` paths), and editing them
would invalidate the frozen 0.24 record, so 0.25 has its own. Nothing here publishes, tags or releases.

**Two PRs.** (1) Release prep: version 0.25.0, the byte-identical `[0.25.0]` notes in `CHANGELOG.md`
and `website/content/changelog.mdx`, the generator and the classifier; the candidate is its merge
commit. (2) Freeze: `inputs.json` (hand-recorded candidate, merge commits, issue states, observed
toolchain, decisions, open items), the generated `candidate-manifest.json`, and
`V025CandidateManifestTests.cs`, which re-verifies the manifest from git, checks the identity
through P1's `DurableProvenance.Verify`, and runs the scope, T1 and D1 validators on the
candidate's blobs.

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
`docs/plans/evidence/v0.25-regeneration/` and `docs/plans/evidence/adjudication-1430/`. The landing
is the freeze PR's merge; after it, the profile's own files are frozen (a change returns to #1430).
Any git failure, including an unreadable landing manifest, gives `UNDECIDED`. As in 0.24, it is not
a required gate on later merges, and later runs come from a checkout of the landing.
Controls: `ReleaseCandidateInvalidationTests` (44).

## Open questions for the maintainer

Codex rated 1 and 2 (round 1) and 4 (round 2) BLOCKING for the freeze: without answers, no candidate
frozen at this PR's merge can be accepted and published. They need maintainer decisions; if one
needs a code or protocol change, the freeze moves to the merge of that change.

1. **READY write-backs.** READY is recorded only in `scope.json` (`S007`), mirrored by the T1
   register; both are frozen inputs, so a READY amendment after the freeze invalidates the
   candidate. Either READY evidence lives downstream and `scope.json` is written back after A1, or
   the classifier gets a narrow readiness-only exemption.
2. **The 0.25 contract at the gate.** The gate cannot check the scope packet format (`G016`), and
   `publish-nuget.yml` runs the gate from the candidate checkout, so the candidate itself must
   contain a gate that reads the 0.25 contract. Which contract governs 0.25 is the open #1548
   decision.
3. **#1545 settings.** On 2026-10-19 `scripts/check_release_credentials.py --live` reported `OPEN`
   (repository-level `NUGET_API_KEY` present, no `nuget-release` environment).
4. **No G2 execution left.** The re-proof runs the 0.24 G2 protocol (#1421) on this candidate.
   Protocol 1.5.0 allows 4 executions, `c2-1424/regeneration-3/ledger.json` records 4 of 4, and
   `determinism_runner.plan_problems()` binds later executions to #1537's 0.24 merge. A 0.25
   protocol amendment (allowance and candidate binding) needs approval first.

## Decisions recorded in the prep PR (2026-10-19, @juanmicrosoft)

In `docs/plans/evidence/v0.25-f1-943/README.md`: "Count the 85 against W1" (F1 at 900 of 900) and
"Accept as known residual" (P2V-1, #1573). Neither changes a case, ceiling or exception, so
`scope.json` is not amended.

## Reviews

Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), at most 3 rounds plus
1 verification pass per PR: `reviews/prep-*.md`.
