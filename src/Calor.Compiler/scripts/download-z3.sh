#!/bin/bash
# Downloads Z3 managed assembly and native libraries for all supported platforms
# The managed Microsoft.Z3.dll must come from the same Z3 release as the native libraries
# to ensure compatibility (NuGet package 4.12.2 doesn't work with Z3 4.15.7 natives)
#
# Every platform uses the checksum-verified upstream binaries, including ARM64
# macOS. This script used to divert that platform to build-z3-from-source.sh;
# see the note below the checksum helper for why that is no longer needed.
set -e

Z3_VERSION="4.15.7"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Resilient download: GitHub release fetches occasionally fail on a transient
# network/TLS error (e.g. curl exit 35), which used to fail the whole build on a
# flaky connection. Retry transient errors a handful of times with backoff, and
# fail loudly (-fS) only after they are exhausted. -L follows the release redirect.
download() {
    local url="$1" out="$2"
    curl -fL -sS \
        --retry 5 --retry-delay 3 --retry-all-errors \
        --connect-timeout 30 --max-time 600 \
        -o "$out" "$url"
}

# W1 Slice 2 (#789): every upstream archive is verified against the committed
# SHA-256 manifest before anything is extracted from it. A corrupt or
# substituted archive fails the build instead of feeding the toolchain.
CHECKSUM_MANIFEST="$SCRIPT_DIR/z3-upstream-${Z3_VERSION}.sha256"
verify_archive() {
    local zip_file="$1"
    local name
    name="$(basename "$zip_file")"
    if [ ! -s "$CHECKSUM_MANIFEST" ]; then
        echo "ERROR: checksum manifest $CHECKSUM_MANIFEST missing — refusing to use unverified Z3 archives" >&2
        exit 1
    fi
    local expected
    expected="$(grep -v '^#' "$CHECKSUM_MANIFEST" | awk -v n="$name" '$2 == n {print $1}')"
    if [ -z "$expected" ]; then
        echo "ERROR: no checksum entry for $name in $CHECKSUM_MANIFEST" >&2
        exit 1
    fi
    local actual
    actual="$(shasum -a 256 "$zip_file" | awk '{print $1}')"
    if [ "$actual" != "$expected" ]; then
        echo "ERROR: checksum mismatch for $name" >&2
        echo "  expected: $expected" >&2
        echo "  actual:   $actual" >&2
        rm -f "$zip_file"
        exit 1
    fi
    echo "  [verified] $name"
}
RUNTIMES_DIR="$SCRIPT_DIR/../runtimes"

# Z3 chain closure (#916 review F2): the osx-x64 RID is dropped, but the
# provenance stamp does not invalidate on a RID-set change — an existing
# checkout keeps the mislabeled dylib forever and local packs resurrect the
# dropped RID (the csproj packs runtimes/** by glob). Purge unconditionally.
rm -rf "$RUNTIMES_DIR/osx-x64"
Z3_DIR="$SCRIPT_DIR/../z3"
TEMP_DIR="$SCRIPT_DIR/../.z3-temp"

# Always clean the scratch directory, not just on the success path. Without this
# an interrupted run leaves ~376MB of archives behind, and — worse — leaves
# half-extracted libraries that the per-RID extraction below is designed to stop
# being mistaken for the right file.
trap 'rm -rf "$TEMP_DIR"' EXIT

# NO ARM64-macOS SOURCE-BUILD DIVERSION. There used to be one here, added by
# 7aeddf1e ("feat: Add Z3 source build support for ARM64 macOS"), whose message
# reports "assembly loading issues on ARM64 macOS" from "the pre-built Z3 NuGet
# package and GitHub release binaries" — and which moved the build off the
# Microsoft.Z3 NuGet package in the same change.
#
# The original failure is NOT reconstructible from the record and no claim is
# made here about what caused it. Note that 7aeddf1e introduced the diversion
# and MANAGED_DLL_ARCHIVE="...-x64-win" together, so the committed script never
# reached the managed-download path on ARM64 macOS; whatever failed, it was not
# this script fetching that wrapper.
#
# What IS established, by measurement rather than inference:
#   * the upstream arm64-osx native libz3.dylib works on ARM64 macOS today,
#     paired with an architecture-neutral managed wrapper — Calor.Verification
#     .Tests 359/359 with ZERO skips (the suite is SkippableFact-gated on
#     Z3ContextFactory.IsAvailable, so an unloadable Z3 would show up as skips,
#     not as vacuous passes);
#   * the x64-win wrapper this script used to designate is PE32+/AMD64 and
#     cannot load in an arm64 process at all, so it could not have been serving
#     ARM64 macOS correctly either way.
#
# So the diversion is unnecessary regardless of what originally motivated it,
# and it cost a full C++ build (~20 min) on every ARM64 macOS checkout.

