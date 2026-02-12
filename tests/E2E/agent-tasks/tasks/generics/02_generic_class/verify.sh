#!/usr/bin/env bash
# Verify: Generic class
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Generic.calr"

[[ -f "$CALR_FILE" ]] || { echo "Generic.calr not found"; exit 1; }

# Check for Container class
grep -q "Container" "$CALR_FILE" || { echo "Container class not found"; exit 1; }

# Check for class definition with generic
grep -qE "§CL\{.*\}<T>" "$CALR_FILE" || { echo "Generic class (§CL{...}<T>) not found"; exit 1; }

# Check for Get and Set methods
grep -q "Get" "$CALR_FILE" || { echo "Get method not found"; exit 1; }
grep -q "Set" "$CALR_FILE" || { echo "Set method not found"; exit 1; }

echo "Verification passed: Container generic class found"
exit 0
