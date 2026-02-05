# /release - Create a New Calor Release

This skill automates the release process: bump versions across all components, create a PR, merge it, and create a GitHub release with proper tagging.

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

### 2. Update Version Files

Update these files with the new version:

| File | What to Update |
|------|----------------|
| `Directory.Build.props` | `<Version>X.Y.Z</Version>` |
| `editors/vscode/package.json` | `"version": "X.Y.Z"` |
| `website/package.json` | `"version": "X.Y.Z"` |
| `CHANGELOG.md` | Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (use today's date) |

When updating CHANGELOG.md:
1. Find the line `## [Unreleased]`
2. Replace it with `## [X.Y.Z] - YYYY-MM-DD` where YYYY-MM-DD is today's date
3. Add a new `## [Unreleased]` section above the new version header

### 3. Create Release Branch and PR

```bash
git checkout -b release/vX.Y.Z
git add Directory.Build.props editors/vscode/package.json website/package.json CHANGELOG.md
git commit -m "chore: bump version to X.Y.Z"
git push -u origin release/vX.Y.Z
```

Create the PR:

```bash
gh pr create --title "Release vX.Y.Z" --body "$(cat <<'EOF'
## Summary
- Bump version to X.Y.Z
- Update CHANGELOG.md with release date

## Checklist
- [ ] Version updated in Directory.Build.props
- [ ] Version updated in editors/vscode/package.json
- [ ] Version updated in website/package.json
- [ ] CHANGELOG.md updated with version and date
EOF
)"
```

### 4. Merge the PR

Wait for any CI checks, then merge:

```bash
gh pr merge --squash --delete-branch
```

### 5. Create GitHub Release

First, extract the changelog content for this version from CHANGELOG.md. The content is between `## [X.Y.Z]` and the next `## [` line.

Determine if this is a pre-release (any version < 1.0.0 is pre-release).

Create the release:

```bash
# For pre-release (version < 1.0.0):
gh release create vX.Y.Z --title "vX.Y.Z" --notes "CHANGELOG_CONTENT" --prerelease

# For stable release (version >= 1.0.0):
gh release create vX.Y.Z --title "vX.Y.Z" --notes "CHANGELOG_CONTENT"
```

### 6. Return to Main Branch

```bash
git checkout main
git pull
```

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
