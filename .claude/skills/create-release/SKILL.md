---
name: create-release
description: >-
    Prepare and hand off a Calor release: bump the version in Directory.Build.props (and the
    website's version pins), write the `## [X.Y.Z]` CHANGELOG section, open and merge the release
    PR, then have the maintainer dispatch publish-nuget.yml once from main, review its immutable
    candidate summary, and approve promotion in nuget-release. The workflow pushes the validated
    packages, creates the tag and GitHub release, and deploys the validated site. Afterwards,
    confirm both packages on nuget.org.
    Use when the user asks to "cut a release", "create a release", "ship a version",
    "release vX.Y.Z", or "do a release".
allowed-tools: Bash, Read, Write, Edit
user-invocable: true
---

# /create-release - Prepare a Calor release

The release contract (#1548, implemented by #1582): **a release is a dispatch of
`publish-nuget.yml` from `main`**. The dispatch names a version that equals `<Version>` in
`Directory.Build.props`, and `CHANGELOG.md` holds a non-empty `## [X.Y.Z]` section for it. The
packages reach nuget.org through OIDC Trusted Publishing. No adjudication record, identity, or
maintainer override exists any more.

What this skill does, and what it does not:

- It bumps the version, writes the CHANGELOG section, and opens and merges the release PR.
- It does **not** create the tag or the GitHub release, and it never runs `gh release create`.
  `publish-nuget.yml` creates both, after the packages are on nuget.org and the installed tool
  passed its check. The site is deployed from the same immutable bundle. A release made by hand publishes nothing. `release-audit.yml` runs when a
  release is created or published by a person (releases that `publish-nuget.yml` creates with
  `GITHUB_TOKEN` raise no event) and fails unless the tag commit is on `main` and nuget.org already serves both
  packages at that version; it cannot tell who created the release or check its notes.
- The **maintainer** dispatches `publish-nuget.yml`. Prepare the commands and wait; do not
  dispatch it, a dry run included, unless the maintainer explicitly asks you to in this session.

The only release artifacts are the two NuGet packages, `calor` and `Calor.Sdk`. VS Code
extension support was withdrawn after v0.13.1: there is no VSIX and no Marketplace publish.

## 1. Pick the version

Read the current version:

```bash
sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props
```

(Not `grep -oP`: BSD grep on macOS has no `-P`.)

Propose the next one (see [Version calculation](#version-calculation)) and ask the user to confirm
it or name another. The version must match `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH-suffix`, or
preflight rejects it.

## 2. Bump the version and write the CHANGELOG section

| File | Change |
|------|--------|
| `Directory.Build.props` | `<Version>X.Y.Z</Version>`. There must be exactly one `<Version>` element. |
| `CHANGELOG.md` | Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a fresh empty `## [Unreleased]` above it. |
| `website/content/changelog.mdx` | Add the same section at the top, after the `---` separator. Turn doc references into site links (`/docs/...`). |
| `website/package.json`, `website/package-lock.json` | `"version": "X.Y.Z"` (root and workspace entries). |
| `website/src/lib/version.ts` | `SITE_VERSION = 'X.Y.Z'` (the version pill in the site header). |
| `website/src/components/landing/WhatsNewBanner.tsx` | Version string and a one-line headline. Keep the link to `/docs/changelog/`. |
| `website/tests/public-claims.spec.ts` | `currentRelease` and the release date it checks. |

The `## [X.Y.Z]` section of `CHANGELOG.md` **is the release notes**. `publish-nuget.yml` renders it
verbatim (`scripts/render_release_notes.py`) into the GitHub release body. So:

- Follow the public-writing rule in `CLAUDE.md`: write for a CS college student, lead with what
  changed, use numbers rather than adjectives.
- **No benchmark numbers.** No benchmark summary, ratio, interval, or program count in
  `CHANGELOG.md`, `changelog.mdx`, the PR body, or the release (#1548). This skill does not run
  benchmarks; benchmark publication is a separate `benchmark.yml` dispatch and is not part of a
  release.
- No claim that the release or its evidence was "independently verified" or "independently
  adjudicated". The wording scan (`release_checks.py wording`) fails the run on those phrases,
  in the notes, the packages, the release metadata, and the built site, unless they are negated.
- The section must be non-empty and its header must appear exactly once.

Check that no live version spot was missed:

```bash
grep -rn "$OLD_VERSION" website/src website/package.json Directory.Build.props
```

Only historical prose (the changelog, pages describing what shipped earlier) may still name the
old version.

## 3. Open and merge the release PR

```bash
git checkout -b release/vX.Y.Z origin/main
git add -A
git status --short          # review: only the files above
git -c commit.gpgsign=false commit -m "chore(release): X.Y.Z"
git push -u origin release/vX.Y.Z
gh pr create --title "chore(release): X.Y.Z" --body "Version bump and the [X.Y.Z] CHANGELOG section. Publishing is a separate dispatch of publish-nuget.yml from main."
```

Wait for CI to pass, then merge (with the maintainer's approval):

```bash
gh pr merge --squash --delete-branch
git checkout main && git pull
```

Merging publishes nothing. The commit that `main` points at when the maintainer dispatches is the
release candidate. Anything merged after that is not in the release.

## 4. Dispatch once, review the candidate, then approve promotion

Only the maintainer dispatches, and only with explicit authorization in this session:

```bash
gh workflow run publish-nuget.yml --ref main -f version=X.Y.Z
```

`version` must equal `Directory.Build.props`; it never overrides it. The dispatch SHA, not a
later `main` tip, fixes the candidate. A `0.x` or `-suffix` version becomes a prerelease.
**Do not run a dry dispatch followed by a real dispatch.** One workflow now validates, pauses
for environment approval, and promotes that run's exact bytes.

Before using this path, a maintainer must configure `nuget-release` with required reviewers,
prevent self-review, disable admin bypass, and admit only branch `main` (not tags). The workflow
reads and enforces these settings; it never creates or changes them. A 403, missing setting, or
environment without reviewers blocks promotion. `main` must be protected. The nuget.org
Trusted Publishing policy must still name this repository, workflow, and environment.

### Jobs, in order

1. **`preflight`** checks out the dispatch SHA at full depth, verifies the version and non-empty
   CHANGELOG section, and refuses a tag already at another commit. A fresh real dispatch also
   refuses a version for which nuget.org already serves either package: resume the original
   run instead of rebuilding a partially published version. Unknown registry responses fail.
2. **`test`**, **`sdk-consumer`**, **`release-quality`**, and **`site`** run the complete release
   validation once, on that candidate: all 13 test projects and inventory/no-Z3-skip guards,
   5 consumer RIDs, coverage/mutation/performance/100 flake trials, corpus round trips and
   scorecard, and the site's build/font/wording checks. Required PR checks are unchanged; PR
   results are not reused as evidence for a potentially different squash-merge commit.
3. **`package`**, with no publishing credentials, packs both packages, inspects all SDK RIDs,
   verifies Z3 pins, creates SBOM/provenance and notes, scans wording and compares existing
   registry content. It includes the validated site tar, verified against the site job's hash.
   `release-bundle` is a v4 immutable artifact with 90-day retention. The approval summary
   records candidate SHA, version, artifact ID, and archive digest. `SHA256SUMS` binds every file.
4. **`promotion-check`**, also without publishing credentials, verifies trusted repository,
   workflow and same-run identity, all six successful prerequisite results, exact SHA/version,
   protected-main and approval settings, immutable artifact ID/run/SHA/expiry/digest, downloaded
   archive bytes and the complete bundle inventory. Missing, unreadable, expired, wrong or
   tampered input blocks entry to `publish`.
5. **`publish`** waits in `nuget-release`. Review the run summary and successful gates, then
   approve **this run's deployment** in GitHub. After admission, only this job obtains a NuGet
   OIDC key. It rechecks the boundary and contents, attests packages and metadata, repeats the
   blocking existing-version comparison, pushes with `--skip-duplicate`, and polls/compares
   both served packages (60 attempts, 30 seconds apart, per package).
6. **`install-check`** installs from nuget.org on linux-x64, osx-arm64 and win-x64 (30 attempts,
   a minute apart). The candidate sample must produce `Proven: 14` and `Skipped: 0`.
7. **`release`** downloads by immutable artifact ID and checks the recorded inventory again.
   It retains remote tag/candidate and existing release title/body checks; metadata assets must
   be identical or absent, never overwritten.
8. **`website`** verifies the release tag and main ancestry, downloads/checks the same bundle,
   extracts the validated site, repeats cheap font/wording checks, and uploads a Pages artifact.
   **`website-deploy`** deploys it in `github-pages`; its Pages-only OIDC permission cannot
   obtain a NuGet key. There is no second npm build or detached dispatch to wait for.

The full workflow stays pending until approval and reports website deployment completion.
Every pre-push check blocks the push; served-package verification and installation block the
GitHub release. A metadata failure after release creation leaves that release in place:
**rerun failed jobs of the same run**, not a fresh dispatch. Benchmark publication remains a
separate `benchmark.yml` dispatch.

### Diagnostic dry runs

`dry_run=true` remains available for diagnostics only. It stops after packaging, with no
approval or publication. It cannot later be promoted or test OIDC/Trusted Publishing. Existing
tag/content mismatches are warnings only in that diagnostic mode; real promotion fails on them.
For an intended release, use the single default dispatch above and approve it after validation.

## 5. Watch the run and confirm on nuget.org — do not skip

```bash
gh run list --workflow publish-nuget.yml --event workflow_dispatch --limit 5 \
  --json databaseId,status,conclusion,createdAt
gh run watch <run-id>
```

A green run already means nuget.org served both packages, matching entry by entry, and the
installed tool proved the sample. Still check directly, because the flat-container index can lag
by several minutes:

```bash
VER=X.Y.Z
curl -s https://api.nuget.org/v3-flatcontainer/calor/index.json     | jq -r '.versions[-3:][]'
curl -s https://api.nuget.org/v3-flatcontainer/calor.sdk/index.json | jq -r '.versions[-3:][]'
gh release view "v$VER" --json tagName,isPrerelease,targetCommitish
```

Report the release as shipped only after nuget.org lists **both** packages at the new version.
If only one is there, say exactly which.

## Failure modes

| Where | Symptom | Cause and fix |
|---|---|---|
| `preflight` | `publish-nuget runs only from main` | Dispatched from another ref. Re-dispatch with `--ref main`. |
| `preflight` | `Directory.Build.props declares [...]` | The input differs from the merged version, or the PR is not merged yet. |
| `preflight` | `CHANGELOG.md must hold exactly one '## [X.Y.Z]' header` / `section [X.Y.Z] is empty` | Fix CHANGELOG through a PR, then re-dispatch. |
| `preflight` (real run) | `already released: tag vX.Y.Z already points at ...` | The version was released from another commit. Bump the version; never move a tag. |
| `preflight` (real run) | `rerun failed jobs of the original run` | Either package is already served. Resume that original run; do not rebuild this version. |
| `test` | `Enforce test inventory` count mismatch | A test count changed without `eng/test-manifest.json`. Fix on main through a PR. |
| `test` | `terminate called after throwing 'std::system_error'` | A native (Z3) crash took down the test host. Re-dispatch once; if it repeats, investigate. |
| any job with `Bootstrap verified Z3 assets` | `download-z3.sh` (or `.ps1`) errors, or the extracted assets fail `.github/z3-binaries-4.15.7.sha256` | The bootstrap downloads the upstream Z3 archives and checks them against `src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256` before extracting. A download error is usually transient: re-dispatch. A checksum mismatch means the upstream bytes changed: stop and investigate. Never update the pins to match unreviewed bytes. |
| `package` | `check-packaged-z3.py` fails | A packed Z3 asset differs from the pins. Investigate the packaging change on main; do not re-dispatch until it is fixed. |
| `site` / `package` | `forbidden wording` | The notes, package text, metadata, or site claims independent verification or adjudication. Reword through a PR. |
| `package` / `publish` | `already released: nuget.org serves ... with content other than the package built here` | nuget.org already has a different package at this version. Bump the version. |
| `publish` | `NuGet/login` fails, or `Trusted Publishing returned no key` | The nuget.org `calor-release` policy or the `nuget-release` environment is wrong. Run `scripts/check_release_credentials.py --live`; settings are a maintainer action. |
| `promotion-check` / `publish` | promotion refused | Inspect identity, artifact expiry/digest, gate results and environment approval controls. Do not edit the bundle or bypass the check. |
| `publish` | `nuget.org does not serve ... after 60 attempts` | Indexing lag longer than the poll (60 attempts per package). Rerun failed jobs of the same run: the push uses `--skip-duplicate` and the compare accepts an identical package. |
| `install-check` | `could not be installed ... after 30 attempts`, or no `Proven: 14` / `Skipped: 0` | A broken package (for example Z3 missing, `Calor0710`). Investigate before anything else; the GitHub release was not created. |
| `release` | `tag vX.Y.Z already points at ...` / `existing release ... has a body other than these notes` | A tag or release was made by hand. Releases are never edited; the maintainer must resolve it. |

### Re-running

After a partial publication, use **Re-run failed jobs** (`gh run rerun RUN_ID --failed`, only
when authorized), retaining the original run ID, candidate, successful gate outputs, artifact ID
and exact bytes even if `main` advances. Do not rerun all jobs: immutable artifact uploads refuse
replacement. The environment may require approval again. Existing-version comparisons, duplicate
pushes, tag/release checks and identical-asset checks make a same-candidate retry idempotent.

A changed candidate requires fresh validation and a fresh dispatch. Never delete tags, edit
releases, or silently rebuild a partially published version. If the artifact is deleted or
expires, promotion fails closed. Recover the original verified artifact through a separately
reviewed maintainer procedure, or use a new version; this workflow offers no rebuild fallback.
GitHub permits an approval wait for at most 30 days and workflow reruns for a limited period
(currently 30 days); 90-day artifact retention is not a promise of indefinite retry support.

See [Release Safety Policy](../../../docs/release-policy.md#immutable-candidate-promotion)
for the trust boundary, intentional duplicate checks and measured baseline versus estimates.

## Version calculation

Given `MAJOR.MINOR.PATCH`: increment PATCH; if PATCH > 9, set it to 0 and increment MINOR; if
MINOR > 9, set it to 0 and increment MAJOR. Examples: `0.1.6` → `0.1.7`, `0.1.9` → `0.2.0`,
`0.9.9` → `1.0.0`. The user may choose another version.
