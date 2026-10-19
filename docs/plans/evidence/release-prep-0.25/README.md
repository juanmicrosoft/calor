# Release prep for the 0.25 candidate

Two PRs prepare the release machinery before the 0.25 candidate freeze.

- **PR 1, #1567:** the release gate reads each release's A1 issue, terminal record, contract packet,
  and claim registry from `eng/release-adjudication-map.json`. Design:
  `docs/plans/evidence/r2-1410/release-gate.md` §9. Reviews: `reviews/pr1-codex.md`.
- **PR 2, #1548 (this record):** the release jobs use one exact .NET SDK, the website stops fetching
  Google Fonts at build time, and the create-release skill stops writing benchmark numbers into
  the release notes. Reviews: `reviews/pr2-codex.md`.

## SDK pin

**Gap.** The release jobs asked for `dotnet-version: '10.0.x'`. The byte-reproducibility evidence
(`docs/plans/evidence/deterministic-builds/`) holds for Microsoft SDK 10.0.401 only. On
2026-10-18 `10.0.x` already resolves to 10.0.402.

**Fix.** Every `actions/setup-dotnet` step in the release workflows changes in three ways:

1. It requests an exact version: `10.0.401`. The one second SDK, the 8.0 SDK that
   `publish-nuget.yml` `release-quality` installs for the corpus targets, is pinned to `8.0.426`.
2. It installs into a job-private directory: `DOTNET_INSTALL_DIR: ${{ runner.temp }}/dotnet-release`.
   Without this, installing 10.0.401 is not enough. The repository `global.json` (10.0.100,
   `rollForward: latestMinor`) would select any newer 10.0 SDK already on the runner image.
3. The job's next step, `Require the pinned release SDK (#1548)`, fails unless
   `dotnet --version` is `10.0.401`.

The release workflows covered:

| Workflow | Jobs with an SDK |
|---|---|
| `publish-nuget.yml` | `test`, `sdk-consumer`, `release-quality`, `publish` |
| `verify-release.yml` | `install-and-verify` (3 OS) |
| `reproducible-builds.yml` | `packages` |
| `benchmark.yml` | `benchmark`, `agent-refactoring-benchmark` |
| `nextjs-gh-pages.yml`, `release-audit.yml` | none: they install no .NET SDK |

**Developer builds are unchanged.** The repository `global.json` keeps 10.0.100 with
`latestMinor`. A release `global.json` passed through `global-json-file` would not help on its own:
`dotnet` resolves the SDK from the repository's `global.json`, not from the file setup-dotnet read.
The job-private install directory plus the version check do the pinning without touching it.

**Test.** `ReleaseSdkPinTests` (13 cases) covers the six release workflows:

- No `dotnet-version` may be anything but an exact `N.N.NNN`, and `global-json-file` is not used.
- Every `setup-dotnet` step installs into the private directory.
- The first install in each job is 10.0.401, and any later one is an exact 8.0 SDK.
- The require step comes right after the last install, and no `dotnet` command runs before it.
- `nextjs-gh-pages.yml` and `release-audit.yml` install no SDK.

A local mutation (`'10.0.x'` in `benchmark.yml`) fails 2 of the 13 cases.

## Self-hosted fonts

**Gap.** `website/src/app/layout.tsx` used `next/font/google`. Every website build downloaded CSS
and woff2 files from `fonts.googleapis.com` and `fonts.gstatic.com`. A change on Google's side
could change the site's bytes, and an outage would fail the build.

**Fix.**

- `website/src/app/fonts.ts` uses `next/font/local`. It covers the same families and weights:
  - DM Sans, variable weight 100–1000
  - Raleway, weights 400, 500, 600, and 700
  - JetBrains Mono, variable weight 100–800
  - VT323, weight 400
- `website/src/fonts/<family>/` holds the 16 woff2 files and each family's `OFL.txt` (SIL Open Font
  License 1.1; Raleway carries the Reserved Font Name "Raleway", and the files are unmodified).
