#!/bin/bash
# Downloads Z3 managed assembly and native libraries for all supported platforms
# The managed Microsoft.Z3.dll must come from the same Z3 release as the native libraries
# to ensure compatibility (NuGet package 4.12.2 doesn't work with Z3 4.15.7 natives)
#
# NOTE: On ARM64 macOS, the pre-built binaries have assembly loading issues.
# This script will redirect to build-z3-from-source.sh on that platform.
set -e

Z3_VERSION="4.15.7"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNTIMES_DIR="$SCRIPT_DIR/../runtimes"
Z3_DIR="$SCRIPT_DIR/../z3"
TEMP_DIR="$SCRIPT_DIR/../.z3-temp"

# Check for ARM64 macOS - pre-built binaries don't work there, need to build from source
if [ "$(uname -s)" = "Darwin" ] && [ "$(uname -m)" = "arm64" ]; then
    echo "Detected ARM64 macOS - pre-built Z3 binaries have compatibility issues."
    echo "Building Z3 from source instead..."
    echo ""
    exec "$SCRIPT_DIR/build-z3-from-source.sh"
fi

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

# Use one platform to get the managed DLL (it's the same across all platforms)
MANAGED_DLL_ARCHIVE="z3-${Z3_VERSION}-x64-win"

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
        curl -L -o "$zip_file" "${BASE_URL}/${MANAGED_DLL_ARCHIVE}.zip" 2>/dev/null
    fi

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
        curl -L -o "$zip_file" "${BASE_URL}/${archive}.zip" 2>/dev/null
    fi

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
