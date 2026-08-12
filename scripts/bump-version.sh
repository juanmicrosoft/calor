#!/bin/bash
set -e

# Bump version across all version-tracked files in the Calor project
# Usage: ./scripts/bump-version.sh <new-version>
# Example: ./scripts/bump-version.sh 0.2.0

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

if [ -z "$1" ]; then
    echo "Usage: $0 <new-version>"
    echo "Example: $0 0.2.0"
    echo ""
    echo "Current versions:"
    echo "  Directory.Build.props: $(grep -oP '(?<=<Version>)[^<]+' "$ROOT_DIR/Directory.Build.props")"
    exit 1
fi

NEW_VERSION="$1"

# Validate version format (semver)
if ! [[ "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
    echo "Error: Invalid version format. Use semver (e.g., 0.2.0 or 0.2.0-beta.1)"
    exit 1
fi

echo "Bumping version to $NEW_VERSION..."

# Update Directory.Build.props (.NET projects)
echo "  Updating Directory.Build.props..."
sed -i.bak "s|<Version>[^<]*</Version>|<Version>$NEW_VERSION</Version>|" "$ROOT_DIR/Directory.Build.props"
rm -f "$ROOT_DIR/Directory.Build.props.bak"

# Update VS Code extension package.json

echo ""
echo "Version bumped to $NEW_VERSION in:"
echo "  - Directory.Build.props (affects all .NET projects)"
echo ""
echo "Don't forget to:"
echo "  1. Update CHANGELOG.md"
echo "  2. Commit the changes"
echo "  3. Create a git tag: git tag v$NEW_VERSION"
