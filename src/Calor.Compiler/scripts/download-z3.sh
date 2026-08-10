#!/bin/bash
# Downloads Z3 managed assembly and native libraries for all supported platforms
# The managed Microsoft.Z3.dll must come from the same Z3 release as the native libraries
# to ensure compatibility (NuGet package 4.12.2 doesn't work with Z3 4.15.7 natives)
#
# Every platform uses the checksum-verified upstream binaries, including ARM64
# macOS. This script used to divert that platform to build-z3-from-source.sh on
# the grounds that "the pre-built binaries have assembly loading issues" — see
# the note below on why that diagnosis was wrong.
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
Z3_DIR="$SCRIPT_DIR/../z3"
TEMP_DIR="$SCRIPT_DIR/../.z3-temp"

# NO ARM64-macOS SOURCE-BUILD DIVERSION. There used to be one here, justified as
# "pre-built Z3 binaries have compatibility issues" on that platform. The
# symptom was real; the diagnosis was not. The failure was an ASSEMBLY load
# error, and it came from the MANAGED wrapper, not from the native library: this
# script pinned MANAGED_DLL_ARCHIVE to the x64-win archive, whose Microsoft.Z3.dll
# is PE32+/AMD64 and cannot load in an arm64 process. Building everything from
# source masked it by producing an architecture-neutral wrapper as a side effect.
#
# With MANAGED_DLL_ARCHIVE corrected to an architecture-neutral archive, the
# upstream arm64-osx native works: verified on ARM64 macOS with the upstream
# prebuilt libz3.dylib in place, Calor.Verification.Tests is 359/359 with zero
# skips. Removing the diversion cuts roughly 20 minutes off every ARM64-macOS
# build, which was the entire reason the VS Code publish workflow took ~23min.

# Platform mappings: "rid|archive_name|lib_name_in_archive|lib_name_output"
PLATFORMS=(
    "osx-arm64|z3-${Z3_VERSION}-arm64-osx-15.7.3|libz3.dylib|libz3.dylib"
    "osx-x64|z3-${Z3_VERSION}-x64-osx-15.7.3|libz3.dylib|libz3.dylib"
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

    # Extract Microsoft.Z3.dll
    unzip -q -o "$zip_file" "${MANAGED_DLL_ARCHIVE}/bin/Microsoft.Z3.dll" -d "$TEMP_DIR" 2>/dev/null || true

    # Find and move the DLL
    found_dll=$(find "$TEMP_DIR" -name "Microsoft.Z3.dll" -type f 2>/dev/null | head -1)
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

    # Find and extract just the library file
    unzip -q -o "$zip_file" "${archive}/bin/${lib_in}" -d "$TEMP_DIR" 2>/dev/null || \
    unzip -q -o "$zip_file" "${archive}/lib/${lib_in}" -d "$TEMP_DIR" 2>/dev/null || \
    unzip -q -o "$zip_file" "*/${lib_in}" -d "$TEMP_DIR" 2>/dev/null

    # Find the extracted library and move it
    found_lib=$(find "$TEMP_DIR" -name "$lib_in" -type f 2>/dev/null | head -1)
    if [ -n "$found_lib" ]; then
        mv "$found_lib" "$target_file"
        echo "[$rid] Done."
    else
        echo "[$rid] WARNING: Could not find $lib_in in archive"
    fi
done

# Cleanup
rm -rf "$TEMP_DIR"

echo ""
echo "Z3 libraries ready."
echo "  Managed: $Z3_DIR/Microsoft.Z3.dll"
echo "  Natives: $RUNTIMES_DIR/*/native/"