- `website/src/fonts/fonts.json` records each file's source URL and SHA-256.
- The files are the exact bytes `next/font/google` served. They were fetched with its user agent,
  and Google serves different files to different user agents. Their SHA-256s equal the 16 media
  files of the last `next/font/google` build.
- `next/font/local` cannot give each file of one family its own `unicode-range`. So each subset is
  its own family, with Google's `unicode-range` copied verbatim. The CSS variable lists the
  subset families latin first. Only the latin files are preloaded, as before.
- `website/src/app/fonts.css` keeps the metric-adjusted Arial fallback faces that the Google loader
  generated, verbatim. `next/font/local` computes slightly different values (for example DM Sans
  `size-adjust` 105.13% against 104.53%). Those values changed how characters outside every
  subset render, such as the arrow `→` (U+2192) in code blocks.

**CI check.** `scripts/check_website_fonts.py` fails closed on any of these:

- `F001`: a source file under `website/` names `next/font/google` or either host.
- `F002`: a vendored file is missing or differs from `fonts.json`.
- `F003`: a family has no `OFL.txt`.
- `F004`: built HTML, CSS, RSC payload, or data names either host.
- `F005`: the built CSS has no `@font-face`, or one whose `src` is not a woff2 in `_next/static/media/`.

Next.js's own runtime chunks contain both host names as constants, for its Google Fonts `<link>`
optimization. So built `.js` is not scanned; `F004` rules out the `<link>` those constants act on.

Where the check runs:

- `website-tests.yml` (both base paths) maps both hosts to `0.0.0.0` and `::` in `/etc/hosts`. It
  confirms the hosts are unreachable, then builds the site and runs the check. This is the proof
  that the build makes no request to Google Fonts.
- `nextjs-gh-pages.yml` runs the check on the site it is about to deploy.
- `reproducible-builds.yml` runs it on its first website build.

**Parity.**

- The 57 Playwright tests pass on the new build.
- `fonts-visual-parity.mjs.txt` serves the old build (main 0b9568b3f, `next/font/google`) and the
  new build side by side, with every non-local request blocked. It covers 8 pages × light/dark ×
  1366/390 px and takes full-page screenshots after `document.fonts.ready`. On each page the
  same number of font faces loads in both builds.
- Result: 30 of 32 screenshots are byte-identical. The other 2 differ in 20–34 pixels, on a
  different page in each run. A control run comparing the old build with itself shows the same
  2-of-32 noise. So the remaining differences are rendering nondeterminism, not the fonts.
- Before the fallback faces were kept verbatim, 12 of 20 screenshots differed. All 12 were glyphs
  that fall back to Arial.

**Website hash.** The reproducible-build check compares two builds of the same commit. The
website tree digest (the gate's `tree_digest`) is per commit by design: it contains the build ID.
This PR changes it. The value from CI is recorded below, with its commit.

| Built tree | Website tree digest (gate `tree_digest`, 253 files) | Run |
|---|---|---|
| PR #1569 head 9bb247ca2 (merge ref b4ce9e2e0), self-hosted fonts | `a87ad9710ca40d4e3d4d6b6e6aeea80e0b06bc368b6e25c9db3139aeedd94711` (build A = build B) | reproducible-builds 39176400008 |

The same run built the packages on SDK 10.0.401 (both builds), 97 entries identical. For
comparison, PR #1568's run on the unpinned workflows (39174767710) resolved 10.0.402.
A later commit has a different digest. The candidate's digest is whatever #1424 records for it.

## create-release skill

`.claude/skills/create-release/SKILL.md` step 2 generated a Markdown benchmark report and told the
agent to write a "Benchmark Results" section into `CHANGELOG.md`. Step 2 now regenerates only the
dashboard JSON. It says release notes carry no benchmark numbers: not in `CHANGELOG.md`, not in
`changelog.mdx`, not in the PR body, and not in the GitHub release. The summary format, the
insertion step, and the temporary-report cleanup are removed.
