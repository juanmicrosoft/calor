#!/usr/bin/env bash
# Generates bench/phase0-agent-native/metadata-references-manifest.json from the
# .NET 10 SDK's shared-framework runtime assemblies (source of truth for
# GeneratedCSharpCompiler.References). Runs at S1 to freeze F-2's content; the
# same script is invoked by the auto-regen path when TPA drifts inside
# sdkVersionRange (v0.14 metadata-binding scoping doc §D3).
#
# The whitelist is: System.*, Microsoft.CSharp, Microsoft.VisualBasic,
# netstandard, mscorlib. Compiler-internal assemblies (Microsoft.CodeAnalysis.*,
# Z3, Calor.Compiler) are excluded so Calor code cannot bind to compiler
# internals.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MANIFEST_PATH="$REPO_ROOT/bench/phase0-agent-native/metadata-references-manifest.json"

# Discover the .NET 10 shared framework runtime directory. dotnet --info prints
# "Microsoft.NETCore.App 10.x.y [path]"; we grab the first 10.x line.
runtime_line=$(dotnet --info | grep -E "^ *Microsoft.NETCore.App 10\." | head -1)
if [ -z "$runtime_line" ]; then
    echo "error: could not locate Microsoft.NETCore.App 10.x runtime via 'dotnet --info'" >&2
    exit 1
fi

# "Microsoft.NETCore.App 10.0.5 [/opt/homebrew/Cellar/dotnet/10.0.302/libexec/shared/Microsoft.NETCore.App]"
version=$(awk '{print $2}' <<<"$runtime_line")
base=$(sed -E 's/.*\[(.*)\]/\1/' <<<"$runtime_line")
RUNTIME_VERSION_DIR="$base/$version"
if [ ! -d "$RUNTIME_VERSION_DIR" ]; then
    echo "error: runtime version directory not found: $RUNTIME_VERSION_DIR" >&2
    exit 1
fi

# Whitelist matcher — assembly *file* names (without .dll). Adjust as F-1
# amendments require new BCL surface (per F-1 amendment rules: additive by PR,
# subtractive with supersession entry).
matches_whitelist() {
    local name="$1"
    case "$name" in
        System.*|Microsoft.CSharp|Microsoft.VisualBasic|netstandard|mscorlib) return 0 ;;
        # Explicitly NOT: Microsoft.CodeAnalysis.*, Z3, Calor.Compiler, Calor.Sdk
    esac
    return 1
}

# Portable SHA-256 — sha256sum on Linux, shasum -a 256 on macOS.
if command -v sha256sum >/dev/null 2>&1; then
    SHA_CMD="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
    SHA_CMD="shasum -a 256"
else
    echo "error: neither sha256sum nor shasum available" >&2
    exit 1
fi

# Build JSON entries.
entries=""
count=0
for dll in "$RUNTIME_VERSION_DIR"/*.dll; do
    [ -f "$dll" ] || continue
    name=$(basename "$dll" .dll)
    matches_whitelist "$name" || continue
    sha=$($SHA_CMD "$dll" | awk '{print $1}')
    if [ -n "$entries" ]; then entries="$entries,"; fi
    entries="$entries
    { \"assemblyName\": \"$name\", \"version\": \"$version\", \"sha256\": \"$sha\" }"
    count=$((count + 1))
done

if [ "$count" -eq 0 ]; then
    echo "error: no matching assemblies found in $RUNTIME_VERSION_DIR" >&2
    exit 1
fi

# Write the manifest.
timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)
cat > "$MANIFEST_PATH" <<EOF
{
  "\$schema": "./metadata-references-manifest.schema.json",
  "sdkVersionRange": "10.0.0 - 10.9.999",
  "generatedFrom": "$RUNTIME_VERSION_DIR",
  "generatedAt": "$timestamp",
  "assemblies": [$entries
  ]
}
EOF

echo "Wrote $MANIFEST_PATH ($count assemblies from .NET $version)"
