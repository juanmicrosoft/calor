#!/usr/bin/env bash
set -euo pipefail

# Local Install Script for opalc
# Run from repo root: ./scripts/install-local.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

cd "$REPO_ROOT"

echo "Building opalc..."
dotnet build src/Opal.Compiler/Opal.Compiler.csproj -c Release

echo "Packing..."
dotnet pack src/Opal.Compiler/Opal.Compiler.csproj -c Release -o ./nupkg

echo "Installing globally..."
dotnet tool install -g --add-source ./nupkg opalc 2>/dev/null \
  || dotnet tool update -g --add-source ./nupkg opalc

echo "Verifying..."
opalc --help

echo "Done! opalc installed globally."
