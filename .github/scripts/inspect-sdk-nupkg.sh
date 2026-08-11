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

# At least one Z3 native must be present (the packing machine's RID)
if ! grep -qE '^tasks/net10\.0/runtimes/(linux-(x64|arm64)/native/libz3\.so|osx-(x64|arm64)/native/libz3\.dylib|win-(x64|arm64)/native/libz3\.dll)$' <<<"$LISTING"; then
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