# Platform mappings: "rid|archive_name|lib_name_in_archive|lib_name_output"
PLATFORMS=(
    "osx-arm64|z3-${Z3_VERSION}-arm64-osx-15.7.3|libz3.dylib|libz3.dylib"
    # osx-x64 removed (Z3 chain closure): upstream's x64-osx archive contains an
    # arm64 binary — Intel macOS is unsupported for verification.
    "win-arm64|z3-${Z3_VERSION}-arm64-win|libz3.dll|libz3.dll"
    "win-x64|z3-${Z3_VERSION}-x64-win|libz3.dll|libz3.dll"
    "win-x86|z3-${Z3_VERSION}-x86-win|libz3.dll|libz3.dll"
    "linux-arm64|z3-${Z3_VERSION}-arm64-glibc-2.38|libz3.so|libz3.so"
    "linux-x64|z3-${Z3_VERSION}-x64-glibc-2.39|libz3.so|libz3.so"
)

# One archive supplies the managed wrapper for every platform. It is NOT "the
# same across all platforms" as previously assumed: upstream's x64-win archive
# ships a PE32+/AMD64 Microsoft.Z3.dll that throws "assembly architecture is not
# compatible with the current process architecture" when loaded in an arm64
# process, whereas the glibc archives ship the ordinary PE32/I386 AnyCPU shape.
# The wrapper is pure P/Invoke (the per-RID native libz3 carries the platform
# difference), so the architecture-neutral one is correct everywhere — and it is
# the only choice that works on linux-arm64 and win-arm64. Keep in sync with
# MANAGED_DLL_ARCHIVE in .github/workflows/build-z3.yml.
MANAGED_DLL_ARCHIVE="z3-${Z3_VERSION}-x64-glibc-2.39"

BASE_URL="https://github.com/Z3Prover/z3/releases/download/z3-${Z3_VERSION}"

echo "Z3 Library Downloader"
echo "====================="
echo "Version: $Z3_VERSION"
echo ""

# ---------------------------------------------------------------------------
# Provenance stamp and stale-artifact invalidation.
#
# z3/ and runtimes/ are gitignored build artifacts, and nothing used to record
# WHERE their contents came from. Every check below is a bare existence test, so
# an artifact — however it was produced — was kept forever. That became a real
# hazard when the source of the managed wrapper changed: a checkout that had
# previously run the ARM64-macOS source build kept its Debug, source-compiled
# wrapper and its source-built osx-arm64 native indefinitely, while newly added
# RIDs came from upstream. The result was a silent MIX, and a local `dotnet
# pack` from such a tree shipped the Debug wrapper this project has since gone
# to some trouble to stop publishing.
#
# The stamp records which upstream archive supplied the wrapper. If it is
# missing (a pre-stamp or source-built checkout) or differs (the designated
# archive changed), every artifact is discarded and refetched — so "present" and
# "up to date" stop being different things.
# ---------------------------------------------------------------------------
PROVENANCE_FILE="$Z3_DIR/.provenance"
EXPECTED_PROVENANCE="upstream ${Z3_VERSION} managed=${MANAGED_DLL_ARCHIVE}"

if [ -f "$Z3_DIR/Microsoft.Z3.dll" ] || [ -d "$RUNTIMES_DIR" ]; then
    actual_provenance="$(cat "$PROVENANCE_FILE" 2>/dev/null || true)"
    if [ "$actual_provenance" != "$EXPECTED_PROVENANCE" ]; then
        echo "Existing Z3 artifacts do not carry the current provenance stamp:"
        echo "  expected: $EXPECTED_PROVENANCE"
        echo "  found:    ${actual_provenance:-<unstamped: pre-stamp or source-built checkout>}"
        echo "Discarding them and refetching from the verified upstream archives."
        echo ""
        rm -f "$Z3_DIR/Microsoft.Z3.dll"
        rm -rf "$RUNTIMES_DIR"
    fi
fi

# Check if managed DLL exists
managed_dll_exists=true
if [ ! -f "$Z3_DIR/Microsoft.Z3.dll" ]; then
    managed_dll_exists=false
fi

# Check if all native libraries already exist
all_natives_exist=true
for platform in "${PLATFORMS[@]}"; do
    IFS='|' read -r rid archive lib_in lib_out <<< "$platform"
    if [ ! -f "$RUNTIMES_DIR/$rid/native/$lib_out" ]; then
        all_natives_exist=false
        break
    fi
done

