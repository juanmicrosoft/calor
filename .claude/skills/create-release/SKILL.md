---
name: create-release
description: >-
    Automate the Calor release process end-to-end: bump version across Directory.Build.props,
    VSCode extension, website, and changelog files; run the statistical benchmark suite
    (30 runs); open and merge a release PR; create the GitHub release with proper pre-release
    tagging; and trigger the website deploy. Use when the user asks to "cut a release",
    "create a release", "ship a version", "release vX.Y.Z", or "do a release".
allowed-tools: Bash, Read, Write, Edit
user-invocable: true
---

# /create-release - Create a New Calor Release

This skill automates the release process: bump versions across all components, run benchmarks, create a PR, merge it, and create a GitHub release with proper tagging.

## Steps to Perform

### 1. Determine the Next Version

Read the current version from `Directory.Build.props`:

```bash
grep -oP '(?<=<Version>)[^<]+' Directory.Build.props
```

Calculate the next version using patch increment logic:
- Patch increment: `0.1.6` → `0.1.7` → ... → `0.1.9`
- Minor rollover: `0.1.9` → `0.2.0`
- Continue pattern until `0.9.9`
- Major rollover: `0.9.9` → `1.0.0`

Ask the user to confirm the next version or allow them to specify a different one.

### 2. Run Benchmarks

Run the benchmark suite in statistical mode (30 runs with confidence intervals) to generate results for the release:

1. Generate markdown report for release notes:
   ```bash
   dotnet run --project tests/Calor.Evaluation -c Release -- run --format markdown --output benchmark-results.md --statistical --runs 30
   ```

2. Generate website JSON for the dashboard:
   ```bash
   dotnet run --project tests/Calor.Evaluation -c Release -- run --format website --output website/public/data/benchmark-results.json --statistical --runs 30
   ```

