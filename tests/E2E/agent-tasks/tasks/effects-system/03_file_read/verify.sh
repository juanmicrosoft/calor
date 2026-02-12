#!/usr/bin/env bash
# Verify: File read effect
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Effects.calr"

[[ -f "$CALR_FILE" ]] || { echo "Effects.calr not found"; exit 1; }

# Check for ReadFile function
grep -q "ReadFile" "$CALR_FILE" || { echo "ReadFile function not found"; exit 1; }

# Check for file system read effect §E{fs:r} or §E{fs}
grep -qE "§E\{fs(:r)?\}" "$CALR_FILE" || { echo "File system read effect (§E{fs:r}) not found"; exit 1; }

# Check for file read call
grep -qE "(File.ReadAllText|File.ReadAllLines|File.Read)" "$CALR_FILE" || { echo "File read call not found"; exit 1; }

echo "Verification passed: ReadFile function found with file read effect"
exit 0