if [ "$managed_dll_exists" = true ] && [ "$all_natives_exist" = true ]; then
    echo "All Z3 libraries already present. Skipping download."
    exit 0
fi

# Create directories
mkdir -p "$TEMP_DIR"
mkdir -p "$Z3_DIR"

# Download managed DLL if needed
if [ "$managed_dll_exists" = false ]; then
    echo "[Managed] Downloading Microsoft.Z3.dll..."

    zip_file="$TEMP_DIR/${MANAGED_DLL_ARCHIVE}.zip"

    # Download if not cached
    if [ ! -f "$zip_file" ]; then
        download "${BASE_URL}/${MANAGED_DLL_ARCHIVE}.zip" "$zip_file"
    fi
    verify_archive "$zip_file"

    # Extract Microsoft.Z3.dll into a private scratch dir, for the same reason
    # the native loop below does: several archives contain a Microsoft.Z3.dll,
    # and they are NOT interchangeable — the x64-win one is PE32+/AMD64 and
    # cannot load in an arm64 process at all. Selecting it with a find over the
    # shared temp dir would make which wrapper you get depend on what a previous
    # iteration happened to leave lying around.
    managed_extract="$TEMP_DIR/extract-managed"
    rm -rf "$managed_extract"
    mkdir -p "$managed_extract"
    unzip -q -o "$zip_file" "${MANAGED_DLL_ARCHIVE}/bin/Microsoft.Z3.dll" -d "$managed_extract" 2>/dev/null || true

    # Find and move the DLL
    found_dll=$(find "$managed_extract" -name "Microsoft.Z3.dll" -type f 2>/dev/null | head -1)
    if [ -n "$found_dll" ]; then
        mv "$found_dll" "$Z3_DIR/Microsoft.Z3.dll"
        echo "[Managed] Done."
    else
        echo "[Managed] WARNING: Could not find Microsoft.Z3.dll in archive"
    fi
fi

# Download native libraries
for platform in "${PLATFORMS[@]}"; do
    IFS='|' read -r rid archive lib_in lib_out <<< "$platform"

    target_dir="$RUNTIMES_DIR/$rid/native"
    target_file="$target_dir/$lib_out"

    if [ -f "$target_file" ]; then
        echo "[$rid] Already exists, skipping."
        continue
    fi

    echo "[$rid] Downloading..."

    zip_file="$TEMP_DIR/${archive}.zip"

    # Download if not cached
    if [ ! -f "$zip_file" ]; then
        download "${BASE_URL}/${archive}.zip" "$zip_file"
    fi
    verify_archive "$zip_file"

    # Extract the library
    mkdir -p "$target_dir"

    # Extract into a scratch directory PRIVATE TO THIS RID. The search below
    # used to run over the whole of TEMP_DIR, which accumulates the extracts of
    # every archive — and the library names are not unique across archives:
    # three ship a file called libz3.dll, two libz3.so, two libz3.dylib. So a
    # leftover extract from another RID could be selected here and installed
    # under this one, silently giving (for example) an arm64 libz3.dll to
    # win-x64. The checksum guard cannot catch that: it verifies the ZIP, never
    # the file that ends up in runtimes/. Isolating the extract makes the
    # selection unambiguous instead of readdir-order dependent.
    extract_dir="$TEMP_DIR/extract-$rid"
    rm -rf "$extract_dir"
    mkdir -p "$extract_dir"

    # Find and extract just the library file
    unzip -q -o "$zip_file" "${archive}/bin/${lib_in}" -d "$extract_dir" 2>/dev/null || \
    unzip -q -o "$zip_file" "${archive}/lib/${lib_in}" -d "$extract_dir" 2>/dev/null || \
    unzip -q -o "$zip_file" "*/${lib_in}" -d "$extract_dir" 2>/dev/null

    # Find the extracted library and move it
    found_lib=$(find "$extract_dir" -name "$lib_in" -type f 2>/dev/null | head -1)
    if [ -n "$found_lib" ]; then
        mv "$found_lib" "$target_file"
        echo "[$rid] Done."
    else
        echo "[$rid] WARNING: Could not find $lib_in in archive"
    fi
done

# Record what these artifacts are, so a later run can tell "present" from
# "current". Written only after every download succeeded.
mkdir -p "$Z3_DIR"
printf '%s\n' "$EXPECTED_PROVENANCE" > "$PROVENANCE_FILE"

# Cleanup (the EXIT trap also covers the failure paths)
rm -rf "$TEMP_DIR"

echo ""
echo "Z3 libraries ready."
echo "  Managed: $Z3_DIR/Microsoft.Z3.dll"
echo "  Natives: $RUNTIMES_DIR/*/native/"
