---
name: create-release
description: >-
    Automate the Calor release process end-to-end: bump version across Directory.Build.props,
    website, and changelog files; run the statistical benchmark suite
    (30 runs); open and merge a release PR; create the GitHub release with proper pre-release
    tagging; trigger the website deploy; and verify both packages reached nuget.org.
    Use when the user asks to "cut a release",
    "create a release", "ship a version", "release vX.Y.Z", or "do a release".
allowed-tools: Bash, Read, Write, Edit
user-invocable: true
---

# /create-release - Create a New Calor Release

This skill automates the release process: bump versions across all components, run benchmarks, create a PR, merge it, create a GitHub release with proper tagging, and **confirm the NuGet packages actually published**. Step 8 is not optional — creating the release only *triggers* the workflow, and in this repo it has failed silently for long stretches.

**VS Code extension support was withdrawn after v0.13.1.** There is no VSIX build, no Marketplace
publish, and no `editors/vscode` tree. Any step below that mentions them describes a channel that
no longer exists — the only release artifacts are the two NuGet packages.

## Steps to Perform

### 1. Determine the Next Version

Read the current version from `Directory.Build.props`:

```bash
sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props
```

(Not `grep -oP` — BSD grep on macOS has no `-P` and exits 2.)

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
| `website/package.json` | `"version": "X.Y.Z"` |
| `website/src/lib/version.ts` | `SITE_VERSION = 'X.Y.Z'` — this is the pill next to "calor" in the site header; if you skip it, the header keeps showing the previous version even after CHANGELOG and banner update |
| `CHANGELOG.md` | Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add benchmark summary |
| `website/content/changelog.mdx` | Add new version section at the top (same content as CHANGELOG.md but MDX format, no benchmark stats) |
| `website/src/components/landing/WhatsNewBanner.tsx` | Update version number and one-line description of the release |

**How to verify you got all the version spots**: after editing, run
`grep -rn "$OLD_VERSION" website/src website/package.json Directory.Build.props`
— the only remaining match should be `changelog.mdx` and prose references in
`website/content/**` describing what shipped in that version (those are historical
and stay). Any live component or config still pointing at the old version means
you missed it.

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
git add Directory.Build.props website/package.json CHANGELOG.md website/public/data/benchmark-results.json website/content/changelog.mdx website/src/components/landing/WhatsNewBanner.tsx
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

**First sync local `main`** — step 5 merged the PR on the remote, and `gh pr merge --delete-branch`
makes no promise of fast-forwarding your local branch:

```bash
git checkout main && git pull --ff-only
sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props   # MUST show the new version
```

Do not skip that check. If you tag a stale `main`, the tag lands on the **pre-bump** commit,
`publish-nuget` checks out the tag, `Directory.Build.props` still holds the previous version, and
`dotnet nuget push --skip-duplicate` silently no-ops — a release that reports success and ships
nothing. Recovering means deleting the release and tag and re-cutting.

Extract the notes. Write them to a file and pass `--notes-file`; the notes contain backticks and
`$`, which a shell-quoted `--notes` string will mangle. **Replace `0.12.1` below with the version
you are cutting**, and then check the result is non-empty — a version string that matches nothing
yields a silent empty file and a release with an empty body:

```bash
VER=0.12.1                                    # <-- the version being released
awk -v v="$VER" '$0 ~ "^## \\[" v "\\]" {f=1; print; next} f && /^## \[/ {exit} f' \
    CHANGELOG.md > /tmp/notes.md
test -s /tmp/notes.md || { echo "ERROR: no CHANGELOG section for $VER"; exit 1; }
head -1 /tmp/notes.md                          # sanity: should be "## [$VER] - YYYY-MM-DD"
```

Create the release (omit `--target` — it defaults to the default branch's head, which you have
just verified):

```bash
# For pre-release (version < 1.0.0):
gh release create "v$VER" --title "v$VER" --notes-file /tmp/notes.md --prerelease

# For stable release (version >= 1.0.0):
gh release create "v$VER" --title "v$VER" --notes-file /tmp/notes.md
```

**If you do pass `--target`, it must be a FULL 40-character SHA** — use
`--target "$(git rev-parse origin/main)"` after a `git fetch origin main`. A short SHA is rejected
with a message that points at the tag rather than the target, and is easy to misread:

