# 0.25: closing the release-gate gaps 0.24 left open (#1546, #1547)

**Maintainer decision (2026-10-18):** 0.25 re-proves 0.24's checks on its own candidate, and the
0.24 gaps (#1545–#1547) close before 0.25's acceptance. The goal is that 0.25 releases through the
#1410 gate (`scripts/verify_release_adjudication.py`), not through an override.

This PR covers #1546 and #1547. #1545 and the override part of #1548 are on branch
`milestone-0.25/release-gaps-1545-1548`, which stopped at its review budget
(`reviews/pr-a-*` on that branch). Reviews for this PR: `reviews/pr-b-*`.

## #1546: the release PR could not consume an identity

**Gap.** The release PR bumps the version and writes the notes before the candidate is frozen, so
it can never carry the adjudication identity. 0.24 inventoried it as a release surface, and
condition 7 failed on it (AD-1).

**Options considered.**

1. *Freeze the candidate first and generate the release PR from it.* This is unsound. The
   candidate must already declare the version (`G009`) and hold the notes the record hashes, so a
   PR generated afterwards changes the candidate.
2. *Make the release PR a metadata step bound to the identity.* It still precedes the identity, so
   the binding could only be checked afterwards. That is option 3 with extra machinery.
3. *Drop the release PR as a publication surface (chosen).* It publishes nothing: no package, tag,
   release, or deploy. Its only products are candidate bytes, and the identity names the candidate.

**Design (PR B).**

- `verify_release_adjudication.py` checks the release PR's products on **every** run, for every
  surface, whether or not `--release-notes` is passed.
  - The version is checked by `G009`, as before.
  - New: the gate renders the candidate's `CHANGELOG.md` `## [<version>]` section with publish-nuget's
    rule. The section must hash to `publication.release-notes.sha256` and pass the wording scan.
    A missing or different section is `G014`; forbidden wording is `G012`.
  - A release PR whose notes differ from what #1408 adjudicated therefore blocks every surface.
- `release-gate.md` §3 (the release-PR row) and §6 record the decision.
- The `create-release` skill now calls this PR the candidate-preparation PR. Its body template
  restates no notes and no benchmark numbers.
- The 0.24 contract is frozen and is not amended. The 0.25 contract must adopt the same
  classification; that is the maintainer's call.
- Tests: `CandidateChangelogThatIsNotTheAdjudicatedNotesFails` (2),
  `CandidateWithoutTheVersionsChangelogSectionFails` (2), and
  `IndependentClaimInTheCandidateChangelogFailsWithoutAnySurfaceOption`. All of them pass no surface
  option.

## #1547: benchmark results were published on the release path

**Gap.** `publish-nuget.yml` dispatched `benchmark.yml` after every gated release. That workflow
pushed the B2 headline to a public branch and opened a PR. So "no benchmark results are published"
was false for 0.24 (AD-3).

**Design (PR B).** Benchmark publication is now a separate, explicitly approved action, with two
independent locks.

1. **Dispatch.**
   - `publish-nuget.yml` no longer dispatches `benchmark.yml` in either mode; it dispatches only
     the website.
   - `benchmark.yml` gains `publish_results` (boolean, default `false`). The publishing step runs
     only on a dispatch with `publish_results == 'true'`. Every other dispatch, and every push run,
     measures and uploads run artifacts only.
2. **Record.** With `--benchmark-worktree`, the gate also requires
   `publication.benchmark-results.approved` to be the JSON literal `true` (`G015`). Adjudicating
   the hashes is not, by itself, approval to publish them. A record without approval still passes
   every other surface.

Tests:

- `BenchmarkPublicationWithoutTheRecordsApprovalFails` (3 cases: missing, `false`, `"true"`);
- `BenchmarkPublicationIsASeparateExplicitDispatch`;
- `ReleasePathKeepsTheTestGatesAndNeverDispatchesBenchmarkPublication`, which also checks that no
  workflow runs `gh workflow run benchmark…`;
- `scripts/test_benchmark_publication_gate.py`, which now allows only the narrowing
  `publish_results` input and checks the step condition.

**Scope: workflows on `main` only.** Workflow files on older refs keep their own publication
logic. For example, `v0.22.0`'s `benchmark.yml` is dispatchable, declares `contents: write`, and
opens a PR with `peter-evans/create-pull-request`. Both run with the write-capable
`GITHUB_TOKEN`, and #1545's NuGet credential isolation does not restrict them. Closing that path
takes repository settings, which the maintainer owns:

- *Settings → Actions → General → Workflow permissions*: set **Read repository contents and
  packages permissions**, and turn **off** *Allow GitHub Actions to create and approve pull
  requests*. Older workflows that declare `contents: write` still get it, but none can open a PR.
  The gated publishing step on `main` would then push its verified branch, and the maintainer
  opens the PR by hand, which suits a separately approved action.
- A branch ruleset on `benchmark-results*` that restricts creation, so an older workflow cannot
  push that branch.

Until then, the guarantee is: **no workflow on `main` publishes benchmark results without both
locks.**

The renderer also changed in round 1. publish-nuget used to render the notes with `awk`, which
matched the version as a regular expression. It now uses `scripts/render_release_notes.py`,
which is the gate's own literal `changelog_section`. A version header that appears twice fails
(`G014`, and the renderer exits 1).

**Wording that stays true.** "The release does not publish benchmark results." Run artifacts are
the exception: every `benchmark.yml` run uploads its measurements as workflow artifacts. Anyone
signed in to GitHub can download artifacts on a public repository. Those artifacts are diagnostic
and not a publication surface, but release notes must not claim that no benchmark numbers are
visible anywhere.
