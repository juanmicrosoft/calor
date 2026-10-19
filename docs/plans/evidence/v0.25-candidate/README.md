# 0.25 release candidate: freeze machinery (#1430)

This directory is the 0.25 form of 0.24's C1 (#1423, `docs/plans/v0.24-c1-candidate.md`). Scope
amendment 1.3.0 (`acceptancePreconditions.reproof`, check `provenance-candidate-manifest`) requires
"a candidate manifest in the 0.24 C1 form ... frozen for the 0.25 candidate SHA". 0.24's generator
and classifier are tied to 0.24 (contract 1.5.0, S2 closure, G3 execution 2, the `c1-1423/` paths),
and editing them now would invalidate the frozen 0.24 record. So 0.25 gets its own copies here.
Nothing in this directory publishes, tags or releases anything.

## Two PRs

1. **Release prep (this record's first PR).** Version 0.25.0, the `[0.25.0]` notes in
   `CHANGELOG.md` and `website/content/changelog.mdx` (byte-identical), the generator and the
   classifier below. The candidate is this PR's merge commit, so it contains both.
2. **Freeze.** Adds `inputs.json` (hand-recorded: candidate, merge commits, issue states, observed
   toolchain, decisions, open items), the generated `candidate-manifest.json`, and
   `V025CandidateManifestTests.cs`, which re-verifies the manifest from git, checks the identity
   through P1's `DurableProvenance.Verify` (as `CandidateManifestTests` does for 0.24), and runs the
   0.25 validators (scope `S001`-`S014`, T1 register, D1 seal) against the candidate's blobs.

## Generator

`generate_candidate_manifest.py` reads only git objects at the candidate. `--check` regenerates and
compares. `--self-test` runs the 13 tag-rule controls copied from 0.24 plus serializer and SDK-pin
controls; every invocation runs them. It fails closed on:

- a shallow clone, unfetched `origin/main`, a candidate that is not GitHub's merge of the prep PR or
  not on main's first-parent chain;
- a declared version other than the decided one, or a `v<version>` tag (local or remote, peeled)
  that is not the candidate;
- `[<version>]` sections of `CHANGELOG.md` and `changelog.mdx` that differ, or that claim
  independent review; an adjudication-map entry for `0.25.x` that is not `not-yet-adjudicated`;
- a release workflow job whose first `setup-dotnet` install is not the release SDK (10.0.401), whose
  later installs are anything but the registered auxiliary SDK (8.0.426), or that uses
  `global-json-file` (the rule of `ReleaseSdkPinTests`);
- the scope packet not `FROZEN`/`MET`, its `evidenceSeal` not recomputing (baseline results,
  producers, `generated/`), or `acceptancePreconditions` without the own-SHA re-proof and the three
  release gaps;
- the T1 register consuming another scope version, any approved evidence, fixture or results hash
  differing, or its approval seal not recomputing; W1 records consuming another scope version;
- a packet seal that differs from the seal pinned in the candidate's own tests. The scope seal
  (`InteropScopeTests.FrozenSeal`), the D1 seal (`RefOutContractTests.FrozenSeal`) and the T1
  baseline and amendment seals (`T1RegisterTests`) are recomputed with System.Text.Json's
  serialization (`dotnet_json`). Three controls on the real packets check that emptying the scope
  denominator or the D1 rules moves its seal, and that a D1 case `status` (unsealed) does not;
- a 0.24 S2 repair or regression witness missing at the candidate (they are not run by the freeze;
  the re-proof runs them on this SHA);
- a gate set other than the required one (R0, D1, F1-F6, W0, T1, W1, the three release gaps, and
  the prep PR as the candidate), a gate with no merged PR, or a PR whose merge is not GitHub's merge
  commit or not an ancestor of the candidate.

It records the durable identity in P1's form (`status`, `commit`, `resolvedVia`, tree hashes for `/`
and 11 top-level directories, SHA-256 of 20 pin files), the 3 corpus gitlinks, every lockfile, the
website npm lockfile, `global.json`, the per-job release SDK pins, the observed `dotnetSdkBuild`
fingerprint and runtime, the G2 determinism protocol's toolchain and environments, seeds, the Z3
pins, the hashes of 7 producer groups, the packets with their seals, family readiness, the notes
section hash, issue states, decisions and open items.

A dry run against `e075f848a` (the #1571 merge, standing in for a candidate) recomputed all four
seals equal to their pins; it failed only where expected (that commit declares 0.24.0, which is
tagged elsewhere, and the stand-in inputs named no gates).

## Classifier

`tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs` applies 0.24's rules
through a `CandidateProfile`; `V025` is the 0.25 profile. Default deny. Exempt only:

| Path | When |
|---|---|
| this directory, `ReleaseCandidateInvalidation*.cs`, `V025CandidateManifestTests.cs` | before the landing |
| `docs/plans/evidence/v0.25-candidate/reviews/**` (added data and text files) | always |
| `eng/test-manifest.json`, compiler-test count rise with an appended note only | before the landing |
| `docs/plans/evidence/v0.25-regeneration/**`, `docs/plans/evidence/adjudication-1430/**` (added data and text files) | always |

The landing is the freeze PR's merge (the first first-parent commit of `origin/main` holding the
manifest). After it, the profile's own files are frozen, and changing them returns to #1430. As in
0.24, the classifier is not a required gate on later merges, and a later run must come from a
checkout of the landing commit. A history git cannot read (a missing object on the target's side or
on protected main's side) gives `UNDECIDED`. Controls: `ReleaseCandidateInvalidationTests` (43).

## Open questions for the maintainer

Codex round 1 rated questions 1 and 2 BLOCKING for the freeze: without answers, no candidate frozen
at this PR's merge can be accepted and published. They need maintainer decisions, so this PR does
not resolve them. If either needs a code change, the freeze moves to the merge of that change.

1. **READY write-backs.** Scope §10 records READY only in `scope.json` (`S007`), with the T1
   register mirroring it. Both are frozen inputs, so a READY amendment after the freeze invalidates
   this candidate under default deny. Either READY evidence lives in the downstream directories
   above and `scope.json` is written back after A1, or the classifier gets a narrow exemption for
   readiness-only amendments.
2. **The 0.25 contract format at the gate.** The `0.25.x` map entry names the scope packet as its
   contract, and the gate does not check that format (`G016`). `publish-nuget.yml` runs the gate
   script from the candidate checkout, so the candidate itself must contain a gate that reads the
   0.25 contract. Which contract governs the 0.25 release is the #1548 decision, still open.
3. **#1545 repository settings.** On 2026-10-19 `scripts/check_release_credentials.py --live`
   reported `OPEN`: the repository-level `NUGET_API_KEY` exists and the `nuget-release`
   environment does not.

## Maintainer decisions recorded in the prep PR (2026-10-19, @juanmicrosoft)

Both are recorded in `docs/plans/evidence/v0.25-f1-943/README.md`, "Maintainer decisions after
PR 2": "Count the 85 against W1" (F1 at 900 of 900) and "Accept as known residual" (P2V-1, #1573).
Neither changes a registered case, ceiling or exception, so `scope.json` is not amended.

## Reviews

Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), at most 3 rounds plus
1 verification pass per PR. Records: `reviews/prep-*.md`.