```
tag_name is not a valid tag
Release.target_commitish is invalid
```

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

### 8. Verify NuGet — DO NOT SKIP

**Creating the release triggers `publish-nuget`; it does not make it succeed.** A tag and website
can go live while the packages are absent, so the release can look complete when it is not.

**Wait for the publish workflow first.** Querying nuget.org before it finishes shows the previous
version and looks like failure.

```bash
VER=0.12.1   # the version you cut

# Watch the publish workflow to completion (get the run id for THIS tag)
gh run list --event release --limit 10 \
  --json databaseId,workflowName,headBranch,status,conclusion \
  --jq ".[] | select(.headBranch==\"v$VER\" and .workflowName==\"Publish to NuGet\") | \"\(.databaseId)\t\(.status)\t\(.conclusion)\""
gh run watch <run-id>
```

A green workflow is still not sufficient evidence — check nuget.org and the GitHub release
directly. Note nuget.org's flat-container index can lag a successful push by several minutes, so
re-check before concluding it failed:

```bash
# nuget.org — both packages must list $VER
curl -s https://api.nuget.org/v3-flatcontainer/calor/index.json     | jq -r '.versions[-3:][]'
curl -s https://api.nuget.org/v3-flatcontainer/calor.sdk/index.json | jq -r '.versions[-3:][]'

# (No release assets are expected: the VSIX channel was removed after v0.13.1.)
```

Strongest check for NuGet — install the published tool and exercise Z3, which is the part most
likely to be broken by a packaging change:

```bash
rm -rf /tmp/calorcheck          # else a stale/newer install makes this a no-op
dotnet tool install --tool-path /tmp/calorcheck calor --version "$VER"
/tmp/calorcheck/calor --version
/tmp/calorcheck/calor verify samples/Verification/proven-contracts.calr
```

Expect `Proven: 14`, **`Skipped: 0`**, exit 0. A package whose Z3 is broken does **not** print
"Z3 not available" (that string only ever appears in test-skip messages) — it emits diagnostic
`Calor0710`, *"Static contract verification skipped: Z3 SMT solver is not available"*, and a
nonzero `Skipped:` count. Do not pick a different sample casually:
`samples/Contracts/contracts.calr` exits 1 **by design** (it contains a deliberately disproven
postcondition) and will look like a broken package.

Known failure modes, so they are recognised rather than re-diagnosed:

| Symptom | Cause | Fix |
|---|---|---|
| `sha256sum: WARNING: N computed checksums did NOT match` | The pinned `z3-binaries` release drifted | Republish via `build-z3.yml` (`workflow_dispatch`), commit the manifest it prints, **then re-publish via `workflow_dispatch`, not `gh run rerun`** — see below. **Never rehash the live assets.** |
| `test` job aborts with `terminate called after throwing 'std::system_error'` | Native (Z3) crash taking down the test host; `publish` is correctly skipped | Re-dispatch; if it reproduces, stop and investigate — a repeating native crash is not flake |

There is no editor-extension channel to verify. VS Code support was withdrawn after v0.13.1 and
the Marketplace listing is abandoned at v0.3.8; the language server (`calor lsp`) ships inside the
`calor` NuGet package, so verifying that package covers it.

#### Retrying a failed publish (do NOT re-cut the version)

A failed publish is not a reason to delete the tag or bump the version. Fix the cause and re-run
the failing channel only.

- **NuGet.** `gh run rerun` re-runs at the **tag's** commit, and `publish-nuget.yml` reads
  `.github/z3-binaries-<ver>.sha256` from the checked-out tree. So if the fix was a manifest
  change committed to `main`, a rerun re-reads the *old* manifest off the tag and fails
  identically. Use the dispatch path instead, which checks out the default branch and takes a
  version override: `gh workflow run publish-nuget.yml -f version=$VER`. Safe to repeat — the push
  uses `--skip-duplicate`.
- **A failed publish never justifies re-cutting.** Fix the cause and re-dispatch; the tag stays.

Note that republishing `z3-binaries` retroactively changes what **every past tag** resolves to, so
older tags' manifests become invalid. That is a reason to prefer fixing forward.

Report the release as shipped only after nuget.org confirms **both** packages at the new version.
If only one landed, say exactly which one.

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