3. Read `benchmark-results.md` and extract a summary for the CHANGELOG. The summary should include:
   - Overall advantage score
   - Win counts (Calor vs C#)
   - Key metric highlights with confidence intervals
   - Number of programs tested

4. Format the benchmark summary for CHANGELOG (see format below).

**Note**: Statistical benchmark runs (30 iterations) add approximately 5-10 minutes to the release process but provide confidence intervals for more rigorous results.

### 3. Update Version Files

Update these files with the new version:

| File | What to Update |
|------|----------------|
| `Directory.Build.props` | `<Version>X.Y.Z</Version>` |
| `editors/vscode/package.json` | `"version": "X.Y.Z"` |
| `website/package.json` | `"version": "X.Y.Z"` |
| `CHANGELOG.md` | Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add benchmark summary |
| `website/content/changelog.mdx` | Add new version section at the top (same content as CHANGELOG.md but MDX format, no benchmark stats) |
| `website/src/components/landing/WhatsNewBanner.tsx` | Update version number and one-line description of the release |

When updating CHANGELOG.md:
1. Find the line `## [Unreleased]`
2. Replace it with `## [X.Y.Z] - YYYY-MM-DD` where YYYY-MM-DD is today's date
3. Add a new `## [Unreleased]` section above the new version header
4. Insert the benchmark summary section immediately after the version header, before any existing changes

When updating website/content/changelog.mdx:
1. Add a new `## [X.Y.Z] - YYYY-MM-DD` section after the `---` separator at the top (before the previous version)
2. Copy the **Added**, **Fixed**, **Removed** sections from CHANGELOG.md (skip benchmark stats — those are in CHANGELOG.md only)
3. Convert any doc references to website links (e.g., `[page title](/docs/path/)`)

When updating WhatsNewBanner.tsx:
1. Update the version string (e.g., `v0.4.8`)
2. Update the one-line description to highlight the most notable feature in this release
3. Keep the "See what's new" link pointing to `/docs/changelog/`

**Benchmark Summary Format for CHANGELOG:**

```markdown
## [X.Y.Z] - YYYY-MM-DD

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: X.XX (Calor/C# leads)
- **Metrics**: Calor wins N, C# wins M
- **Highlights**:
  - MetricName1: X.XXx ± 0.XX (winner)
  - MetricName2: X.XXx ± 0.XX (winner)
  - MetricName3: X.XXx ± 0.XX (winner)
- **Programs Tested**: NN

### Changes
[existing changelog content under ## [Unreleased]]
```

Extract the key metrics from the benchmark markdown output's Executive Summary section to populate the highlights.

### 4. Create Release Branch and PR

```bash
git checkout -b release/vX.Y.Z
git add Directory.Build.props editors/vscode/package.json website/package.json CHANGELOG.md website/public/data/benchmark-results.json website/content/changelog.mdx website/src/components/landing/WhatsNewBanner.tsx
git commit -m "chore: bump version to X.Y.Z"
git push -u origin release/vX.Y.Z
```

Create the PR:

```bash
gh pr create --title "Release vX.Y.Z" --body "$(cat <<'EOF'
## Summary
- Bump version to X.Y.Z
- Update CHANGELOG.md with release date and benchmark results
- Update website changelog and WhatsNewBanner
- Update benchmark results JSON for website dashboard

## Benchmark Results
[Include brief benchmark summary here]

## Checklist
- [ ] Version updated in Directory.Build.props
- [ ] Version updated in editors/vscode/package.json
- [ ] Version updated in website/package.json
- [ ] CHANGELOG.md updated with version, date, and benchmark summary
- [ ] website/content/changelog.mdx updated with version and changes (no benchmark stats)
- [ ] website/src/components/landing/WhatsNewBanner.tsx updated with version and headline
- [ ] website/public/data/benchmark-results.json updated with latest results
EOF
)"
```

### 5. Merge the PR

Wait for any CI checks, then merge:

```bash
gh pr merge --squash --delete-branch
```

### 6. Create GitHub Release

First, extract the changelog content for this version from CHANGELOG.md. The content is between `## [X.Y.Z]` and the next `## [` line. This will include the benchmark results section.

Determine if this is a pre-release (any version < 1.0.0 is pre-release).

Write the extracted notes to a file and pass `--notes-file`; the notes contain backticks and
`$`, which a shell-quoted `--notes` string will mangle.

Create the release:

```bash
awk '/^## \[X\.Y\.Z\]/{f=1} /^## \[/{if(f && !/X\.Y\.Z/) exit} f' CHANGELOG.md > /tmp/notes.md

# For pre-release (version < 1.0.0):
gh release create vX.Y.Z --title "vX.Y.Z" --notes-file /tmp/notes.md --prerelease

# For stable release (version >= 1.0.0):
gh release create vX.Y.Z --title "vX.Y.Z" --notes-file /tmp/notes.md
```

**If you pass `--target`, it must be a FULL 40-character SHA.** A short SHA is rejected with

```
tag_name is not a valid tag
Publishing releases must have a valid tag
Release.target_commitish is invalid
```

which points at the tag and not at the real problem. Use `--target "$(git rev-parse main)"`, or
omit `--target` entirely to release from the default branch's current head.

### 7. Cleanup and Return to Main Branch

```bash
git checkout main
git pull
```

Remove the temporary benchmark markdown file:

```bash
rm -f benchmark-results.md
```

Trigger the website deploy (the `nextjs-gh-pages` workflow runs on release creation, but if the banner/changelog were updated after the release tag was created, trigger a manual deploy):

```bash
gh workflow run nextjs-gh-pages.yml
```

### 8. Verify the packages actually published — DO NOT SKIP

**Creating the release triggers `publish-nuget` and `publish-vscode`; it does not make them
succeed.** Both have failed silently while the tag and the website went out normally, so the
release *looks* complete. The VS Code extension failed for three consecutive releases (v0.9.0,
v0.10.0, v0.12.0) before anyone noticed, and the marketplace sat at `0.3.8` the whole time.

A green workflow list is not sufficient evidence either — check the registries themselves:

```bash
gh run list --event release --limit 4 \
  --json workflowName,status,conclusion \
  --jq '.[] | "\(.workflowName)\t\(.status)\t\(.conclusion)"'

# nuget.org — must list the version you just cut
curl -s https://api.nuget.org/v3-flatcontainer/calor/index.json \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['versions'][-3:])"
curl -s https://api.nuget.org/v3-flatcontainer/calor.sdk/index.json \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['versions'][-3:])"

# VS Code marketplace — must report the version you just cut
curl -s -X POST "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json;api-version=7.2-preview.1" \
  -d '{"filters":[{"criteria":[{"filterType":7,"value":"calor-dev.calor"}]}],"flags":914}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['results'][0]['extensions'][0]['versions'][0]['version'])"
```

Strongest check for NuGet — install the published tool and exercise Z3, which is the part most
likely to be broken by a packaging change:

```bash
dotnet tool install --tool-path /tmp/calorcheck calor --version X.Y.Z
/tmp/calorcheck/calor --version
/tmp/calorcheck/calor verify <some.calr>   # expect real Proven counts, not "Z3 not available"
```

Known failure modes, so they are recognised rather than re-diagnosed:

| Symptom | Cause | Who can fix |
|---|---|---|
| `sha256sum: WARNING: N computed checksums did NOT match` | The pinned `z3-binaries` release drifted | Republish via `build-z3.yml` (`workflow_dispatch`) and commit the manifest it prints. **Never rehash the live assets.** |
| `error IL3000` during the VSIX build | `Assembly.Location` under single-file publish | Code fix. `test.yml`'s `vsix-single-file-publish` job should have caught this on the PR. |
| `Access Denied: The Personal Access Token used has expired` | `VSCE_PAT` secret expired | **Maintainer only** — mint at https://aka.ms/vscodepat, update the secret, then re-run just the `publish` job. The built VSIXes are already uploaded as artifacts. |

Report the release as shipped only after the registries confirm it.

## Version Calculation Logic

Given version `MAJOR.MINOR.PATCH`:

1. Increment PATCH by 1
2. If PATCH > 9, set PATCH = 0 and increment MINOR
3. If MINOR > 9, set MINOR = 0 and increment MAJOR

Examples:
- `0.1.6` → `0.1.7`
- `0.1.9` → `0.2.0`
- `0.9.9` → `1.0.0`
- `1.0.0` → `1.0.1`

## Pre-release Flag

- Version < 1.0.0: Always use `--prerelease` flag
- Version >= 1.0.0: Do not use `--prerelease` flag (stable release)
