#!/usr/bin/env bash
# Package-content inspection for the Calor.Sdk nupkg (#787, W1 Slice 4).
# Fails loud if any required entry is missing.
#
# Usage: inspect-sdk-nupkg.sh <path-to-Calor.Sdk.nupkg> [--all-rids]
#   --all-rids  additionally require the Z3 native for every supported RID
#               (used by release publishing; a local dev pack only carries the
#               RIDs present in src/Calor.Compiler/runtimes).
set -euo pipefail

NUPKG="${1:?usage: inspect-sdk-nupkg.sh <nupkg> [--all-rids]}"
ALL_RIDS="${2:-}"

if [ ! -f "$NUPKG" ]; then
  echo "ERROR: nupkg not found: $NUPKG" >&2
  exit 1
fi

LISTING="$(unzip -Z1 "$NUPKG")"

require() {
  local entry="$1"
  if ! grep -qxF "$entry" <<<"$LISTING"; then
    echo "ERROR: Calor.Sdk package is missing required entry: $entry" >&2
    MISSING=1
  fi
}

MISSING=0

# SDK plumbing
require "Sdk/Sdk.props"
require "Sdk/Sdk.targets"

# Task assembly + full compiler closure
require "tasks/net10.0/Calor.Tasks.dll"
require "tasks/net10.0/calor.dll"
require "tasks/net10.0/Calor.Runtime.dll"
require "tasks/net10.0/Microsoft.Z3.dll"
require "tasks/net10.0/Microsoft.CodeAnalysis.dll"
require "tasks/net10.0/Microsoft.CodeAnalysis.CSharp.dll"
require "tasks/net10.0/Calor.Tasks.deps.json"

# Self-contained topology: task dependencies are bundled, never left for the
# consumer's NuGet graph to resolve in an isolated MSBuild load context.
NUSPEC="$(unzip -Z1 "$NUPKG" | grep -E '^Calor\.Sdk(\.[^/]+)?\.nuspec$' | head -1 || true)"
if [ -z "$NUSPEC" ]; then
  echo "ERROR: Calor.Sdk package contains no nuspec" >&2
  MISSING=1
elif unzip -p "$NUPKG" "$NUSPEC" | grep -q '<dependency '; then
  echo "ERROR: self-contained Calor.Sdk package must not declare external package dependencies" >&2
  MISSING=1
fi

if grep -qE '^tasks/net10\.0/runtimes/(osx-x64|win-x86)/' <<<"$LISTING"; then
  echo "ERROR: package contains unsupported osx-x64 or win-x86 assets" >&2
  MISSING=1
fi

python3 - "$NUPKG" <<'PY'
import json
import sys
import zipfile

nupkg = sys.argv[1]
deps_path = "tasks/net10.0/Calor.Tasks.deps.json"
host_assemblies = {
    "Microsoft.Build.Framework.dll",
    "Microsoft.Build.Utilities.Core.dll",
}

with zipfile.ZipFile(nupkg) as package:
    entries = set(package.namelist())
    dependencies = json.loads(package.read(deps_path))

missing = []
for target in dependencies["targets"].values():
    for library, assets in target.items():
        for asset in (assets.get("runtime") or {}):
            filename = asset.rsplit("/", 1)[-1]
            if filename in host_assemblies:
                continue
            expected = f"tasks/net10.0/{filename}"
            if expected not in entries:
                missing.append((library, asset, expected))

        for asset, metadata in (assets.get("runtimeTargets") or {}).items():
            if metadata.get("assetType") != "runtime":
                continue
            expected = f"tasks/net10.0/{asset}"
            if expected not in entries:
                missing.append((library, asset, expected))

if missing:
    for library, asset, expected in missing:
        print(
            f"ERROR: dependency closure missing {expected} "
            f"(required by {library}: {asset})",
            file=sys.stderr,
        )
    raise SystemExit(1)
PY

# At least one Z3 native must be present (the packing machine's RID)
if ! grep -qE '^tasks/net10\.0/runtimes/(linux-(x64|arm64)/native/libz3\.so|osx-arm64/native/libz3\.dylib|win-(x64|arm64)/native/libz3\.dll)$' <<<"$LISTING"; then
  echo "ERROR: Calor.Sdk package contains no Z3 native library under tasks/net10.0/runtimes/" >&2
  MISSING=1
fi

if [ "$ALL_RIDS" = "--all-rids" ]; then
  require "tasks/net10.0/runtimes/linux-x64/native/libz3.so"
  require "tasks/net10.0/runtimes/linux-arm64/native/libz3.so"
  # osx-x64 dropped (Z3 chain closure): Intel macOS unsupported for verification.
  require "tasks/net10.0/runtimes/osx-arm64/native/libz3.dylib"
  require "tasks/net10.0/runtimes/win-x64/native/libz3.dll"
  require "tasks/net10.0/runtimes/win-arm64/native/libz3.dll"
fi

# Things that must NOT be in the package: MSBuild host assemblies (loaded from
# the host, packing them risks version skew) and the source-tree apphost.
if grep -qE '^tasks/net10\.0/Microsoft\.Build\.' <<<"$LISTING"; then
  echo "ERROR: Calor.Sdk package must not bundle Microsoft.Build.* assemblies" >&2
  MISSING=1
fi

if [ "$MISSING" -ne 0 ]; then
  echo "FAIL: Calor.Sdk package content inspection failed for $NUPKG" >&2
  exit 1
fi

echo "OK: Calor.Sdk package content inspection passed ($(wc -l <<<"$LISTING" | tr -d ' ') entries)"
