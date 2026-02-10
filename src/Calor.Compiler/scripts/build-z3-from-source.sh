#!/bin/bash
# Builds Z3 from source with .NET bindings
# Required for ARM64 macOS where pre-built NuGet packages don't work
# Prerequisites: Python 3, .NET SDK, C++ compiler (Xcode command line tools)
set -e

Z3_VERSION="4.15.7"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
Z3_DIR="$SCRIPT_DIR/../z3"
RUNTIMES_DIR="$SCRIPT_DIR/../runtimes"
BUILD_DIR="/tmp/z3-build-$$"

echo "Z3 Source Builder"
echo "================="
echo "Version: $Z3_VERSION"
echo "Build dir: $BUILD_DIR"
echo ""

# Check prerequisites
command -v python3 >/dev/null 2>&1 || { echo "Error: python3 is required"; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo "Error: dotnet SDK is required"; exit 1; }
command -v g++ >/dev/null 2>&1 || { echo "Error: C++ compiler (g++) is required"; exit 1; }

# Check if already built
if [ -f "$Z3_DIR/Microsoft.Z3.dll" ]; then
    echo "Microsoft.Z3.dll already exists at $Z3_DIR"
    echo "Delete it to rebuild from source"
    exit 0
fi

# Detect platform
ARCH=$(uname -m)
OS=$(uname -s)
RID=""

if [ "$OS" = "Darwin" ]; then
    if [ "$ARCH" = "arm64" ]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
    LIB_NAME="libz3.dylib"
elif [ "$OS" = "Linux" ]; then
    if [ "$ARCH" = "aarch64" ]; then
        RID="linux-arm64"
    else
        RID="linux-x64"
    fi
    LIB_NAME="libz3.so"
else
    echo "Error: Unsupported platform $OS/$ARCH"
    exit 1
fi

echo "Detected platform: $RID"
echo ""

# Clone Z3
echo "[1/4] Cloning Z3 $Z3_VERSION..."
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"
git clone --depth 1 --branch "z3-$Z3_VERSION" https://github.com/Z3Prover/z3.git
cd z3

# Configure
echo "[2/4] Configuring build with .NET bindings..."
python3 scripts/mk_make.py --dotnet

# Build native library
echo "[3/4] Building native library (this takes a few minutes)..."
cd build
make -j$(getconf _NPROCESSORS_ONLN)

# Build .NET bindings
echo "[4/4] Building .NET bindings..."
cd dotnet
dotnet build -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false

# Copy outputs
echo ""
echo "Copying outputs..."
mkdir -p "$Z3_DIR"
mkdir -p "$RUNTIMES_DIR/$RID/native"

cp "$BUILD_DIR/z3/build/dotnet/bin/Debug/netstandard1.4/Microsoft.Z3.dll" "$Z3_DIR/"
cp "$BUILD_DIR/z3/build/$LIB_NAME" "$RUNTIMES_DIR/$RID/native/"

echo ""
echo "Build complete!"
echo "  Managed: $Z3_DIR/Microsoft.Z3.dll"
echo "  Native:  $RUNTIMES_DIR/$RID/native/$LIB_NAME"

# Cleanup
rm -rf "$BUILD_DIR"
