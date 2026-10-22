---
name: create-release
description: >-
    Prepare and hand off a Calor release: bump the version in Directory.Build.props (and the
    website's version pins), write the `## [X.Y.Z]` CHANGELOG section, open and merge the release
    PR, then have the maintainer dispatch publish-nuget.yml from main, first with dry_run: true
    and then for real. The workflow, not this skill, pushes the packages, creates the tag and the
    GitHub release, and dispatches the website. Afterwards, confirm both packages on nuget.org.
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
  passed its check. A release made by hand publishes nothing. `release-audit.yml` runs on every
  release event and fails unless the tag commit is on `main` and nuget.org already serves both
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

## 4. The maintainer dispatches publish-nuget.yml

`publish-nuget.yml` has two inputs, and only `workflow_dispatch` triggers it:

| Input | Type | Meaning |
|---|---|---|
| `version` | string, required | Must equal `<Version>` in `Directory.Build.props` on `main`. It is checked, never used as an override. |
| `dry_run` | boolean, default `false` | Run every check and pack, then stop before the `publish` job. |

There is no `--prerelease` input or flag. The workflow derives it: a `0.x` version or a `-suffix`
version becomes a prerelease.

**First a dry run**, then, if it is green, the real run:

```bash
gh workflow run publish-nuget.yml --ref main -f version=X.Y.Z -f dry_run=true
# after the dry run passes:
gh workflow run publish-nuget.yml --ref main -f version=X.Y.Z
```

`--ref` must be `main`. Any other ref fails in the first step.

### Jobs, in order

1. **`preflight`**: refuses any ref but `main`. Checks out `main` at full depth and runs
   `release_checks.py preflight`. The version must equal `Directory.Build.props`, CHANGELOG must
   hold exactly one non-empty `## [X.Y.Z]` section, and a tag `vX.Y.Z` must be absent or already
   at this commit. Outputs the candidate commit, the tag, and the prerelease flag.
2. In parallel, all on the candidate commit:
   - **`test`**: 13 test projects, one runner each, with the manifest drift check, the
     `check_trx.py` inventory, and the no-Z3-skip guard.
   - **`sdk-consumer`**: the Calor.Sdk consumer check on 5 RIDs (linux-x64, linux-arm64,
     osx-arm64, win-x64, win-arm64).
   - **`release-quality`**: script self-tests, `check_test_quality.py`, sample IDs, coverage,
     mutation, performance and flake ratchets, the round-trip harness, and the scorecard.
   - **`site`**: builds the website, checks it uses only self-hosted fonts, and runs the wording
     scan over `website/out`.
3. **`package`** (no environment, no `id-token`): packs `calor` and `Calor.Sdk`
   (`CalorSdkRequireAllRids=true`). Then it inspects the SDK package on all RIDs, checks the
   packed Z3 against the pins, writes the SBOM and metadata, and renders the notes from
   CHANGELOG. It runs the wording scan over the notes, packages, and metadata, and compares any
   version nuget.org already serves, entry by entry. It uploads the `release-bundle` artifact.
   **A dry run ends here.**
4. **`publish`** (skipped when `dry_run` is true; runs in the `nuget-release` environment, which
   admits only `main`): exchanges the GitHub OIDC token for a one-hour NuGet key
   (`NuGet/login`), and fails if the key is empty. It verifies the bundle against the package
   job's `SHA256SUMS` hash and attests the packages. It repeats the existing-version compare,
   this time always blocking, then pushes both packages. Finally it polls nuget.org (60 attempts,
   30 seconds apart, per package, one package after the other) and compares the served packages
   entry by entry.
5. **`install-check`**: calls `verify-release.yml`. It installs `calor` from nuget.org on
   linux-x64, osx-arm64, and win-x64 (30 attempts, a minute apart), and requires
   `calor verify samples/Verification/proven-contracts.calr` to print `Proven: 14` and
   `Skipped: 0`.
6. **`release`**: verifies the bundle again. The remote tag must be absent or at the candidate.
   Creates the tag and the GitHub release `vX.Y.Z` (title `vX.Y.Z`, body = the rendered notes,
   `--prerelease` when derived). Attaches the release metadata without overwriting.
7. **`website`**: dispatches `nextjs-gh-pages.yml` from `main` with `tag=vX.Y.Z` and
   `commit=<candidate>`. That workflow checks that the tag names that commit, then rebuilds the
   site, rescans it, and deploys it.

Every step before the push blocks the push. After the push, the served-package compare and the
install check block the GitHub release. Inside `release`, the release is created before the
metadata is attached, so a metadata failure leaves the release in place (re-dispatch to finish
it). The `website` job only dispatches `nextjs-gh-pages.yml` and does not wait for it; check that
run separately. The workflow never dispatches benchmark publication.

### What a dry run proves, and what it cannot

A dry run runs `preflight`, `test`, `sdk-consumer`, `release-quality`, `site`, and `package`. It
never enters `nuget-release` and never calls `NuGet/login`, so it cannot test the OIDC exchange or
the Trusted Publishing policy. `publish`, `install-check`, `release`, and `website` are skipped.

In a dry run, two "already released" findings are warnings, not failures: a tag `vX.Y.Z` at
another commit, and a different package already on nuget.org. A dry run of the version already
released therefore still exercises everything else. A real run fails on both.

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
| `test` | `Enforce test inventory` count mismatch | A test count changed without `eng/test-manifest.json`. Fix on main through a PR. |
| `test` | `terminate called after throwing 'std::system_error'` | A native (Z3) crash took down the test host. Re-dispatch once; if it repeats, investigate. |
| any job with `Bootstrap verified Z3 assets` | `download-z3.sh` (or `.ps1`) errors, or the extracted assets fail `.github/z3-binaries-4.15.7.sha256` | The bootstrap downloads the upstream Z3 archives and checks them against `src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256` before extracting. A download error is usually transient: re-dispatch. A checksum mismatch means the upstream bytes changed: stop and investigate. Never update the pins to match unreviewed bytes. |
| `package` | `check-packaged-z3.py` fails | A packed Z3 asset differs from the pins. Investigate the packaging change on main; do not re-dispatch until it is fixed. |
| `site` / `package` | `forbidden wording` | The notes, package text, metadata, or site claims independent verification or adjudication. Reword through a PR. |
| `package` / `publish` | `already released: nuget.org serves ... with content other than the package built here` | nuget.org already has a different package at this version. Bump the version. |
| `publish` | `NuGet/login` fails, or `Trusted Publishing returned no key` | The nuget.org `calor-release` policy or the `nuget-release` environment is wrong. Run `scripts/check_release_credentials.py --live`; settings are a maintainer action. |
| `publish` | bundle verification fails | The artifact does not match the package job's record. Re-dispatch; do not edit the bundle. |
| `publish` | `nuget.org does not serve ... after 60 attempts` | Indexing lag longer than the poll (60 attempts per package). Re-dispatch: the push uses `--skip-duplicate` and the compare accepts an identical package. |
| `install-check` | `could not be installed ... after 30 attempts`, or no `Proven: 14` / `Skipped: 0` | A broken package (for example Z3 missing, `Calor0710`). Investigate before anything else; the GitHub release was not created. |
| `release` | `tag vX.Y.Z already points at ...` / `existing release ... has a body other than these notes` | A tag or release was made by hand. Releases are never edited; the maintainer must resolve it. |

### Re-running

Fix the cause on `main` and dispatch again with the same `version`. Do not delete tags, edit
releases, or bump the version just because a run failed. Prefer a fresh dispatch to
`gh run rerun`: a fresh dispatch takes the current `main`. Once the packages are pushed, a
re-dispatch is safe. The push skips duplicates, every compare accepts an identical package, an
existing release is accepted only when its body equals the notes, and metadata is never
overwritten.

Note that a re-dispatch releases whatever `main` holds. If `main` moved on after a push, the
rebuilt packages usually differ from the ones already on nuget.org, and the existing-version
compare stops the run. Do not merge unrelated changes between a partial run and its
re-dispatch.

## Version calculation

Given `MAJOR.MINOR.PATCH`: increment PATCH; if PATCH > 9, set it to 0 and increment MINOR; if
MINOR > 9, set it to 0 and increment MAJOR. Examples: `0.1.6` → `0.1.7`, `0.1.9` → `0.2.0`,
`0.9.9` → `1.0.0`. The user may choose another version.
