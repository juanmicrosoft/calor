#!/usr/bin/env bash
# Verify: Generic function
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Generic.calr"

[[ -f "$CALR_FILE" ]] || { echo "Generic.calr not found"; exit 1; }

# Check for Identity function
grep -q "Identity" "$CALR_FILE" || { echo "Identity function not found"; exit 1; }

# Check for generic type parameter <T> or <T,
grep -qE "<T>|<T," "$CALR_FILE" || { echo "Generic type parameter (<T>) not found"; exit 1; }

# Check for generic type usage in input or output
grep -qE "(§I\{T:|§O\{T\})" "$CALR_FILE" || { echo "Generic type usage in §I or §O not found"; exit 1; }

echo "Verification passed: Identity generic function found"
exit 0
